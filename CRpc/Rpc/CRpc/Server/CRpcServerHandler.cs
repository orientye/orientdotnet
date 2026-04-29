using System.Net.Sockets;
using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Server;

public class CRpcServerHandler : ChannelHandlerAdapter
{
    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        var message = (CRpcMessage)msg;

        var serviceId = message.getServiceId();
        var methodId = message.getMethodId();
        if (CRpcServer.TryGetService(serviceId, out var rpcService))
        {
            CRpcLoop.Main.Post(() => ProcessMessage(rpcService, ctx, msg));
        }
        
        Console.WriteLine($"CRpcServerHandler recv msg: serviceId={serviceId}, methodId={methodId}");

        ctx.FireChannelRead(msg);
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
        var t = await rpcService.OnMessageAsync(rpcContext, (CRpcMessage)msg);
        var resultCode = t.Item1;
        var bytes = t.Item2;

        var rsp = (CRpcMessage)msg;
        rsp.toResponse(resultCode);
        rsp.setBody(bytes);

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