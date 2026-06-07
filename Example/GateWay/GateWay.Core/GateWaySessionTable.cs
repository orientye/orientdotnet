using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public sealed class GateWaySessionTable
{
    private readonly Dictionary<long, GateWayBackendLink> links = new();
    private readonly IBackendClientFactory backendClientFactory;
    private readonly IBackendConnector backendConnector;
    private readonly GateWayPushRelay pushRelay;

    public GateWaySessionTable(
        IBackendClientFactory backendClientFactory,
        IBackendConnector backendConnector,
        GateWayPushRelay pushRelay)
    {
        this.backendClientFactory = backendClientFactory ?? throw new ArgumentNullException(nameof(backendClientFactory));
        this.backendConnector = backendConnector ?? throw new ArgumentNullException(nameof(backendConnector));
        this.pushRelay = pushRelay ?? throw new ArgumentNullException(nameof(pushRelay));
    }

    public GateWayBackendLink? TryGet(long connectionId)
    {
        return links.GetValueOrDefault(connectionId);
    }

    public async CRpcTask<GateWayBackendLink?> GetOrCreateAsync(
        CRpcConnection inbound,
        GateWayOptions options,
        CRpcLoop loop)
    {
        if (links.TryGetValue(inbound.ConnectionId, out var existing))
        {
            return existing;
        }

        var client = backendClientFactory.Create(loop);
        try
        {
            await backendConnector.ConnectAsync(client, options);
        }
        catch
        {
            return null;
        }

        var link = new GateWayBackendLink(inbound, client, options, backendConnector);
        pushRelay.Attach(link);
        links[inbound.ConnectionId] = link;
        return link;
    }

    public async CRpcTask RemoveAsync(long connectionId)
    {
        if (links.Remove(connectionId, out var link))
        {
            await link.DisposeAsync();
        }
    }

    public async CRpcTask DisposeAllAsync()
    {
        foreach (var link in links.Values)
        {
            await link.DisposeAsync();
        }

        links.Clear();
    }
}
