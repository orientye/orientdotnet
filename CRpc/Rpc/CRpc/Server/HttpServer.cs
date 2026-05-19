using System.Net;

using DotNetty.Codecs.Http;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;

using CRpc.Async;

namespace CRpc.Rpc.CRpc.Server;

public sealed class HttpServer
{
    private readonly CRpcLoop loop;
    private readonly HttpServerOptions options;
    private IChannel? bootstrapChannel;
    private IEventLoopGroup? group;
    private IEventLoopGroup? workGroup;

    public HttpServer(CRpcLoop loop, HttpServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
        this.options = options ?? new HttpServerOptions();
    }

    public CRpcLoop Loop => loop;

    public bool IsRunning => bootstrapChannel is not null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (bootstrapChannel is not null)
        {
            throw new InvalidOperationException("HttpServer is already started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        group = new MultithreadEventLoopGroup(1);
        workGroup = new MultithreadEventLoopGroup(1);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(group, workGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap.Option(ChannelOption.SoBacklog, 8192);
        bootstrap.ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
        {
            var pipeline = channel.Pipeline;
            pipeline.AddLast(new HttpServerCodec());
            pipeline.AddLast(new HttpObjectAggregator(options.MaxContentLength));
            pipeline.AddLast(new HttpServerHandler(loop));
        }));

        bootstrapChannel = await bootstrap.BindAsync(options.Address, options.Port).ConfigureAwait(false);
    }

    public async Task StopAsync()
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
    }
}
