using Orient.Logging;
using Orient.Runtime;
using Orient.Rpc.Codec;
using Orient.Rpc.Logging;
using Orient.Rpc.Transport;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Client;

public sealed class CRpcClient : IRpcClient, IAsyncDisposable
{
    private readonly Dictionary<long, PendingCall> results = new();
    private readonly Dictionary<(ushort ServiceId, ushort MethodId), CRpcPushHandler> pushHandlers = new();
    private readonly CRpcClientOptions options;
    private readonly TcpChannelHost host;
    private readonly IOrientLogger logger;
    private long reqSequence;
    private readonly OrientExecutor ownerExecutor;

    public Action<CRpcPushContext, Exception>? OnPushException { get; set; }

    public Action<CRpcPushContext>? OnUnhandledPush { get; set; }

    public event Action? ConnectionLost;

    public CRpcClient(OrientExecutor executor, CRpcClientOptions? options = null)
        : this(executor, options ?? new CRpcClientOptions(), createHost: true)
    {
    }

    internal CRpcClient(OrientExecutor executor, CRpcClientOptions options, TcpChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(host);

        options.Validate();

        ownerExecutor = executor;
        this.options = options;
        this.host = host;
        logger = (options.LoggerFactory ?? NullOrientLoggerFactory.Instance)
            .CreateLogger("Orient.Rpc.Client.CRpcClient");
        ConfigureHostCallbacks();
    }

    private CRpcClient(OrientExecutor executor, CRpcClientOptions options, bool createHost)
        : this(executor, options, CreateHost(executor, options))
    {
    }

    public CRpcClientOptions Options => options;

    /// <summary>
    /// Connects to the remote host. DotNetty connect runs on IO threads; the connected
    /// <see cref="IChannel"/> is assigned on the owner executor thread before this task completes.
    /// Must be called on the owner's bound <see cref="OrientExecutor"/> thread while the executor is driven.
    /// </summary>
    public OrientTask<IChannel> ConnectAsync(string host, int port)
    {
        EnsureOwnerExecutorThread();

        if (this.host.IsConnected)
        {
            throw new InvalidOperationException("CRpcClient is already connected.");
        }

        return this.host.ConnectAsync(host, port);
    }

    /// <summary>
    /// Clears the executor-owned channel on the owner executor thread, then closes the underlying
    /// DotNetty channel via <see cref="OrientTask.FromTask(System.Threading.Tasks.Task, OrientExecutor?)"/>.
    /// </summary>
    public OrientTask CloseAsync()
    {
        EnsureOwnerExecutorThread();

        FailPendingCalls(new ConnectionClosedException("CRpcClient channel was closed."));
        return this.host.CloseAsync();
    }

    /// <summary>
    /// Shuts down the DotNetty event loop group after the client channel is closed.
    /// </summary>
    public OrientTask ShutdownIoAsync()
    {
        EnsureOwnerExecutorThread();
        return host.ShutdownIoAsync();
    }

    /// <summary>
    /// Closes the client and shuts down IO. Prefer awaiting <see cref="CloseAsync"/> and
    /// <see cref="ShutdownIoAsync"/> from CRpc async code while driving the executor.
    /// <see cref="IAsyncDisposable"/> is kept for compatibility; it requires close to complete
    /// synchronously on the owner executor thread.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        EnsureOwnerExecutorThread();

        var closeTask = CloseAsync();
        var closeAwaiter = closeTask.GetAwaiter();
        if (!closeAwaiter.IsCompleted)
        {
            throw new InvalidOperationException(
                "CRpcClient.DisposeAsync requires CloseAsync to complete synchronously on the owner executor thread. " +
                "Await CloseAsync() while driving the executor, then call ShutdownIoAsync().");
        }

        closeAwaiter.GetResult();

        var shutdownTask = ShutdownIoAsync();
        var shutdownAwaiter = shutdownTask.GetAwaiter();
        while (!shutdownAwaiter.IsCompleted)
        {
            ownerExecutor.Tick();
            if (!shutdownAwaiter.IsCompleted)
            {
                ownerExecutor.WaitForWorkOrTimer(CancellationToken.None);
            }
        }

