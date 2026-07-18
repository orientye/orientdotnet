using Orient.Runtime;
using Orient.Rpc.Server;

namespace GateWay;

public sealed class GateWaySessionTable
{
    private readonly Dictionary<long, GateWayBackendLink> links = new();
    private readonly IBackendClientFactory backendClientFactory;
    private readonly IBackendConnector backendConnector;
    private readonly GateWayPushRelay pushRelay;
    private readonly BackendPoolRegistry poolRegistry;

    public GateWaySessionTable(
        BackendPoolRegistry poolRegistry,
        IBackendClientFactory backendClientFactory,
        IBackendConnector backendConnector,
        GateWayPushRelay pushRelay)
    {
        this.poolRegistry = poolRegistry ?? throw new ArgumentNullException(nameof(poolRegistry));
        this.backendClientFactory = backendClientFactory ?? throw new ArgumentNullException(nameof(backendClientFactory));
        this.backendConnector = backendConnector ?? throw new ArgumentNullException(nameof(backendConnector));
        this.pushRelay = pushRelay ?? throw new ArgumentNullException(nameof(pushRelay));
    }

    public GateWayBackendLink? TryGet(long connectionId)
    {
        return links.GetValueOrDefault(connectionId);
    }

    public async OrientTask<GateWayBackendLink?> GetOrCreateAsync(
        CRpcConnection inbound,
        ushort serviceId,
        OrientExecutor loop)
    {
        if (links.TryGetValue(inbound.ConnectionId, out var existing))
        {
            return existing;
        }

        if (!poolRegistry.TryGetPool(serviceId, out var pool))
        {
            return null;
        }

        var endpoint = pool.Pick();
        if (endpoint is null)
        {
            return null;
        }

        var client = backendClientFactory.Create(loop);
        try
        {
            await backendConnector.ConnectAsync(client, endpoint);
        }
        catch
        {
            pool.MarkUnhealthy(endpoint);
            return null;
        }

        var link = new GateWayBackendLink(inbound, client, serviceId, endpoint, backendConnector);
        var inboundConnectionId = inbound.ConnectionId;
        client.ConnectionLost += () =>
        {
            loop.Post(() => HandleBackendConnectionLost(inboundConnectionId, serviceId, endpoint));
        };
        pushRelay.Attach(link);
        links[inbound.ConnectionId] = link;
        return link;
    }

    private void HandleBackendConnectionLost(long inboundConnectionId, ushort serviceId, BackendEndpoint endpoint)
    {
        links.Remove(inboundConnectionId);
        if (poolRegistry.TryGetPool(serviceId, out var pool))
        {
            pool.MarkUnhealthy(endpoint);
        }
    }

    public async OrientTask RemoveAsync(long connectionId)
    {
        if (links.Remove(connectionId, out var link))
        {
            await link.DisposeAsync();
        }
    }

    public async OrientTask DisposeAllAsync()
    {
        foreach (var link in links.Values)
        {
            await link.DisposeAsync();
        }

        links.Clear();
    }
}
