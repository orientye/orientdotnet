using System.Collections.Concurrent;
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
    private static readonly int ConcurrencyLevel = Environment.ProcessorCount * 2;

    private static readonly ConcurrentDictionary<int, IRpcService> Services = new(ConcurrencyLevel, InitialCapacity);
    private CancellationTokenSource? runCancellation;
    private IChannel? bootstrapChannel;
    private IEventLoopGroup? group;
    private IEventLoopGroup? workGroup;

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
        var serviceId = service.GetServiceId();
        Services[serviceId] = service;
    }

    public void UnregisterService(IRpcService service)
    {
        var serviceId = service.GetServiceId();
        Services.TryRemove(new KeyValuePair<int, IRpcService>(serviceId, service));
    }

    public static bool TryGetService(int serviceId, [MaybeNullWhen(false)] out IRpcService s)
    {
        var result = Services.TryGetValue(serviceId, out s);
        return result;
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
                    pipeline.AddLast("handler", new CRpcServerHandler());
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
                CRpcServerLoop.RunUntilCancelled(CRpcLoop.Main, cancellation.Token);
            }
            finally
            {
                if (cancelHandler is not null)
                {
                    Console.CancelKeyPress -= cancelHandler;
                }
            }

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