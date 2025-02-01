using CRPC.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace CRPC.Rpc.CRpc.Client;

public class CRpcClientHandler : ChannelHandlerAdapter
{
    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        var message = (CRpcMessage)msg;
        
        var serviceId = message.getServiceId();
        var methodId = message.getMethodId();
        var reqSequence = message.getReqSequence();
        Console.WriteLine($"CRpcClientHandler recv msg: serviceId={serviceId}, methodId={methodId}, reqSequence={reqSequence}");
        
        CRpcClient.OnReceiveResponse(message);

        ctx.FireChannelRead(msg);
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        Console.WriteLine($"******************exception={exception}");
        context.FireExceptionCaught(exception);
    }
}