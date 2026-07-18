using Orient.Runtime;
using DotNetty.Transport.Channels;

namespace Orient.Rpc.Server;

public sealed class CRpcConnectionRegistry
{
    private readonly OrientExecutor ownerExecutor;
    private readonly Dictionary<IChannel, CRpcConnection> byChannel = new();
    private readonly Dictionary<long, CRpcConnection> byId = new();
    private long nextConnectionId;

    internal CRpcConnectionRegistry(OrientExecutor ownerExecutor)
    {
        this.ownerExecutor = ownerExecutor ?? throw new ArgumentNullException(nameof(ownerExecutor));
    }

    public bool TryGet(long connectionId, out CRpcConnection connection)
    {
        EnsureOwnerExecutorThread();
        return byId.TryGetValue(connectionId, out connection!);
    }

    public IReadOnlyList<CRpcConnection> Snapshot()
    {
        EnsureOwnerExecutorThread();
        return byId.Values.ToArray();
    }

    public CRpcConnection Register(IChannel channel)
    {
        EnsureOwnerExecutorThread();
        if (byChannel.TryGetValue(channel, out var existing))
        {
            return existing;
        }

        var connection = new CRpcConnection(ownerExecutor, ++nextConnectionId, channel);
        byChannel[channel] = connection;
        byId[connection.ConnectionId] = connection;
        return connection;
    }

    public void Unregister(IChannel channel)
    {
        EnsureOwnerExecutorThread();
        if (!byChannel.Remove(channel, out var connection))
        {
            return;
        }

        connection.MarkInactive();
        byId.Remove(connection.ConnectionId);
    }

    public bool TryGetByChannel(IChannel channel, out CRpcConnection connection)
    {
        EnsureOwnerExecutorThread();
        return byChannel.TryGetValue(channel, out connection!);
    }

    private void EnsureOwnerExecutorThread()
    {
        var executor = OrientExecutor.Current
            ?? throw new InvalidOperationException("CRpcConnectionRegistry operations must be called from a bound OrientExecutor thread.");
        if (!ReferenceEquals(ownerExecutor, executor))
        {
            throw new InvalidOperationException("CRpcConnectionRegistry operations must be called on the server owner OrientExecutor thread.");
        }
    }
}
