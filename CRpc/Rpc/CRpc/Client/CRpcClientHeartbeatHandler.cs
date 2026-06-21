using CRpc.Rpc.CRpc;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;

namespace CRpc.Rpc.CRpc.Client;

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
