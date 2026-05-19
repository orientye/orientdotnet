using System.Diagnostics.CodeAnalysis;
using System.Net;

using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServer : IRpcServer
{
    private readonly CRpcServerOptions options;
    private CancellationTokenSource? runCancellation;
    private IChannel? bootstrapChannel;
    private IEventLoopGroup? group;
    private IEventLoopGroup? workGroup;

    public CRpcServer(CRpcLoop loop, CRpcServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Loop = loop;
        this.options = options ?? new CRpcServerOptions();
    }

    public CRpcLoop Loop { get; }

    public bool IsRunning => bootstrapChannel is not null
        && runCancellation is not null
        && !runCancellation.IsCancellationRequested;

    public void Open()
    {
    }

    public void Close()
    {
        runCancellation?.Cancel();
    }

    public void RegisterService(IRpcService service)
    {
        EnsureLoopThread();
        Loop.RegisterService(service);
    }

    public void UnregisterService(IRpcService service)
    {
        EnsureLoopThread();
        Loop.UnregisterService(service);
    }

    public bool TryGetRegisteredService(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
    {
        EnsureLoopThread();
        return Loop.TryGetService(serviceId, out service);
    }

    internal void ClearRegisteredServices()
    {
        EnsureLoopThread();
        Loop.ClearRegisteredServices();
    }

    private void EnsureLoopThread()
    {
        if (!Loop.IsInLoopThread)
        {
            throw new InvalidOperationException("CRpcServer service registry operations must run on the server loop thread.");
        }
    }

    public async Task RunAsync()
    {
        await RunAsync(options.Address, options.Port).ConfigureAwait(false);
    }

    public async Task RunAsync(IPAddress address, int port, bool registerConsoleCancelHandler = true)
    {
        var boundOptions = new CRpcServerOptions
        {
            Address = address,
            Port = port,
            MaxFrameLength = options.MaxFrameLength,
            HashLength = options.HashLength,
        };

        using var runCts = new CancellationTokenSource();
        runCancellation = runCts;
        group = new MultithreadEventLoopGroup(1);
        workGroup = new MultithreadEventLoopGroup(1);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(group, workGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap
            .Option(ChannelOption.SoBacklog, 8192)
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
            {
                var pipeline = channel.Pipeline;
                pipeline.AddLast("decoder", new CRpcMessageDecoder(boundOptions.MaxFrameLength, boundOptions.HashLength));
                pipeline.AddLast("handler", new CRpcServerHandler(this));
            }));

        bootstrapChannel = await bootstrap.BindAsync(address, port).ConfigureAwait(false);

        Console.WriteLine($"CRpcServer started, Listening on {bootstrapChannel.LocalAddress}");
        Console.WriteLine("Press Ctrl+C to stop.");

        ConsoleCancelEventHandler? cancelHandler = null;
        if (registerConsoleCancelHandler)
        {
            cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                runCts.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
        }

        try
        {
            CRpcLoopHost.RunUntilCancelled(Loop, runCts.Token);
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        if (Loop.IsInLoopThread)
        {
            ClearRegisteredServices();
        }
        else
        {
            Loop.Post(ClearRegisteredServices);
            Loop.Tick();
        }

        await StopInternalAsync().ConfigureAwait(false);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (bootstrapChannel is not null)
        {
            throw new InvalidOperationException("CRpcServer is already started.");
        }

        runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await StartInternalAsync().ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        runCancellation?.Cancel();
        await StopInternalAsync().ConfigureAwait(false);
    }

    private async Task StartInternalAsync()
    {
        if (bootstrapChannel is not null)
        {
            return;
        }

        runCancellation ??= new CancellationTokenSource();
        group = new MultithreadEventLoopGroup(1);
        workGroup = new MultithreadEventLoopGroup(1);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(group, workGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap
            .Option(ChannelOption.SoBacklog, 8192)
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
            {
                var pipeline = channel.Pipeline;
                pipeline.AddLast("decoder", new CRpcMessageDecoder(options.MaxFrameLength, options.HashLength));
                pipeline.AddLast("handler", new CRpcServerHandler(this));
            }));

        bootstrapChannel = await bootstrap.BindAsync(options.Address, options.Port).ConfigureAwait(false);
    }

    private async Task StopInternalAsync()
    {
        if (bootstrapChannel is not null)
        {
            await bootstrapChannel.CloseAsync().ConfigureAwait(false);
            bootstrapChannel = null;
        }

        if (workGroup is not null)
        {
            await workGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            workGroup = null;
        }

        if (group is not null)
        {
            await group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            group = null;
        }

        runCancellation?.Dispose();
        runCancellation = null;
    }
}
