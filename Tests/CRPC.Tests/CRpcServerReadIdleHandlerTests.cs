using CRpc.Rpc.CRpc.Server;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public sealed class CRpcServerReadIdleHandlerTests
{
    [Fact]
    public void ReaderIdleEventClosesChannel()
    {
        var channel = new EmbeddedChannel(new CRpcServerReadIdleHandler());
        channel.Pipeline.FireUserEventTriggered(CRpcClientHeartbeatHandlerTests.CreateIdleStateEvent(IdleState.ReaderIdle));

        Assert.False(channel.Active);
    }
}
