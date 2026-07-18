using System.Net;
using Orient.Runtime;
using Orient.Rpc.Server;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

namespace Example.Http;

public sealed class HttpListenServer
{
    private readonly OrientExecutor executor;
    private readonly CRpcServer crpcServer;
    private readonly HelloworldServiceImpl greeter;
    private readonly int port;
    private IChannel? channel;
    private IEventLoopGroup? bossGroup;
    private IEventLoopGroup? workerGroup;

    public HttpListenServer(OrientExecutor executor, CRpcServer crpcServer, HelloworldServiceImpl greeter, int port)
    {
        this.executor = executor;
        this.crpcServer = crpcServer;
        this.greeter = greeter;
        this.port = port;
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
            ch.Pipeline.AddLast(new HttpServerCodec());
            ch.Pipeline.AddLast(new HttpObjectAggregator(65536));
            ch.Pipeline.AddLast(new GreeterHttpHandler(executor, crpcServer.Connections, greeter));
        }));

        channel = await OrientTask.FromTask(bootstrap.BindAsync(IPAddress.Loopback, port), executor);
    }

    public OrientTask StopAsync()
    {
        return StopInternalAsync();
    }

    private async OrientTask StopInternalAsync()
    {
        if (channel is not null)
        {
            await OrientTask.FromTask(channel.CloseAsync(), executor);
            channel = null;
        }

        if (workerGroup is not null)
        {
            await OrientTask.FromTask(
                workerGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                executor);
            workerGroup = null;
        }

        if (bossGroup is not null)
        {
            await OrientTask.FromTask(
                bossGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                executor);
            bossGroup = null;
        }
    }
}
