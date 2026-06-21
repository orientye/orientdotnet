using CRpc.Rpc.CRpc.Server;
using DotNetty.Handlers.Timeout;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public sealed class CRpcServerPipelineFactoryTests
{
    [Fact]
    public void ConfigureAddsReadIdleHandlersWhenEnabled()
    {
        var options = new CRpcServerOptions { ReadIdleSeconds = 45 };
        var factory = new CRpcServerPipelineFactory(options);
        var channel = new EmbeddedChannel();

        factory.Configure(channel.Pipeline, new ChannelHandlerAdapter());

        Assert.NotNull(channel.Pipeline.Get<IdleStateHandler>());
        Assert.NotNull(channel.Pipeline.Get<CRpcServerReadIdleHandler>());
        Assert.NotNull(channel.Pipeline.Get<CRpc.Rpc.CRpc.Codec.CRpcMessageDecoder>());
        Assert.NotNull(channel.Pipeline.Get<CRpc.Rpc.CRpc.Codec.CRpcMessageEncoder>());
    }

    [Fact]
    public void ConfigureOmitsIdleHandlersWhenDisabled()
    {
        var options = new CRpcServerOptions { HeartbeatEnabled = false };
        var factory = new CRpcServerPipelineFactory(options);
        var channel = new EmbeddedChannel();

        factory.Configure(channel.Pipeline, new ChannelHandlerAdapter());

        Assert.Null(channel.Pipeline.Get<IdleStateHandler>());
        Assert.Null(channel.Pipeline.Get<CRpcServerReadIdleHandler>());
    }
}
