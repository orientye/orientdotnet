using CRpc.Async;
using CRpc.Rpc.CRpc.Server;

namespace GateWay;

public sealed class GateWayRouter
{
    private readonly CRpcLoop loop;
    private readonly GateWayOptions options;
    private readonly GateWaySessionTable sessionTable;

    public GateWayRouter(CRpcLoop loop, GateWayOptions options, GateWaySessionTable sessionTable)
    {
        this.loop = loop ?? throw new ArgumentNullException(nameof(loop));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.sessionTable = sessionTable ?? throw new ArgumentNullException(nameof(sessionTable));
    }

    public GateWaySessionTable SessionTable => sessionTable;

    public async CRpcTask<GateWayBackendLink?> GetOrCreateLinkAsync(CRpcConnection inbound, ushort serviceId)
    {
        if (!options.RoutedServiceIds.Contains(serviceId))
        {
            return null;
        }

        return await sessionTable.GetOrCreateAsync(inbound, options, loop);
    }
}
