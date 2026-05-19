using System.Diagnostics.CodeAnalysis;
using System.Net;

using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServer : IRpcServer
{
    private const int InitialCapacity = 106;

    private readonly Dictionary<ushort, IRpcService> registeredServices;
    private CancellationTokenSource? runCancellation;
    private IChannel? bootstrapChannel;
    private IEventLoopGroup? group;
    private IEventLoopGroup? workGroup;

    public CRpcServer(CRpcLoop loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        Loop = loop;
        registeredServices = new Dictionary<ushort, IRpcService>(InitialCapacity);
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
        var serviceId = service.GetServiceId();
        registeredServices[serviceId] = service;
    }

    public void UnregisterService(IRpcService service)
    {
        EnsureLoopThread();
        var serviceId = service.GetServiceId();
        if (registeredServices.TryGetValue(serviceId, out var registeredService)
            && ReferenceEquals(registeredService, service))
        {
            registeredServices.Remove(serviceId);
        }
    }

    public bool TryGetRegisteredService(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
    {
        EnsureLoopThread();
        return registeredServices.TryGetValue(serviceId, out service);
    }

    internal void ClearRegisteredServices()
    {
        EnsureLoopThread();
        registeredServices.Clear();
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
        await RunAsync(IPAddress.Any, 7999).ConfigureAwait(false);
    }

    public async Task RunAsync(IPAddress address, int port, bool registerConsoleCancelHandler = true)
    {
        var cancellation = new CancellationTokenSource();
        runCancellation = cancellation;
        group = new MultithreadEventLoopGroup(1);
        workGroup = new MultithreadEventLoopGroup(1);
        try
        {
            var bootstrap = new ServerBootstrap();
            bootstrap.Group(group, workGroup);
            bootstrap.Channel<TcpServerSocketChannel>();

            bootstrap
                .Option(ChannelOption.SoBacklog, 8192)
                .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
                {
                    var pipeline = channel.Pipeline;
                    pipeline.AddLast("decoder", new CRpcMessageDecoder(32768, 16));
                    //pipeline.AddLast("encoder", new CRpcMessageEncoder());
                    pipeline.AddLast("handler", new CRpcServerHandler(this));
                    //TODO: 心跳消息
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
                    Close();
                };
                Console.CancelKeyPress += cancelHandler;
            }

            try
            {
                CRpcServerLoop.RunUntilCancelled(Loop, cancellation.Token);
            }
            finally
            {
                if (cancelHandler is not null)
                {
                    Console.CancelKeyPress -= cancelHandler;
                }
            }

            ClearRegisteredServices();

            await bootstrapChannel.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            bootstrapChannel = null;
            cancellation.Dispose();
            if (ReferenceEquals(runCancellation, cancellation))
            {
                runCancellation = null;
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
        }
    }
}