using CRpc.Async;
using CRpc.Transport;
using DotNetty.Transport.Channels.Embedded;
using LordUnion.IntegrationTests.Protocol;

namespace CRPC.Tests.LordUnion;

public sealed class GameServerPipelineFactoryTests
{
    [Fact]
    public void PipelineDecodesFrameAndPostsToHostLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        object? received = null;
        var host = new TcpChannelHost(loop, new GameServerPipelineFactory())
        {
            InboundMessageReceived = message => received = message
        };
        var channel = new EmbeddedChannel();

        host.PipelineFactory.Configure(channel.Pipeline, host);
        var packet = ServerPacketFrame.EncodeFrame(1001, new byte[] { 1, 2, 3 });

        channel.WriteInbound(DotNetty.Buffers.Unpooled.WrappedBuffer(packet));
        loop.Tick();

        var frame = Assert.IsType<GameServerFrame>(received);
        Assert.Equal(1001u, frame.Header0);
        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Body);
    }
}
