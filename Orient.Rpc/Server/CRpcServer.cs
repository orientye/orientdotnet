using System.Net;

using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

using Orient.Runtime;
using Orient.Rpc.Codec;

namespace Orient.Rpc.Server;

public sealed class CRpcServer
{
    private readonly CRpcServerOptions options;
    private CancellationTokenSource? runCancellation;
    private IChannel? bootstrapChannel;
    private IEventLoopGroup? group;
    private IEventLoopGroup? workGroup;
    private int isRunning;

    public CRpcServer(OrientExecutor executor, CRpcServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        Executor = executor;
        Connections = new CRpcConnectionRegistry(executor);
        Services = new RpcServiceRegistry(executor);
        this.options = options ?? new CRpcServerOptions();
    }

    public OrientExecutor Executor { get; }

    public CRpcConnectionRegistry Connections { get; }

    public RpcServiceRegistry Services { get; }

    public CRpcServerOptions Options => options;

    public bool IsRunning => Volatile.Read(ref isRunning) == 1;

    /// <summary>
    /// Demo host helper: bind, run <see cref="OrientExecutorHost.RunUntilCancelled"/> on the current thread, then stop.
    /// Does not clear executor service registrations. Production hosts should use <see cref="StartAsync"/> +
    /// <see cref="OrientExecutorHost.RunUntilCancelled"/> + <see cref="StopAsync"/>.
    /// </summary>
    public OrientTask RunAsync(IPAddress address, int port, bool registerConsoleCancelHandler = true)
    {
        EnsureOwnerExecutorThread();
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

    private async OrientTask RunInternalAsync(CRpcServerOptions boundOptions, bool registerConsoleCancelHandler)
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
            OrientExecutorHost.RunUntilCancelled(Executor, currentRunCancellation.Token);
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

    public OrientTask StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureOwnerExecutorThread();
        return StartInternalAsync(options, cancellationToken);
    }

    public OrientTask StopAsync()
    {
        EnsureOwnerExecutorThread();
        runCancellation?.Cancel();
        return StopInternalAsync();
    }

    private OrientTask StartInternalAsync(CRpcServerOptions startOptions, CancellationToken cancellationToken)
    {
        if (bootstrapChannel is not null)
        {
            throw new InvalidOperationException("CRpcServer is already started.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        startOptions.Validate();

        return StartInternalAsyncCore(startOptions, cancellationToken);
    }

    private async OrientTask StartInternalAsyncCore(CRpcServerOptions startOptions, CancellationToken cancellationToken)
    {
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

        if (startOptions.WriteBufferWarningEnabled)
        {
            bootstrap
                .ChildOption(ChannelOption.WriteBufferLowWaterMark, startOptions.WriteBufferLowWaterMark)
                .ChildOption(ChannelOption.WriteBufferHighWaterMark, startOptions.WriteBufferHighWaterMark);
        }

        try
        {
            bootstrapChannel = await OrientTask.FromTask(
                bootstrap.BindAsync(startOptions.Address, startOptions.Port),
                Executor);
            Volatile.Write(ref isRunning, 1);
        }
        catch
        {
            await StopInternalAsync();
            throw;
        }
    }

    private async OrientTask StopInternalAsync()
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
            await OrientTask.FromTask(currentBootstrapChannel.CloseAsync(), Executor);
        }

        if (currentWorkGroup is not null)
        {
            await OrientTask.FromTask(
                currentWorkGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                Executor);
        }

        if (currentGroup is not null)
        {
            await OrientTask.FromTask(
                currentGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                Executor);
        }

        currentRunCancellation?.Dispose();
    }

    private void EnsureOwnerExecutorThread()
    {
        var executor = OrientExecutor.Current
            ?? throw new InvalidOperationException("CRpcServer lifecycle operations must be called from a bound OrientExecutor thread.");
        if (!ReferenceEquals(Executor, executor))
        {
            throw new InvalidOperationException("CRpcServer lifecycle operations must be called on the server's owner OrientExecutor thread.");
        }
    }
}