        shutdownAwaiter.GetResult();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Sends an RPC request and returns a task that completes when the response arrives or the call times out.
    /// <paramref name="timeout"/> must be a positive value; a timer is registered on the owner executor.
    /// Must be called on the bound owner <see cref="OrientExecutor"/> thread while the executor is driven.
    /// </summary>
    public OrientTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout)
    {
        EnsureOwnerExecutorThread();

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
        var pendingCall = __AddResultTaskAsync(reqSeq, timeout, ownerExecutor);

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
        EnsureOwnerExecutorThread();
        ArgumentNullException.ThrowIfNull(handler);
        pushHandlers[(serviceId, methodId)] = handler;
    }

    internal void OnReceiveResponse(CRpcMessage message)
    {
        ownerExecutor.Post(() => CompleteReceiveResponse(message));
    }

    private void ConfigureHostCallbacks()
    {
        host.InboundMessageReceived = OnHostInboundMessage;
        host.ChannelBecameInactive = OnHostChannelInactive;
        host.ChannelExceptionCaught = OnHostChannelException;
    }

    private static TcpChannelHost CreateHost(OrientExecutor executor, CRpcClientOptions options)
    {
        options.Validate();
        var loggerFactory = options.LoggerFactory ?? NullOrientLoggerFactory.Instance;
        var decoderLogger = loggerFactory.CreateLogger("Orient.Rpc.Codec.CRpcMessageDecoder");

        return new TcpChannelHost(
            executor,
            new CRpcClientPipelineFactory(options, decoderLogger),
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
        ConnectionLost?.Invoke();
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
            case CRpcMessageType.Heartbeat:
                return;
            case CRpcMessageType.Push:
                DispatchPush(message);
                return;
            case CRpcMessageType.Response:
                CompletePendingCall(message);
                return;
            default:
                if (logger.IsEnabled(OrientLogLevel.Warn))
                {
                    logger.Warn(
                        OrientRpcLogEventIds.IgnoredMessageType,
                        $"CRpcClient ignored inbound message type {message.MessageType}: serviceId={message.ServiceId}, methodId={message.MethodId}");
                }
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
        var context = new CRpcPushContext(ownerExecutor, serviceId, methodId);

        if (!pushHandlers.TryGetValue((serviceId, methodId), out var handler))
        {
            if (OnUnhandledPush is not null)
            {
                OnUnhandledPush(context);
            }
            else
            {
                if (logger.IsEnabled(OrientLogLevel.Warn))
                {
                    logger.Warn(
                        OrientRpcLogEventIds.UnhandledPush,
                        $"CRpcClient unhandled push: serviceId={serviceId}, methodId={methodId}");
                }
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

    private void ObservePushHandler(OrientTask task, CRpcPushContext context)
    {
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            CompletePushHandler(awaiter, context);
            return;
        }

        awaiter.OnCompleted(() => CompletePushHandler(awaiter, context));
    }

    private void CompletePushHandler(OrientTask.Awaiter awaiter, CRpcPushContext context)
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

        if (logger.IsEnabled(OrientLogLevel.Error))
        {
            logger.Error(
                OrientRpcLogEventIds.PushHandlerException,
                $"CRpcClient push handler exception: serviceId={context.ServiceId}, methodId={context.MethodId}",
                exception);
        }
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
        var writeTask = host.WriteAndFlushAsync(req);
        var awaiter = writeTask.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            awaiter.GetResult();
        }
    }

    private PendingCall __AddResultTaskAsync(long reqSeq, int timeout, OrientExecutor executor)
    {
        var pendingCall = new PendingCall(executor, new OrientTaskCompletionSource<CRpcMessage>(executor));
        pendingCall.TimeoutTimer = executor.ScheduleDelay(
            timeout,
            () =>
            {
                if (results.Remove(reqSeq, out var removed))
                {
                    if (logger.IsEnabled(OrientLogLevel.Warn))
                    {
                        logger.Warn(
                            OrientRpcLogEventIds.CallTimeout,
                            $"CRpcClient CallAsync timeout: {timeout} ms");
                    }
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

    private void EnsureOwnerExecutorThread()
    {
        var executor = OrientExecutor.Current
            ?? throw new InvalidOperationException("CRpcClient operations must be called from a bound OrientExecutor thread.");
        if (!ReferenceEquals(ownerExecutor, executor))
        {
            throw new InvalidOperationException("CRpcClient operations must be called on the client's owner OrientExecutor thread.");
        }
    }

    private long __IncrementReqId()
    {
        var id = Interlocked.Increment(ref this.reqSequence);
        return id;
    }

    private sealed class PendingCall
    {
        public PendingCall(OrientExecutor executor, OrientTaskCompletionSource<CRpcMessage> source)
        {
            Executor = executor;
            Source = source;
        }

        public OrientExecutor Executor { get; }

        public OrientTaskCompletionSource<CRpcMessage> Source { get; }

        public OrientExecutorTimer? TimeoutTimer { get; set; }
    }
}
