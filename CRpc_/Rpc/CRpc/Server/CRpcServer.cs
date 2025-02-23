using System.Collections.Concurrent;
using System.Net;
using CRpc.Rpc.CRpc.Codec;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace CRpc.Rpc.CRpc.Server;

public sealed class CRpcServer : IRpcServer
{
    private static readonly int InitialCapacity = 106;
    private static readonly int ConcurrencyLevel = Environment.ProcessorCount * 2;

    private static readonly ConcurrentDictionary<int, IRpcService> services = new(ConcurrencyLevel, InitialCapacity);

    public void Open()
    {
    }

    public void Close()
    {
    }

    public void RegisterService(IRpcService service)
    {
        var serviceId = service.GetServiceId();
        services[serviceId] = service;
    }

    public void UnregisterService(IRpcService service)
    {
    }

    public static bool TryGetService(int serviceId, out IRpcService s)
    {
        var result = services.TryGetValue(serviceId, out s);
        return result;
    }

    public async Task RunAsync()
    {
        IEventLoopGroup group = new MultithreadEventLoopGroup(1);
        IEventLoopGroup workGroup = new MultithreadEventLoopGroup(1);
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

            var bootstrapChannel = await bootstrap.BindAsync(IPAddress.Any, 7999);

            Console.WriteLine($"CRpcServer started, Listening on {bootstrapChannel.LocalAddress}");
            Console.ReadLine();

            await bootstrapChannel.CloseAsync();
        }
        finally
        {
            group.ShutdownGracefullyAsync().Wait();
        }
    }
}