using Orient.Runtime;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

public sealed class CRpcConnectionRegistry
{
    private readonly OrientLoop ownerLoop;
    private readonly Dictionary<IChannel, CRpcConnection> byChannel = new();
    private readonly Dictionary<long, CRpcConnection> byId = new();
    private long nextConnectionId;

    internal CRpcConnectionRegistry(OrientLoop ownerLoop)
    {
        this.ownerLoop = ownerLoop ?? throw new ArgumentNullException(nameof(ownerLoop));
    }

    public bool TryGet(long connectionId, out CRpcConnection connection)
    {
        EnsureOwnerLoopThread();
        return byId.TryGetValue(connectionId, out connection!);
    }

    public IReadOnlyList<CRpcConnection> Snapshot()
    {
        EnsureOwnerLoopThread();
        return byId.Values.ToArray();
    }

    public CRpcConnection Register(IChannel channel)
    {
        EnsureOwnerLoopThread();
        if (byChannel.TryGetValue(channel, out var existing))
        {
            return existing;
        }

        var connection = new CRpcConnection(ownerLoop, ++nextConnectionId, channel);
        byChannel[channel] = connection;
        byId[connection.ConnectionId] = connection;
        return connection;
    }

    public void Unregister(IChannel channel)
    {
        EnsureOwnerLoopThread();
        if (!byChannel.Remove(channel, out var connection))
        {
            return;
        }

        connection.MarkInactive();
        byId.Remove(connection.ConnectionId);
    }

    public bool TryGetByChannel(IChannel channel, out CRpcConnection connection)
    {
        EnsureOwnerLoopThread();
        return byChannel.TryGetValue(channel, out connection!);
    }

    private void EnsureOwnerLoopThread()
    {
        var loop = OrientLoop.Current
            ?? throw new InvalidOperationException("CRpcConnectionRegistry operations must be called from a bound OrientLoop thread.");
        if (!ReferenceEquals(ownerLoop, loop))
        {
            throw new InvalidOperationException("CRpcConnectionRegistry operations must be called on the server owner OrientLoop thread.");
        }
    }
}
