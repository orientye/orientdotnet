using DotNetty.Transport.Channels;

namespace Orient.Rpc.Transport;

public sealed class ExecutorInboundHandler : ChannelHandlerAdapter
{
    private readonly TcpChannelHost host;

    public ExecutorInboundHandler(TcpChannelHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public override void ChannelRead(IChannelHandlerContext context, object message)
    {
        host.PostInboundMessage(message);
    }

    public override void ChannelInactive(IChannelHandlerContext context)
    {
        host.PostChannelInactive(context.Channel);
        base.ChannelInactive(context);
    }

    public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
    {
        host.PostChannelException(context.Channel, exception);
        _ = context.CloseAsync();
    }
}
