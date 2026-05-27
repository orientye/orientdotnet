using DotNetty.Transport.Channels;

namespace CRpc.Transport;

public sealed class LoopInboundHandler : ChannelHandlerAdapter
{
    private readonly TcpChannelHost host;

    public LoopInboundHandler(TcpChannelHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
        host.PostInboundMessage(message);
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        host.PostChannelInactive();
        base.ChannelInactive(context);
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        host.PostChannelException(exception);
        _ = context.CloseAsync();
    }
}
