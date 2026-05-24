using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Handlers.Logging;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcClient : IRpcClient, IAsyncDisposable
{
    private readonly Dictionary<long, PendingCall> results = new();
    private readonly IEventLoopGroup group = new MultithreadEventLoopGroup(1);
    private long reqSequence;
    private readonly CRpcLoop ownerLoop;
    private IChannel? channel;

    private readonly Bootstrap bootstrap = new Bootstrap();

    public CRpcClient(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ownerLoop = loop;

        bootstrap
            .Channel<TcpSocketChannel>()
            .Option(ChannelOption.TcpNodelay, true)
            .Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(10))
            .Group(group)
            .Handler(new ActionChannelInitializer<ISocketChannel>(c =>
            {
                var pipeline = c.Pipeline;
                pipeline.AddLast(new LoggingHandler("crpc-client"));
                pipeline.AddLast("timeout", new IdleStateHandler(0, 0, 60));
                pipeline.AddLast("decoder", new CRpcMessageDecoder(32768, 16));
                pipeline.AddLast("handler", new CRpcClientHandler(this));
            }));
    }

    /// <summary>
    /// Connects to the remote host. DotNetty connect runs on IO threads; the connected
    /// <see cref="IChannel"/> is assigned on the owner loop thread before this task completes.
    /// Must be called on the owner's bound <see cref="CRpcLoop"/> thread while the loop is driven.
    /// </summary>
    public CRpcTask<IChannel> ConnectAsync(string host, int port)
    {
        EnsureOwnerLoopThread();

        if (channel is not null)
        {
            throw new InvalidOperationException("CRpcClient is already connected.");
        }

        return ConnectInternalAsync(host, port);
    }

    private async CRpcTask<IChannel> ConnectInternalAsync(string host, int port)
    {
        var connectedChannel = await CRpcTask.FromTask(bootstrap.ConnectAsync(host, port), ownerLoop);
        channel = connectedChannel;
        return connectedChannel;
    }

    /// <summary>
    /// Clears the loop-owned channel on the owner loop thread, then closes the underlying
    /// DotNetty channel via <see cref="CRpcTask.FromTask(System.Threading.Tasks.Task, CRpcLoop?)"/>.
    /// </summary>
    public CRpcTask CloseAsync()
    {
        EnsureOwnerLoopThread();

        var currentChannel = channel;
        if (currentChannel is null)
        {
            return CRpcTask.CompletedTask(ownerLoop);
        }

        channel = null;
        return CRpcTask.FromTask(currentChannel.CloseAsync(), ownerLoop);
    }

    /// <summary>
    /// Shuts down the DotNetty event loop group after the client channel is closed.
    /// </summary>
    public CRpcTask ShutdownIoAsync()
    {
        EnsureOwnerLoopThread();
        return CRpcTask.FromTask(group.ShutdownGracefullyAsync(), ownerLoop);
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
    /// When <paramref name="timeout"/> is greater than zero, a timer is registered on the owner loop.
    /// Must be called on the bound owner <see cref="CRpcLoop"/> thread while the loop is driven.
    /// </summary>
    public CRpcTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout)
    {
        EnsureOwnerLoopThread();

        var currentChannel = channel
            ?? throw new InvalidOperationException("CRpcClient is not connected.");

        long reqSeq = __IncrementReqId();
        var pendingCall = __AddResultTaskAsync(reqSeq, timeout, ownerLoop);

        __Send(currentChannel, reqSeq, serviceId, methodId, body);

        return pendingCall.Source.Task;
    }

    internal void OnReceiveResponse(CRpcMessage message)
    {
        ownerLoop.Post(() => CompleteReceiveResponse(message));
    }

    private void CompleteReceiveResponse(CRpcMessage message)
    {
        var reqSequence = message.getReqSequence();
        if (results.Remove(reqSequence, out var pendingCall))
        {
            pendingCall.TimeoutTimer?.Cancel();
            pendingCall.Source.TrySetResult(message);
        }
    }

    private void __Send(IChannel currentChannel, long reqSeq, ushort serviceId, ushort methodId, byte[] bytes)
    {
        CRpcMessageHeader header = CRpcMessageHeader.valueOf(CRpcMessageState.STATE_NONE, 0, reqSeq, serviceId, methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        CRpcMessage req = CRpcMessage.valueOf(header, bytes);
        req.encryptAndCompress(512, true, true);
        var size = req.getSize();
        Console.WriteLine($"*******************rsp size: {size}");
        Console.WriteLine($"*********CallAsync send");
        ChannelWriteUtil.WriteEncodedFrameFireAndForget(currentChannel, size, frame => req.toFrame(frame, 16));
    }

    private PendingCall __AddResultTaskAsync(long reqSeq, int timeout, CRpcLoop loop)
    {
        var pendingCall = new PendingCall(loop, new CRpcTaskCompletionSource<CRpcMessage>(loop));
        if (timeout > 0)
        {
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
        }

        results[reqSeq] = pendingCall;
        return pendingCall;
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
