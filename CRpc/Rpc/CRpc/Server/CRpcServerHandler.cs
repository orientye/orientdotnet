using System.Net.Sockets;
using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

public class CRpcServerHandler : ChannelHandlerAdapter
{
    private readonly CRpcServer server;

    public CRpcServerHandler(CRpcServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        this.server = server;
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        var message = (CRpcMessage)msg;

        var serviceId = message.getServiceId();
        var methodId = message.getMethodId();
        server.Loop.Post(() =>
        {
            if (server.Loop.TryGetService(serviceId, out var rpcService))
            {
                ProcessMessage(rpcService, ctx, message);
            }
        });
        
        Console.WriteLine($"CRpcServerHandler recv msg: serviceId={serviceId}, methodId={methodId}");

    }

    private static void ProcessMessage(IRpcService rpcService, IChannelHandlerContext ctx, object msg)
    {
        var task = ProcessMessageAsync(rpcService, ctx, msg);
        var awaiter = task.GetAwaiter();
        if (awaiter.IsCompleted)
        {
            CompleteProcessMessage(awaiter);
            return;
        }

        awaiter.OnCompleted(() => CompleteProcessMessage(awaiter));
    }

    private static async CRpcTask ProcessMessageAsync(IRpcService rpcService, IChannelHandlerContext ctx, object msg)
    {
        var rpcContext = new CRpcContext();
        var request = (CRpcMessage)msg;
        var (resultCode, bytes) = await RpcServiceInvoker.InvokeAsync(rpcService, rpcContext, request);
        var rsp = RpcServiceInvoker.BuildCrpcResponse(request, resultCode, bytes);
        rsp.encryptAndCompress(512, true, true);
        var allocator = ctx.Allocator;
        var size = rsp.getSize();
        Console.WriteLine($"*******************rsp size: {size}");
        Console.WriteLine($"*******************channel: {ctx.Channel}");
        var frame = allocator.DirectBuffer(rsp.getSize());
        rsp.toFrame(frame, 16);
        _ = ctx.WriteAndFlushAsync(frame);
    }

    private static void CompleteProcessMessage(CRpcTask.Awaiter awaiter)
    {
        try
        {
            awaiter.GetResult();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"******************process exception={exception}");
        }
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        Console.WriteLine($"CRpcServerHandler client disconnected: {context.Channel.RemoteAddress}");
        context.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        if (IsRemoteDisconnect(exception))
        {
            Console.WriteLine($"CRpcServerHandler remote disconnected: {exception.Message}");
            return;
        }

        Console.WriteLine($"******************exception={exception}");
        context.FireExceptionCaught(exception);
    }

    private static bool IsRemoteDisconnect(Exception exception)
    {
        return exception is SocketException { SocketErrorCode: SocketError.ConnectionReset }
            || exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset };
    }
}