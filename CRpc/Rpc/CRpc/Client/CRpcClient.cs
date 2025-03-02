using System.Collections.Concurrent;
using System.Net;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Handlers.Logging;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace CRpc.Rpc.CRpc.Client;

public sealed class CRpcClient : IRpcClient
{
    private static readonly ConcurrentDictionary<long, TaskCompletionSource<CRpcMessage>> results =
        new ConcurrentDictionary<long, TaskCompletionSource<CRpcMessage>>();
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
                pipeline.AddLast("handler", new CRpcClientHandler());
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

    //public async Task<(int, byte[])> CallAsync(short serviceId, short methodId, byte[] bytes, int timeout)
    public async Task<CRpcMessage> CallAsync(short serviceId, short methodId, byte[] body, int timeout)
    {
        long reqSeq = __IncrementReqId();
        var respTask = __AddResultTaskAsync(reqSeq);
        
        __Send(reqSeq, serviceId, methodId, body);
        
        Task timeoutTask = Task.Delay(timeout);
        Task completedTask = await Task.WhenAny(respTask, timeoutTask);
        if (completedTask == timeoutTask)
        {
            Console.WriteLine($"*********CallAsync timeout: {timeout}");
            throw new TimeoutException();
        }
        var result = await respTask;
        return result;
    }

    public static void OnReceiveResponse(CRpcMessage message)
    {
        var serviceId = message.getServiceId();
        var methodId = message.getMethodId();
        var reqSequence = message.getReqSequence();
        //TaskCompletionSource<CRpcMessage> tcs;
        if (results.TryGetValue(reqSequence, out TaskCompletionSource<CRpcMessage> tcs))
        {
            tcs.SetResult(message);
        }
    }

    private void __Send(long reqSeq, short serviceId, short methodId, byte[] bytes)
    {
        CRpcMessageHeader header = CRpcMessageHeader.valueOf(CRpcMessageState.STATE_NONE, 0, reqSequence, serviceId, methodId);
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

    private Task<CRpcMessage> __AddResultTaskAsync(long reqSeq)
    {
        var tcs = new TaskCompletionSource<CRpcMessage>();
        var task = tcs.Task;
        results[reqSeq] = tcs;
        return task;
    }
    
    private long __IncrementReqId()
    {
        var id = Interlocked.Increment(ref this.reqSequence);
        return id;
    }
}