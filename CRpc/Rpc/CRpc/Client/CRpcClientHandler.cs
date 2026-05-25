using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Client;

public class CRpcClientHandler : ChannelHandlerAdapter
{
    private readonly CRpcClient client;

    public CRpcClientHandler(CRpcClient client)
    {
        this.client = client;
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        var message = (CRpcMessage)msg;
        
        var serviceId = message.getServiceId();
        var methodId = message.getMethodId();
        var reqSequence = message.getReqSequence();
        Console.WriteLine($"CRpcClientHandler recv msg: serviceId={serviceId}, methodId={methodId}, reqSequence={reqSequence}");
        
        client.OnReceiveResponse(message);

        ctx.FireChannelRead(msg);
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        client.OnChannelInactive(context.Channel);
        context.FireChannelInactive();
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        client.OnChannelException(context.Channel, exception);
        context.CloseAsync();
    }
}