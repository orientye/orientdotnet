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
        IRpcService rpcService;
        if (CRpcServer.TryGetService(serviceId, out rpcService))
            ProcessMessage(rpcService, ctx, msg);
        
        Console.WriteLine($"CRpcServerHandler recv msg: serviceId={serviceId}, methodId={methodId}");

        ctx.FireChannelRead(msg);
    }

    private async void ProcessMessage(IRpcService rpcService, IChannelHandlerContext ctx, object msg)
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
        await ctx.WriteAndFlushAsync(frame);
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        Console.WriteLine($"******************exception={exception}");
        context.FireExceptionCaught(exception);
    }
}