using System.Net;
using Orient.Runtime;
using DotNetty.Handlers.Logging;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Orient.Rpc.Transport;

public sealed class TcpChannelHost : IAsyncDisposable
{
    private readonly OrientLoop ownerLoop;
    private readonly IChannelPipelineFactory pipelineFactory;
    private readonly TcpChannelHostOptions options;
    private readonly IEventLoopGroup group;
    private readonly bool ownsEventLoopGroup;
    private readonly Bootstrap bootstrap;
    private IChannel? channel;

    /// <param name="sharedEventLoopGroup">
    /// When set, this host borrows the group and does not shut it down on <see cref="ShutdownIoAsync"/> /
    /// <see cref="DisposeAsync"/>. <see cref="TcpChannelHostOptions.IoThreadCount"/> is ignored for sizing.
    /// </param>
    public TcpChannelHost(
        OrientLoop ownerLoop,
        IChannelPipelineFactory pipelineFactory,
        TcpChannelHostOptions? options = null,
        IEventLoopGroup? sharedEventLoopGroup = null)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
        this.pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        this.options = options ?? new TcpChannelHostOptions();
        this.options.Validate();
        ownsEventLoopGroup = sharedEventLoopGroup is null;
        group = sharedEventLoopGroup ?? new MultithreadEventLoopGroup(this.options.IoThreadCount);
        bootstrap = new Bootstrap()
            .Channel<TcpSocketChannel>()
            .Option(ChannelOption.TcpNodelay, this.options.TcpNoDelay)
            .Option(ChannelOption.ConnectTimeout, TimeSpan.FromSeconds(this.options.ConnectTimeoutSeconds))
            .Group(group)
            .Handler(new ActionChannelInitializer<ISocketChannel>(socket =>
            {
                var pipeline = socket.Pipeline;
                pipeline.AddLast(new LoggingHandler(this.options.LoggingName));
                this.pipelineFactory.Configure(pipeline, this);
            }));
    }

    public OrientLoop OwnerLoop => ownerLoop;

    public IChannelPipelineFactory PipelineFactory => pipelineFactory;

    public TcpChannelHostOptions Options => options;

    internal bool OwnsEventLoopGroup => ownsEventLoopGroup;

    public bool IsConnected => channel is not null && channel.Active;

    public Action<object>? InboundMessageReceived { get; set; }

    public Action? ChannelBecameInactive { get; set; }

    public Action<Exception>? ChannelExceptionCaught { get; set; }

    public OrientTask<IChannel> ConnectAsync(string host, int port)
    {
        EnsureOwnerLoopThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "TCP port must be in range 1..65535.");
        }

        if (channel is not null)
        {
            throw new InvalidOperationException("TcpChannelHost is already connected.");
        }

        return ConnectCoreAsync(host, port);
    }

    private async OrientTask<IChannel> ConnectCoreAsync(string host, int port)
    {
        Task<IChannel> connectTask = IPAddress.TryParse(host, out var ipAddress)
            ? bootstrap.ConnectAsync(new IPEndPoint(ipAddress, port))
            : bootstrap.ConnectAsync(host, port);

        var connected = await OrientTask.FromTask(connectTask, ownerLoop);
        channel = connected;
        return connected;
    }

    public OrientTask WriteAndFlushAsync(object message)
    {
        EnsureOwnerLoopThread();
        ArgumentNullException.ThrowIfNull(message);

        var currentChannel = channel
            ?? throw new InvalidOperationException("TcpChannelHost is not connected.");

        return OrientTask.FromTask(currentChannel.WriteAndFlushAsync(message), ownerLoop);
    }

    public OrientTask CloseAsync()
    {
        EnsureOwnerLoopThread();

        var currentChannel = channel;
        channel = null;

        if (currentChannel is null)
        {
            return OrientTask.CompletedTask(ownerLoop);
        }

        return OrientTask.FromTask(currentChannel.CloseAsync(), ownerLoop);
    }

    public OrientTask ShutdownIoAsync()
    {
        EnsureOwnerLoopThread();
        if (!ownsEventLoopGroup)
        {
            return OrientTask.CompletedTask(ownerLoop);
        }

        return OrientTask.FromTask(group.ShutdownGracefullyAsync(), ownerLoop);
    }

    internal void PostInboundMessage(object message)
    {
        ownerLoop.Post(() => InboundMessageReceived?.Invoke(message));
    }

    internal void PostChannelInactive(IChannel eventChannel)
    {
        ArgumentNullException.ThrowIfNull(eventChannel);
        ownerLoop.Post(() =>
        {
            if (!ReferenceEquals(channel, eventChannel))
            {
                return;
            }

            ChannelBecameInactive?.Invoke();
        });
    }

    internal void PostChannelException(IChannel eventChannel, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(eventChannel);
        ArgumentNullException.ThrowIfNull(exception);
        ownerLoop.Post(() =>
        {
            if (!ReferenceEquals(channel, eventChannel))
            {
                return;
            }

            ChannelExceptionCaught?.Invoke(exception);
        });
    }

    public ValueTask DisposeAsync()
    {
        EnsureOwnerLoopThread();

        PumpAwaitableOnOwnerLoop(CloseAsync());

        if (ownsEventLoopGroup)
        {
            PumpAwaitableOnOwnerLoop(ShutdownIoAsync());
        }

        return ValueTask.CompletedTask;
    }

    private void PumpAwaitableOnOwnerLoop(OrientTask task)
    {
        var awaiter = task.GetAwaiter();
        while (!awaiter.IsCompleted)
        {
            ownerLoop.Tick();
            if (!awaiter.IsCompleted)
            {
                ownerLoop.WaitForWorkOrTimer(CancellationToken.None);
            }
        }

        awaiter.GetResult();
    }

    private void EnsureOwnerLoopThread()
    {
        if (!ownerLoop.IsInLoopThread)
        {
            throw new InvalidOperationException(
                "TcpChannelHost operations must run on the owner OrientLoop thread.");
        }
    }
}
