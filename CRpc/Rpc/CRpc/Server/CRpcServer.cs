using System.Net;

using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServer
{
    private readonly CRpcServerOptions options;
    private CancellationTokenSource? runCancellation;
    private IChannel? bootstrapChannel;
    private IEventLoopGroup? group;
    private IEventLoopGroup? workGroup;
    private int isRunning;

    public CRpcServer(CRpcLoop loop, CRpcServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Loop = loop;
        Connections = new CRpcConnectionRegistry(loop);
        this.options = options ?? new CRpcServerOptions();
    }

    public CRpcLoop Loop { get; }

    public CRpcConnectionRegistry Connections { get; }

    public CRpcServerOptions Options => options;

    public bool IsRunning => Volatile.Read(ref isRunning) == 1;

    /// <summary>
    /// Demo host helper: bind, run <see cref="CRpcLoopHost.RunUntilCancelled"/> on the current thread, then stop.
    /// Does not clear loop service registrations. Production hosts should use <see cref="StartAsync"/> +
    /// <see cref="CRpcLoopHost.RunUntilCancelled"/> + <see cref="StopAsync"/>.
    /// </summary>
    public CRpcTask RunAsync(IPAddress address, int port, bool registerConsoleCancelHandler = true)
    {
        EnsureOwnerLoopThread();
        var boundOptions = new CRpcServerOptions
        {
            Address = address,
            Port = port,
            MaxFrameLength = options.MaxFrameLength,
            BossThreadCount = options.BossThreadCount,
            WorkerThreadCount = options.WorkerThreadCount,
            SoBacklog = options.SoBacklog,
        };

        return RunInternalAsync(boundOptions, registerConsoleCancelHandler);
    }

    private async CRpcTask RunInternalAsync(CRpcServerOptions boundOptions, bool registerConsoleCancelHandler)
    {
        using var runCts = new CancellationTokenSource();
        await StartInternalAsync(boundOptions, runCts.Token);

        var currentBootstrapChannel = bootstrapChannel
            ?? throw new InvalidOperationException("CRpcServer did not initialize its bootstrap channel.");
        Console.WriteLine($"CRpcServer started, Listening on {currentBootstrapChannel.LocalAddress}");
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
            var currentRunCancellation = runCancellation
                ?? throw new InvalidOperationException("CRpcServer run cancellation was not initialized.");
            CRpcLoopHost.RunUntilCancelled(Loop, currentRunCancellation.Token);
        }
        finally
        {
            if (cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        await StopInternalAsync();
    }

    public CRpcTask StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureOwnerLoopThread();
        return StartInternalAsync(options, cancellationToken);
    }

    public CRpcTask StopAsync()
    {
        EnsureOwnerLoopThread();
        runCancellation?.Cancel();
        return StopInternalAsync();
    }

    private async CRpcTask StartInternalAsync(CRpcServerOptions startOptions, CancellationToken cancellationToken)
    {
        if (bootstrapChannel is not null)
        {
            throw new InvalidOperationException("CRpcServer is already started.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        group = new MultithreadEventLoopGroup(startOptions.BossThreadCount);
        workGroup = new MultithreadEventLoopGroup(startOptions.WorkerThreadCount);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(group, workGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap
            .Option(ChannelOption.SoBacklog, startOptions.SoBacklog)
            .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
            {
                var handler = options.HandlerFactory?.Invoke(this) ?? new CRpcServerHandler(this);
                new CRpcServerPipelineFactory(startOptions).Configure(channel.Pipeline, handler);
            }));

        try
        {
            bootstrapChannel = await CRpcTask.FromTask(
                bootstrap.BindAsync(startOptions.Address, startOptions.Port),
                Loop);
            Volatile.Write(ref isRunning, 1);
        }
        catch
        {
            await StopInternalAsync();
            throw;
        }
    }

    private async CRpcTask StopInternalAsync()
    {
        var currentBootstrapChannel = bootstrapChannel;
        var currentWorkGroup = workGroup;
        var currentGroup = group;
        var currentRunCancellation = runCancellation;

        bootstrapChannel = null;
        workGroup = null;
        group = null;
        runCancellation = null;
        Volatile.Write(ref isRunning, 0);

        if (currentBootstrapChannel is not null)
        {
            await CRpcTask.FromTask(currentBootstrapChannel.CloseAsync(), Loop);
        }

        if (currentWorkGroup is not null)
        {
            await CRpcTask.FromTask(
                currentWorkGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                Loop);
        }

        if (currentGroup is not null)
        {
            await CRpcTask.FromTask(
                currentGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                Loop);
        }

        currentRunCancellation?.Dispose();
    }

    private void EnsureOwnerLoopThread()
    {
        var loop = CRpcLoop.Current
            ?? throw new InvalidOperationException("CRpcServer lifecycle operations must be called from a bound CRpcLoop thread.");
        if (!ReferenceEquals(Loop, loop))
        {
            throw new InvalidOperationException("CRpcServer lifecycle operations must be called on the server's owner CRpcLoop thread.");
        }
    }
}
