using Orient.Runtime;
using Orient.Rpc.Server;

namespace GateWay;

public sealed class GateWayRouter
{
    private readonly OrientLoop loop;
    private readonly GateWayConfig config;
    private readonly GateWaySessionTable sessionTable;
    private readonly BackendPoolRegistry poolRegistry;

    public GateWayRouter(
        OrientLoop loop,
        GateWayConfig config,
        BackendPoolRegistry poolRegistry,
        GateWaySessionTable sessionTable)
    {
        this.loop = loop ?? throw new ArgumentNullException(nameof(loop));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.poolRegistry = poolRegistry ?? throw new ArgumentNullException(nameof(poolRegistry));
        this.sessionTable = sessionTable ?? throw new ArgumentNullException(nameof(sessionTable));
    }

    public GateWaySessionTable SessionTable => sessionTable;

    public BackendPoolRegistry PoolRegistry => poolRegistry;

    public GateWayConfig Config => config;

    public async OrientTask<GateWayBackendLink?> GetOrCreateLinkAsync(CRpcConnection inbound, ushort serviceId)
    {
        if (!poolRegistry.TryGetPool(serviceId, out _))
        {
            return null;
        }

        return await sessionTable.GetOrCreateAsync(inbound, serviceId, loop);
    }
}
