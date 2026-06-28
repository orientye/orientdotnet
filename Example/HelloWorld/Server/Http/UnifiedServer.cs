using System.Net;
using Orient.Runtime;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Example.Http;

public sealed class UnifiedServer
{
    private readonly OrientLoop loop;
    private readonly CRpcServer crpcServer;
    private readonly HelloworldServiceImpl greeter;
    private readonly int port;
    private readonly int maxFrameLength;
    private IChannel? channel;
    private IEventLoopGroup? bossGroup;
    private IEventLoopGroup? workerGroup;

    public UnifiedServer(
        OrientLoop loop,
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

    public OrientTask StartAsync(CancellationToken cancellationToken = default)
    {
        return StartInternalAsync(cancellationToken);
    }

    private async OrientTask StartInternalAsync(CancellationToken cancellationToken)
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
                    new CRpcServerPipelineFactory(crpcServer.Options).Configure(
                        ctx.Channel.Pipeline,
                        new CRpcServerHandler(crpcServer));
                },
                ctx =>
                {
                    ctx.Channel.Pipeline.AddLast(new HttpServerCodec());
                    ctx.Channel.Pipeline.AddLast(new HttpObjectAggregator(65536));
                    ctx.Channel.Pipeline.AddLast(new GreeterHttpHandler(loop, crpcServer.Connections, greeter));
                }));
        }));

        channel = await OrientTask.FromTask(bootstrap.BindAsync(IPAddress.Loopback, port), loop);
    }

    public OrientTask StopAsync()
    {
        return StopInternalAsync();
    }

    private async OrientTask StopInternalAsync()
    {
        if (channel is not null)
        {
            await OrientTask.FromTask(channel.CloseAsync(), loop);
            channel = null;
        }

        if (workerGroup is not null)
        {
            await OrientTask.FromTask(
                workerGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            workerGroup = null;
        }

        if (bossGroup is not null)
        {
            await OrientTask.FromTask(
                bossGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
            bossGroup = null;
        }
    }
}
