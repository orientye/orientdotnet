using System.Net;
using CRpc.Async;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Example.Http;

public sealed class UnifiedServer
{
    private readonly CRpcLoop loop;
    private readonly CRpcServer crpcServer;
    private readonly HelloworldServiceImpl greeter;
    private readonly int port;
    private readonly int maxFrameLength;
    private IChannel? channel;
    private IEventLoopGroup? bossGroup;
    private IEventLoopGroup? workerGroup;

    public UnifiedServer(
        CRpcLoop loop,
        CRpcServer crpcServer,
        HelloworldServiceImpl greeter,
        int port,
        int maxFrameLength = CRpcServerOptions.DefaultMaxFrameLength)
    {
        this.loop = loop;
        this.crpcServer = crpcServer;
        this.greeter = greeter;
        this.port = port;
        this.maxFrameLength = maxFrameLength;
    }

    public CRpcTask StartAsync(CancellationToken cancellationToken = default)
    {
        return StartInternalAsync(cancellationToken);
    }

    private async CRpcTask StartInternalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bossGroup = new MultithreadEventLoopGroup(1);
        workerGroup = new MultithreadEventLoopGroup(1);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(bossGroup, workerGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap.ChildHandler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new PortUnificationHandler(
                loop,
                crpcServer.Connections,
                ctx =>
                {
                    ctx.Channel.Pipeline.AddLast(new CRpcMessageDecoder(maxFrameLength));
                    ctx.Channel.Pipeline.AddLast(new CRpcMessageEncoder());
                    ctx.Channel.Pipeline.AddLast(new CRpcServerHandler(crpcServer));
                },
                ctx =>
                {
                    ctx.Channel.Pipeline.AddLast(new HttpServerCodec());
                    ctx.Channel.Pipeline.AddLast(new HttpObjectAggregator(65536));
                    ctx.Channel.Pipeline.AddLast(new GreeterHttpHandler(loop, crpcServer.Connections, greeter));
                }));
        }));

        channel = await CRpcTask.FromTask(bootstrap.BindAsync(IPAddress.Loopback, port), loop);
    }

    public CRpcTask StopAsync()
    {
        return StopInternalAsync();
    }

    private async CRpcTask StopInternalAsync()
    {
        if (channel is not null)
        {
            await CRpcTask.FromTask(channel.CloseAsync(), loop);
            channel = null;
        }

        if (workerGroup is not null)
        {
            await CRpcTask.FromTask(
                workerGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            workerGroup = null;
        }

        if (bossGroup is not null)
        {
            await CRpcTask.FromTask(
                bossGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            bossGroup = null;
        }
    }
}
