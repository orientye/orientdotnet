using CRpc.Async;
using CRpc.Transport;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using LordUnion.IntegrationTests.Config;

namespace LordUnion.IntegrationTests.Sessions;

/// <summary>
/// One DotNetty <see cref="IEventLoopGroup"/> shared by all live account transports in a scenario run.
/// Shutdown exactly once after all channels are closed.
/// </summary>
internal sealed class LordUnionSharedIo : IAsyncDisposable
{
    private readonly MultithreadEventLoopGroup group;
    private bool disposed;

    private LordUnionSharedIo(MultithreadEventLoopGroup group)
    {
        this.group = group;
    }

    public IEventLoopGroup EventLoopGroup => group;

    public static LordUnionSharedIo FromConfig(LordUnionTestConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var ioThreadCount = config.Live.IoThreadCount;
        if (ioThreadCount <= 0)
        {
            throw new InvalidOperationException(
                $"LordUnion Live.IoThreadCount must be positive; got {ioThreadCount}.");
        }

        return new LordUnionSharedIo(new MultithreadEventLoopGroup(ioThreadCount));
    }

    public ValueTask DisposeAsync(CRpcLoop ownerLoop)
    {
        ArgumentNullException.ThrowIfNull(ownerLoop);
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        if (!ownerLoop.IsInLoopThread)
        {
            throw new InvalidOperationException(
                "LordUnionSharedIo.DisposeAsync must run on the scenario CRpcLoop thread.");
        }

        disposed = true;
        var shutdownAwaiter = CRpcTask.FromTask(group.ShutdownGracefullyAsync(), ownerLoop).GetAwaiter();
        while (!shutdownAwaiter.IsCompleted)
        {
            ownerLoop.Tick();
            if (!shutdownAwaiter.IsCompleted)
            {
                ownerLoop.WaitForWorkOrTimer(CancellationToken.None);
            }
        }

        shutdownAwaiter.GetResult();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() =>
        throw new InvalidOperationException(
            "Use DisposeAsync(CRpcLoop) so shutdown is pumped on the owner loop thread.");
}
