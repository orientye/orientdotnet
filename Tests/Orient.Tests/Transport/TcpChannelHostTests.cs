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
        var executor = new OrientExecutor();
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.ConnectAsync("127.0.0.1", 1));

        Assert.Contains("owner OrientExecutor", exception.Message);
    }

    [Fact]
    public void WriteAndFlushAsyncThrowsWhenNotConnected()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            host.WriteAndFlushAsync(Unpooled.Empty));

        Assert.Contains("not connected", exception.Message);
    }

    [Fact]
    public void CloseAsyncCompletesWhenNeverConnected()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory());

        var closeTask = host.CloseAsync();

        Assert.True(closeTask.GetAwaiter().IsCompleted);
    }

    [Fact]
    public void EmptyPipelineFactoryAddsExecutorInboundHandlerToPipeline()
    {
        var executor = new OrientExecutor();
        var factory = new EmptyPipelineFactory();
        var host = new TcpChannelHost(executor, factory);
        var channel = new EmbeddedChannel();

        factory.Configure(channel.Pipeline, host);

        Assert.NotNull(channel.Pipeline.Get<ExecutorInboundHandler>());
    }

    [Fact]
    public void StaleChannelInactiveDoesNotInvokeCallback()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var staleChannel = new EmbeddedChannel();
        var callbackCount = 0;
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory())
        {
            ChannelBecameInactive = () => callbackCount++
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelInactive(staleChannel);
        DrainOwnerExecutor(executor);

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void CurrentChannelInactiveInvokesCallback()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var callbackCount = 0;
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory())
        {
            ChannelBecameInactive = () => callbackCount++
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelInactive(currentChannel);
        DrainOwnerExecutor(executor);

        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void StaleChannelExceptionDoesNotInvokeCallback()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var staleChannel = new EmbeddedChannel();
        var callbackCount = 0;
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory())
        {
            ChannelExceptionCaught = _ => callbackCount++
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelException(staleChannel, new InvalidOperationException("boom"));
        DrainOwnerExecutor(executor);

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void BorrowedEventLoopGroupDoesNotOwnGroup()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var sharedGroup = new MultithreadEventLoopGroup(1);
        try
        {
            var host = new TcpChannelHost(executor, new EmptyPipelineFactory(), sharedEventLoopGroup: sharedGroup);
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
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var sharedGroup = new MultithreadEventLoopGroup(1);
        try
        {
            var host = new TcpChannelHost(executor, new EmptyPipelineFactory(), sharedEventLoopGroup: sharedGroup);
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
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var sharedGroup = new MultithreadEventLoopGroup(1);
        try
        {
            var host = new TcpChannelHost(executor, new EmptyPipelineFactory(), sharedEventLoopGroup: sharedGroup)
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
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var currentChannel = new EmbeddedChannel();
        var expected = new InvalidOperationException("boom");
        Exception? received = null;
        var host = new TcpChannelHost(executor, new EmptyPipelineFactory())
        {
            ChannelExceptionCaught = exception => received = exception
        };
        SetHostChannel(host, currentChannel);

        host.PostChannelException(currentChannel, expected);
        DrainOwnerExecutor(executor);

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

    private static void DrainOwnerExecutor(OrientExecutor executor)
    {
        executor.Tick();
    }

    private sealed class EmptyPipelineFactory : IChannelPipelineFactory
    {
        public void Configure(IChannelPipeline pipeline, TcpChannelHost host)
        {
            pipeline.AddLast(new ExecutorInboundHandler(host));
        }
    }
}
