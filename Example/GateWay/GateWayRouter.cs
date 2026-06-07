using CRpc.Rpc;

namespace GateWay;

public sealed class GateWayRouter
{
    private readonly Dictionary<ushort, IRpcClient> routes = new();

    public void Register(ushort serviceId, IRpcClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        routes[serviceId] = client;
    }

    public IRpcClient? GetBackend(ushort serviceId)
    {
        return routes.GetValueOrDefault(serviceId);
    }
}
