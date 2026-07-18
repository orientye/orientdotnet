using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Codec;
using Orient.Rpc.Transport;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels.Embedded;

namespace Orient.Tests;

public sealed class CRpcClientPipelineFactoryTests : OrientTestBase
{
    [Fact]
    public void ConfigureAddsClientCodecAndExecutorInboundHandler()
    {
        var options = new CRpcClientOptions
        {
            HeartbeatIntervalSeconds = 17,
            MaxFrameLength = 4096,
        };
        var executor = new OrientExecutor();
        var host = new TcpChannelHost(executor, new CRpcClientPipelineFactory(options));
        var channel = new EmbeddedChannel();

        host.PipelineFactory.Configure(channel.Pipeline, host);

        Assert.NotNull(channel.Pipeline.Get<IdleStateHandler>());
        Assert.NotNull(channel.Pipeline.Get<CRpcClientHeartbeatHandler>());
        Assert.NotNull(channel.Pipeline.Get<CRpcMessageDecoder>());
        Assert.NotNull(channel.Pipeline.Get<CRpcMessageEncoder>());
        Assert.NotNull(channel.Pipeline.Get<ExecutorInboundHandler>());
    }

    [Fact]
    public void ConfigureOmitsIdleHandlersWhenDisabled()
    {
        var options = new CRpcClientOptions { HeartbeatEnabled = false };
        var executor = new OrientExecutor();
        var host = new TcpChannelHost(executor, new CRpcClientPipelineFactory(options));
        var channel = new EmbeddedChannel();

        host.PipelineFactory.Configure(channel.Pipeline, host);

        Assert.Null(channel.Pipeline.Get<IdleStateHandler>());
        Assert.Null(channel.Pipeline.Get<CRpcClientHeartbeatHandler>());
    }

    [Fact]
    public void ConstructorThrowsWhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CRpcClientPipelineFactory(null!));
    }
}
