using CRpc.Async;
using CRpc.Transport;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests.Transport;

public sealed class LoopInboundHandlerTests : CrpcTestBase
{
    [Fact]
    public void ChannelReadPostsInboundMessageToOwnerLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        object? received = null;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            InboundMessageReceived = message => received = message
        };
        var channel = new EmbeddedChannel(new LoopInboundHandler(host));
        var payload = new object();

        channel.WriteInbound(payload);
        loop.Tick();

        Assert.Same(payload, received);
    }

    [Fact]
    public void ExceptionCaughtPostsExceptionToOwnerLoop()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        Exception? received = null;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            ChannelExceptionCaught = exception => received = exception
        };
        var channel = new EmbeddedChannel(new LoopInboundHandler(host));
        var expected = new InvalidOperationException("boom");

        channel.Pipeline.FireExceptionCaught(expected);
        loop.Tick();

        Assert.Same(expected, received);
    }

    private sealed class EmptyPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
        }
    }
}
