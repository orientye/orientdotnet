using Orient.Runtime;
using Orient.Rpc.Transport;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace Orient.Tests.Transport;

public sealed class ExecutorInboundHandlerTests : OrientTestBase
{
    [Fact]
    public void ChannelReadPostsInboundMessageToOwnerExecutor()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        object? received = null;
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory())
        {
            InboundMessageReceived = message => received = message
        };
        var channel = new EmbeddedChannel(new ExecutorInboundHandler(host));
        var payload = new object();

        channel.WriteInbound(payload);
        executor.Tick();

        Assert.Same(payload, received);
    }

    [Fact]
    public void ExceptionCaughtPostsExceptionToOwnerExecutor()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        Exception? received = null;
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory())
        {
            ChannelExceptionCaught = exception => received = exception
        };
        var channel = new EmbeddedChannel(new ExecutorInboundHandler(host));
        SetHostChannel(host, channel);
        var expected = new InvalidOperationException("boom");

        channel.Pipeline.FireExceptionCaught(expected);
        executor.Tick();

        Assert.Same(expected, received);
    }

    private static void SetHostChannel(TcpChannelHost host, IChannel channel)
    {
        var field = typeof(TcpChannelHost).GetField(
            "channel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(host, channel);
    }

    private sealed class EmptyPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
        }
    }
}
