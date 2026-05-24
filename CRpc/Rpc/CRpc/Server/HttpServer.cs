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
    private int isRunning;

    public HttpServer(CRpcLoop loop, HttpServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        this.loop = loop;
        this.options = options ?? new HttpServerOptions();
    }

    public CRpcLoop Loop => loop;

    public bool IsRunning => Volatile.Read(ref isRunning) == 1;

    public CRpcTask StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureOwnerLoopThread();
        return StartInternalAsync(cancellationToken);
    }

    public CRpcTask StopAsync()
    {
        EnsureOwnerLoopThread();
        return StopInternalAsync();
    }

    private async CRpcTask StartInternalAsync(CancellationToken cancellationToken)
    {
        if (bootstrapChannel is not null)
        {
            throw new InvalidOperationException("HttpServer is already started.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        group = new MultithreadEventLoopGroup(options.BossThreadCount);
        workGroup = new MultithreadEventLoopGroup(options.WorkerThreadCount);

        var bootstrap = new ServerBootstrap();
        bootstrap.Group(group, workGroup);
        bootstrap.Channel<TcpServerSocketChannel>();
        bootstrap.Option(ChannelOption.SoBacklog, options.SoBacklog);
        bootstrap.ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
        {
            var pipeline = channel.Pipeline;
            pipeline.AddLast(new HttpServerCodec());
            pipeline.AddLast(new HttpObjectAggregator(options.MaxContentLength));
            pipeline.AddLast(new HttpServerHandler(loop));
        }));

        try
        {
            bootstrapChannel = await CRpcTask.FromTask(
                bootstrap.BindAsync(options.Address, options.Port),
                loop);
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

        bootstrapChannel = null;
        workGroup = null;
        group = null;
        Volatile.Write(ref isRunning, 0);

        if (currentBootstrapChannel is not null)
        {
            await CRpcTask.FromTask(currentBootstrapChannel.CloseAsync(), loop);
        }

        if (currentWorkGroup is not null)
        {
            await CRpcTask.FromTask(
                currentWorkGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
        }

        if (currentGroup is not null)
        {
            await CRpcTask.FromTask(
                currentGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                loop);
        }
    }

    private void EnsureOwnerLoopThread()
    {
        var currentLoop = CRpcLoop.Current
            ?? throw new InvalidOperationException("HttpServer lifecycle operations must be called from a bound CRpcLoop thread.");
        if (!ReferenceEquals(loop, currentLoop))
        {
            throw new InvalidOperationException("HttpServer lifecycle operations must be called on the server's owner CRpcLoop thread.");
        }
    }
}
