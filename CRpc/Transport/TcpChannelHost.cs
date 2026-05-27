using System.Net;
using CRpc.Async;
using DotNetty.Handlers.Logging;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace CRpc.Transport;

public sealed class TcpChannelHost : IAsyncDisposable
{
    private readonly CRpcLoop ownerLoop;
    private readonly IChannelPipelineFactory pipelineFactory;
    private readonly TcpChannelHostOptions options;
    private readonly IEventLoopGroup group;
    private readonly Bootstrap bootstrap;
    private IChannel? channel;

    public TcpChannelHost(
        CRpcLoop ownerLoop,
        IChannelPipelineFactory pipelineFactory,
        TcpChannelHostOptions? options = null)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
        this.pipelineFactory = pipelineFactory ?? throw new ArgumentNullException(nameof(pipelineFactory));
        this.options = options ?? new TcpChannelHostOptions();
        this.options.Validate();
        group = new MultithreadEventLoopGroup(this.options.IoThreadCount);
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

    public CRpcLoop OwnerLoop => ownerLoop;

    public IChannelPipelineFactory PipelineFactory => pipelineFactory;

    public TcpChannelHostOptions Options => options;

    public bool IsConnected => channel is not null && channel.Active;

    public Action<object>? InboundMessageReceived { get; set; }

    public Action? ChannelBecameInactive { get; set; }

    public Action<Exception>? ChannelExceptionCaught { get; set; }

    public CRpcTask<IChannel> ConnectAsync(string host, int port)
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

    private async CRpcTask<IChannel> ConnectCoreAsync(string host, int port)
    {
        Task<IChannel> connectTask = IPAddress.TryParse(host, out var ipAddress)
            ? bootstrap.ConnectAsync(new IPEndPoint(ipAddress, port))
            : bootstrap.ConnectAsync(host, port);

        var connected = await CRpcTask.FromTask(connectTask, ownerLoop);
        channel = connected;
        return connected;
    }

    public CRpcTask WriteAndFlushAsync(object message)
    {
        EnsureOwnerLoopThread();
        ArgumentNullException.ThrowIfNull(message);

        var currentChannel = channel
            ?? throw new InvalidOperationException("TcpChannelHost is not connected.");

        return CRpcTask.FromTask(currentChannel.WriteAndFlushAsync(message), ownerLoop);
    }

    public CRpcTask CloseAsync()
    {
        EnsureOwnerLoopThread();

        var currentChannel = channel;
        channel = null;

        if (currentChannel is null)
        {
            return CRpcTask.CompletedTask(ownerLoop);
        }

        return CRpcTask.FromTask(currentChannel.CloseAsync(), ownerLoop);
    }

    public CRpcTask ShutdownIoAsync()
    {
        EnsureOwnerLoopThread();
        return CRpcTask.FromTask(group.ShutdownGracefullyAsync(), ownerLoop);
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

        var closeAwaiter = CloseAsync().GetAwaiter();
        if (!closeAwaiter.IsCompleted)
        {
            throw new InvalidOperationException(
                "TcpChannelHost.DisposeAsync requires CloseAsync to complete synchronously on the owner loop. " +
                "Await CloseAsync() while driving the loop, then call ShutdownIoAsync().");
        }

        closeAwaiter.GetResult();

        var shutdownAwaiter = ShutdownIoAsync().GetAwaiter();
        while (!shutdownAwaiter.IsCompleted)
        {
            ownerLoop.Tick();
            if (!shutdownAwaiter.IsCompleted)
            {
                ownerLoop.WaitForWorkOrTimer(CancellationToken.None);
            }
        }

        shutdownAwaiter.GetResult();
        return ValueTask.CompletedTask;
    }

    private void EnsureOwnerLoopThread()
    {
        if (!ownerLoop.IsInLoopThread)
        {
            throw new InvalidOperationException(
                "TcpChannelHost operations must run on the owner CRpcLoop thread.");
        }
    }
}
