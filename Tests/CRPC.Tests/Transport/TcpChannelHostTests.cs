using CRpc.Async;
using CRpc.Transport;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests.Transport;

public sealed class TcpChannelHostTests : CrpcTestBase
{
    [Fact]
    public void ConnectAsyncThrowsWhenNotOnOwnerLoop()
    {
        var loop = new CRpcLoop();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.ConnectAsync("127.0.0.1", 1));

        Assert.Contains("owner CRpcLoop", exception.Message);
    }

    [Fact]
    public void WriteAndFlushAsyncThrowsWhenNotConnected()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.WriteAndFlushAsync(Unpooled.Empty));

        Assert.Contains("not connected", exception.Message);
    }

    [Fact]
    public void CloseAsyncCompletesWhenNeverConnected()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var closeTask = host.CloseAsync();

        Assert.True(closeTask.GetAwaiter().IsCompleted);
    }

    [Fact]
    public void EmptyPipelineFactoryAddsLoopInboundHandlerToPipeline()
    {
        var loop = new CRpcLoop();
        var factory = new EmptyPipelineFactory();
        var host = new TcpChannelHost(loop, factory);
        var channel = new EmbeddedChannel();

        factory.Configure(channel.Pipeline, host);

        Assert.NotNull(channel.Pipeline.Get<LoopInboundHandler>());
    }

    private sealed class EmptyPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
            pipeline.AddLast(new LoopInboundHandler(host));
        }
    }
}
