using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Transport;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcClient : IRpcClient, IAsyncDisposable
{
    private readonly Dictionary<long, PendingCall> results = new();
    private readonly Dictionary<(ushort ServiceId, ushort MethodId), CRpcPushHandler> pushHandlers = new();
    private readonly CRpcClientOptions options;
    private readonly TcpChannelHost host;
    private long reqSequence;
    private readonly CRpcLoop ownerLoop;

    public Action<CRpcPushContext, Exception>? OnPushException { get; set; }

    public Action<CRpcPushContext>? OnUnhandledPush { get; set; }

    public CRpcClient(CRpcLoop loop, CRpcClientOptions? options = null)
        : this(loop, options ?? new CRpcClientOptions(), createHost: true)
    {
    }

    internal CRpcClient(CRpcLoop loop, CRpcClientOptions options, TcpChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);

        ownerLoop = loop;
        this.options = options;
        this.host = host;
        ConfigureHostCallbacks();
    }

    private CRpcClient(CRpcLoop loop, CRpcClientOptions options, bool createHost)
        : this(loop, options, CreateHost(loop, options))
    {
    }

    public CRpcClientOptions Options => options;

    /// <summary>
    /// Connects to the remote host. DotNetty connect runs on IO threads; the connected
    /// <see cref="IChannel"/> is assigned on the owner loop thread before this task completes.
    /// Must be called on the owner's bound <see cref="CRpcLoop"/> thread while the loop is driven.
    /// </summary>
    public CRpcTask<IChannel> ConnectAsync(string host, int port)
    {
        EnsureOwnerLoopThread();

        if (this.host.IsConnected)
        {
            throw new InvalidOperationException("CRpcClient is already connected.");
        }

        return this.host.ConnectAsync(host, port);
    }

    /// <summary>
    /// Clears the loop-owned channel on the owner loop thread, then closes the underlying
    /// DotNetty channel via <see cref="CRpcTask.FromTask(System.Threading.Tasks.Task, CRpcLoop?)"/>.
    /// </summary>
    public CRpcTask CloseAsync()
    {
        EnsureOwnerLoopThread();

        FailPendingCalls(new ConnectionClosedException("CRpcClient channel was closed."));
        return this.host.CloseAsync();
    }

    /// <summary>
    /// Shuts down the DotNetty event loop group after the client channel is closed.
    /// </summary>
    public CRpcTask ShutdownIoAsync()
    {
        EnsureOwnerLoopThread();
        return host.ShutdownIoAsync();
    }

    /// <summary>
    /// Closes the client and shuts down IO. Prefer awaiting <see cref="CloseAsync"/> and
    /// <see cref="ShutdownIoAsync"/> from CRpc async code while driving the loop.
    /// <see cref="IAsyncDisposable"/> is kept for compatibility; it requires close to complete
    /// synchronously on the owner loop thread.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        EnsureOwnerLoopThread();

        var closeTask = CloseAsync();
        var closeAwaiter = closeTask.GetAwaiter();
        if (!closeAwaiter.IsCompleted)
        {
            throw new InvalidOperationException(
                "CRpcClient.DisposeAsync requires CloseAsync to complete synchronously on the owner loop thread. " +
                "Await CloseAsync() while driving the loop, then call ShutdownIoAsync().");
        }

        closeAwaiter.GetResult();

        var shutdownTask = ShutdownIoAsync();
        var shutdownAwaiter = shutdownTask.GetAwaiter();
        while (!shutdownAwaiter.IsCompleted)
        {
            ownerLoop.Tick();
            if (!shutdownAwaiter.IsCompleted)
            {
                ownerLoop.WaitForWorkOrTimer(CancellationToken.None);
            }
        }

        shutdownAwaiter.GetResult();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sends an RPC request and returns a task that completes when the response arrives or the call times out.
    /// <paramref name="timeout"/> must be a positive value; a timer is registered on the owner loop.
    /// Must be called on the bound owner <see cref="CRpcLoop"/> thread while the loop is driven.
    /// </summary>
    public CRpcTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout)
    {
        EnsureOwnerLoopThread();

        if (timeout <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "CRpcClient.CallAsync requires an explicit positive timeout.");
        }

        if (!host.IsConnected)
        {
            throw new InvalidOperationException("CRpcClient is not connected.");
        }

        long reqSeq = __IncrementReqId();
        var pendingCall = __AddResultTaskAsync(reqSeq, timeout, ownerLoop);

        try
        {
            __Send(reqSeq, serviceId, methodId, body);
        }
        catch (Exception exception)
        {
            FailPendingCall(reqSeq, exception);
            throw;
        }

        return pendingCall.Source.Task;
    }

    public void RegisterPushHandler(ushort serviceId, ushort methodId, CRpcPushHandler handler)
    {
        EnsureOwnerLoopThread();
        ArgumentNullException.ThrowIfNull(handler);
        pushHandlers[(serviceId, methodId)] = handler;
    }

    internal void OnReceiveResponse(CRpcMessage message)
    {
        ownerLoop.Post(() => CompleteReceiveResponse(message));
    }

    private void ConfigureHostCallbacks()
    {
        host.InboundMessageReceived = OnHostInboundMessage;
        host.ChannelBecameInactive = OnHostChannelInactive;
        host.ChannelExceptionCaught = OnHostChannelException;
    }

    private static TcpChannelHost CreateHost(CRpcLoop loop, CRpcClientOptions options)
    {
        return new TcpChannelHost(
            loop,
            new CRpcClientPipelineFactory(options),
            new TcpChannelHostOptions
            {
                IoThreadCount = options.IoThreadCount,
                ConnectTimeoutSeconds = options.ConnectTimeoutSeconds,
                TcpNoDelay = true,
                LoggingName = "crpc-client",
            });
    }

    private void OnHostInboundMessage(object message)
    {
        if (message is not CRpcMessage response)
        {
            OnHostChannelException(new InvalidOperationException(
                $"CRpcClient received unexpected inbound message type '{message.GetType().FullName}'."));
            return;
        }

        CompleteReceiveResponse(response);
    }

    private void OnHostChannelInactive()
    {
        FailPendingCalls(new ConnectionClosedException("CRpcClient channel became inactive."));
    }

    private void OnHostChannelException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        FailPendingCalls(new ConnectionClosedException(
            "CRpcClient channel encountered an exception.",
            exception));
    }

    private void CompleteReceiveResponse(CRpcMessage message)
    {
        switch (message.MessageType)
        {
            case CRpcMessageType.Push:
                DispatchPush(message);
                return;
            case CRpcMessageType.Response:
                CompletePendingCall(message);
                return;
            default:
                Console.WriteLine(
                    $"CRpcClient ignored inbound message type {message.MessageType}: serviceId={message.ServiceId}, methodId={message.MethodId}");
                return;
        }
    }

    private void CompletePendingCall(CRpcMessage message)
    {
        var reqSequence = message.ReqSequence;
        if (results.Remove(reqSequence, out var pendingCall))
        {
            pendingCall.TimeoutTimer?.Cancel();
            pendingCall.Source.TrySetResult(message);
        }
    }

    private void DispatchPush(CRpcMessage message)
    {
        var serviceId = message.ServiceId;
        var methodId = message.MethodId;
        var context = new CRpcPushContext(ownerLoop, serviceId, methodId);

        if (!pushHandlers.TryGetValue((serviceId, methodId), out var handler))
        {
            if (OnUnhandledPush is not null)
            {
                OnUnhandledPush(context);
            }
            else
            {
                Console.WriteLine($"CRpcClient unhandled push: serviceId={serviceId}, methodId={methodId}");
            }

            return;
        }

        try
        {
            var task = handler(context, message.Body);
            ObservePushHandler(task, context);
        }
        catch (Exception exception)
        {
            ReportPushException(context, exception);
        }
    }

    private void ObservePushHandler(CRpcTask task, CRpcPushContext context)
    {
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            CompletePushHandler(awaiter, context);
            return;
        }

        awaiter.OnCompleted(() => CompletePushHandler(awaiter, context));
    }

    private void CompletePushHandler(CRpcTask.Awaiter awaiter, CRpcPushContext context)
    {
        try
        {
            awaiter.GetResult();
        }
        catch (Exception exception)
        {
            ReportPushException(context, exception);
        }
    }

    private void ReportPushException(CRpcPushContext context, Exception exception)
    {
        if (OnPushException is not null)
        {
            OnPushException(context, exception);
            return;
        }

        Console.WriteLine(
            $"CRpcClient push handler exception: serviceId={context.ServiceId}, methodId={context.MethodId}, exception={exception}");
    }

    private void __Send(long reqSeq, ushort serviceId, ushort methodId, byte[] bytes)
    {
        var req = CRpcMessage.Create(
            CRpcMessageType.Request,
            serviceId,
            methodId,
            reqSeq,
            resultCode: 0,
            bytes);
        Console.WriteLine($"*********CallAsync send");
        var writeTask = host.WriteAndFlushAsync(req);
        var awaiter = writeTask.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            awaiter.GetResult();
        }
    }

    private PendingCall __AddResultTaskAsync(long reqSeq, int timeout, CRpcLoop loop)
    {
        var pendingCall = new PendingCall(loop, new CRpcTaskCompletionSource<CRpcMessage>(loop));
        pendingCall.TimeoutTimer = loop.ScheduleDelay(
            timeout,
            () =>
            {
                if (results.Remove(reqSeq, out var removed))
                {
                    Console.WriteLine($"*********CallAsync timeout: {timeout}");
                    removed.Source.TrySetException(new TimeoutException());
                }
            });

        results[reqSeq] = pendingCall;
        return pendingCall;
    }

    private void FailPendingCall(long reqSeq, Exception exception)
    {
        if (results.Remove(reqSeq, out var pendingCall))
        {
            pendingCall.TimeoutTimer?.Cancel();
            pendingCall.Source.TrySetException(exception);
        }
    }

    private void FailPendingCalls(Exception exception)
    {
        if (results.Count == 0)
        {
            return;
        }

        var pendingCalls = results.Values.ToArray();
        results.Clear();

        foreach (var pendingCall in pendingCalls)
        {
            pendingCall.TimeoutTimer?.Cancel();
            pendingCall.Source.TrySetException(exception);
        }
    }

    private void EnsureOwnerLoopThread()
    {
        var loop = CRpcLoop.Current
            ?? throw new InvalidOperationException("CRpcClient operations must be called from a bound CRpcLoop thread.");
        if (!ReferenceEquals(ownerLoop, loop))
        {
            throw new InvalidOperationException("CRpcClient operations must be called on the client's owner CRpcLoop thread.");
        }
    }

    private long __IncrementReqId()
    {
        var id = Interlocked.Increment(ref this.reqSequence);
        return id;
    }

    private sealed class PendingCall
    {
        public PendingCall(CRpcLoop loop, CRpcTaskCompletionSource<CRpcMessage> source)
        {
            Loop = loop;
            Source = source;
        }

        public CRpcLoop Loop { get; }

        public CRpcTaskCompletionSource<CRpcMessage> Source { get; }

        public CRpcLoopTimer? TimeoutTimer { get; set; }
    }
}
