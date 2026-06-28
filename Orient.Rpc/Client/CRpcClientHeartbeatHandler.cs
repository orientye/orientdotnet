using Orient.Rpc.CRpc;
using Orient.Rpc.Codec;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Client;

internal sealed class CRpcClientHeartbeatHandler : ChannelHandlerAdapter
{
    public override void UserEventTriggered(IChannelHandlerContext context, object evt)
    {
        if (evt is IdleStateEvent idleStateEvent
            && idleStateEvent.State == IdleState.WriterIdle)
        {
            ChannelWriteUtil.WriteAndFlushFireAndForget(context, CRpcMessage.CreateHeartbeat());
            return;
        }

        base.UserEventTriggered(context, evt);
    }
}
