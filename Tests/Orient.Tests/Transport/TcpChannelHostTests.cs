using Orient.Runtime;
using Orient.Rpc.Transport;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;
using DotNetty.Transport.Channels.Sockets;

namespace Orient.Tests.Transport;

public sealed class TcpChannelHostTests : OrientTestBase
{
    [Fact]
    public void ConnectAsyncThrowsWhenNotOnOwnerLoop()
    {
        var loop = new OrientLoop();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.ConnectAsync("127.0.0.1", 1));

        Assert.Contains("owner OrientLoop", exception.Message);
    }

    [Fact]
    public void WriteAndFlushAsyncThrowsWhenNotConnected()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.WriteAndFlushAsync(Unpooled.Empty));

        Assert.Contains("not connected", exception.Message);
    }

    [Fact]
    public void CloseAsyncCompletesWhenNeverConnected()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory());

        var closeTask = host.CloseAsync();

        Assert.True(closeTask.GetAwaiter().IsCompleted);
    }

    [Fact]
    public void EmptyPipelineFactoryAddsLoopInboundHandlerToPipeline()
    {
        var loop = new OrientLoop();
        var factory = new EmptyPipelineFactory();
        var host = new TcpChannelHost(loop, factory);
        var channel = new EmbeddedChannel();

        factory.Configure(channel.Pipeline, host);

        Assert.NotNull(channel.Pipeline.Get<LoopInboundHandler>());
    }

    [Fact]
    public void StaleChannelInactiveDoesNotInvokeCallback()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var staleChannel = new EmbeddedChannel();
        var callbackCount = 0;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            ChannelBecameInactive = () => callbackCount++
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelInactive(staleChannel);
        DrainOwnerLoop(loop);

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void CurrentChannelInactiveInvokesCallback()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var callbackCount = 0;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            ChannelBecameInactive = () => callbackCount++
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelInactive(currentChannel);
        DrainOwnerLoop(loop);

        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void StaleChannelExceptionDoesNotInvokeCallback()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var staleChannel = new EmbeddedChannel();
        var callbackCount = 0;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            ChannelExceptionCaught = _ => callbackCount++
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelException(staleChannel, new InvalidOperationException("boom"));
        DrainOwnerLoop(loop);

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void BorrowedEventLoopGroupDoesNotOwnGroup()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var sharedGroup = new MultithreadEventLoopGroup(1);
        try
        {
            var host = new TcpChannelHost(loop, new EmptyPipelineFactory(), sharedEventLoopGroup: sharedGroup);
            Assert.False(host.OwnsEventLoopGroup);
        }
        finally
        {
            sharedGroup.ShutdownGracefullyAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void ShutdownIoAsyncCompletesImmediatelyWhenBorrowingSharedGroup()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var sharedGroup = new MultithreadEventLoopGroup(1);
        try
        {
            var host = new TcpChannelHost(loop, new EmptyPipelineFactory(), sharedEventLoopGroup: sharedGroup);
            var shutdownTask = host.ShutdownIoAsync();
            Assert.True(shutdownTask.GetAwaiter().IsCompleted);
        }
        finally
        {
            sharedGroup.ShutdownGracefullyAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task DisposeAsyncClosesChannelWithoutShuttingDownBorrowedGroup()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var sharedGroup = new MultithreadEventLoopGroup(1);
        try
        {
            var host = new TcpChannelHost(loop, new EmptyPipelineFactory(), sharedEventLoopGroup: sharedGroup)
            {
                ChannelBecameInactive = () => { }
            };
            SetHostChannel(host, new EmbeddedChannel());

            await host.DisposeAsync();

            Assert.False(host.OwnsEventLoopGroup);
            Assert.False(host.IsConnected);
        }
        finally
        {
            await sharedGroup.ShutdownGracefullyAsync();
        }
    }

    [Fact]
    public void CurrentChannelExceptionInvokesCallback()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var expected = new InvalidOperationException("boom");
        Exception? received = null;
        var host = new TcpChannelHost(loop, new EmptyPipelineFactory())
        {
            ChannelExceptionCaught = exception => received = exception
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelException(currentChannel, expected);
        DrainOwnerLoop(loop);

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

    private static void DrainOwnerLoop(OrientLoop loop)
    {
        loop.Tick();
    }

    private sealed class EmptyPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
            pipeline.AddLast(new LoopInboundHandler(host));
        }
    }
}
