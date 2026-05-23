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

    public CRpcTask<IChannel> ConnectAsync(string host, int port)
    {
        EnsureOwnerLoopThread();

        if (channel is not null)
        {
            throw new InvalidOperationException("CRpcClient is already connected.");
        }

        var source = new CRpcTaskCompletionSource<IChannel>(ownerLoop);
        var dotnetTask = bootstrap.ConnectAsync(host, port);
        if (dotnetTask.IsCompleted)
        {
            CompleteConnect(dotnetTask, source);
        }
        else
        {
            dotnetTask.ContinueWith(
                completedTask => ownerLoop.Post(() => CompleteConnect(completedTask, source)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return source.Task;
    }

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

    public CRpcTask ShutdownIoAsync()
    {
        EnsureOwnerLoopThread();
        return CRpcTask.FromTask(group.ShutdownGracefullyAsync(), ownerLoop);
    }

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

    private void CompleteConnect(Task<IChannel> task, CRpcTaskCompletionSource<IChannel> source)
    {
        if (task.IsCanceled)
        {
            source.TrySetCanceled();
            return;
        }

        if (task.IsFaulted)
        {
            Exception exception;
            if (task.Exception?.InnerException is not null)
            {
                exception = task.Exception.InnerException;
            }
            else if (task.Exception is not null)
            {
                exception = task.Exception;
            }
            else
            {
                exception = new InvalidOperationException("Connect failed.");
            }

            source.TrySetException(exception);
            return;
        }

        channel = task.Result;
        source.TrySetResult(task.Result);
    }

    private void __Send(IChannel currentChannel, long reqSeq, ushort serviceId, ushort methodId, byte[] bytes)
    {
        CRpcMessageHeader header = CRpcMessageHeader.valueOf(CRpcMessageState.STATE_NONE, 0, reqSeq, serviceId, methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        CRpcMessage req = CRpcMessage.valueOf(header, bytes);
        req.encryptAndCompress(512, true, true);
        var size = req.getSize();
        Console.WriteLine($"*******************rsp size: {size}");
        var frame = currentChannel.Allocator.DirectBuffer(size);
        Console.WriteLine($"*********CallAsync send");
        req.toFrame(frame, 16);
        _ = currentChannel.WriteAndFlushAsync(frame);
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
