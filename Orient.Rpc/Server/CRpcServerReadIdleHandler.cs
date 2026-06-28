using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

internal sealed class CRpcServerReadIdleHandler : ChannelHandlerAdapter
{
    public override void UserEventTriggered(IChannelHandlerContext context, object evt)
    {
        if (evt is IdleStateEvent idleStateEvent
            && idleStateEvent.State == IdleState.ReaderIdle)
        {
            _ = context.CloseAsync();
            return;
        }

        base.UserEventTriggered(context, evt);
    }
}
