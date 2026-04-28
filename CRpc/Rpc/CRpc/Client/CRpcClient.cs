using System.Collections.Concurrent;
using System.Net;
using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Handlers.Logging;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcClient : IRpcClient
{
    private readonly ConcurrentDictionary<long, PendingCall> results = new();
    private long reqSequence;
    private IChannel? channel;

    private readonly Bootstrap bootstrap = new Bootstrap();

    public CRpcClient()
    {
        bootstrap
            .Channel<TcpSocketChannel>()
            .Option(ChannelOption.TcpNodelay, true)
            .Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(10))
            .Group(new MultithreadEventLoopGroup(1))
            .Handler(new ActionChannelInitializer<ISocketChannel>(c =>
            {
                var pipeline = c.Pipeline;
                pipeline.AddLast(new LoggingHandler("crpc-client"));
                pipeline.AddLast("timeout", new IdleStateHandler(0, 0, 60));
                pipeline.AddLast("decoder", new CRpcMessageDecoder(32768, 16));
                pipeline.AddLast("handler", new CRpcClientHandler(this));
            }));
    }

    public async Task<IChannel> ConnectAsync(string host, int port)
    {
        channel = await bootstrap.ConnectAsync(host, port);
        return channel;
    }
    
    public async Task<IChannel> ConnectAsync(IPAddress inetHost, int inetPort)
    {
        channel = await bootstrap.ConnectAsync(inetHost, inetPort);
        return channel;
    }

    public CRpcTask<CRpcMessage> CallAsync(short serviceId, short methodId, byte[] body, int timeout)
    {
        var loop = CRpcLoop.Current
            ?? throw new InvalidOperationException("CRpcClient.CallAsync must be called from a bound CRpcLoop thread.");

        long reqSeq = __IncrementReqId();
        var pendingCall = __AddResultTaskAsync(reqSeq, timeout, loop);
        
        __Send(reqSeq, serviceId, methodId, body);

        return pendingCall.Source.Task;
    }

    internal void OnReceiveResponse(CRpcMessage message)
    {
        var reqSequence = message.getReqSequence();
        if (results.TryRemove(reqSequence, out var pendingCall))
        {
            pendingCall.TimeoutTimer?.Dispose();
            pendingCall.Source.TrySetResult(message);
        }
    }

    private void __Send(long reqSeq, short serviceId, short methodId, byte[] bytes)
    {
        CRpcMessageHeader header = CRpcMessageHeader.valueOf(CRpcMessageState.STATE_NONE, 0, reqSeq, serviceId, methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        CRpcMessage req = CRpcMessage.valueOf(header, bytes);
        req.encryptAndCompress(512, true, true);
        var allocator = channel?.Allocator;
        var size = req.getSize();
        Console.WriteLine($"*******************rsp size: {size}");
        var frame = allocator?.DirectBuffer(size);
        if (frame != null)
        {
            Console.WriteLine($"*********CallAsync send");
            req.toFrame(frame, 16);
            channel?.WriteAndFlushAsync(frame);
        }
    }

    private PendingCall __AddResultTaskAsync(long reqSeq, int timeout, CRpcLoop loop)
    {
        var pendingCall = new PendingCall(new CRpcTaskCompletionSource<CRpcMessage>(loop));
        if (timeout > 0)
        {
            pendingCall.TimeoutTimer = new Timer(
                _ =>
                {
                    if (results.TryRemove(reqSeq, out var removed))
                    {
                        Console.WriteLine($"*********CallAsync timeout: {timeout}");
                        removed.Source.TrySetException(new TimeoutException());
                    }
                },
                null,
                timeout,
                Timeout.Infinite);
        }

        results[reqSeq] = pendingCall;
        return pendingCall;
    }
    
    private long __IncrementReqId()
    {
        var id = Interlocked.Increment(ref this.reqSequence);
        return id;
    }

    private sealed class PendingCall
    {
        public PendingCall(CRpcTaskCompletionSource<CRpcMessage> source)
        {
            Source = source;
        }

        public CRpcTaskCompletionSource<CRpcMessage> Source { get; }

        public Timer? TimeoutTimer { get; set; }
    }
}