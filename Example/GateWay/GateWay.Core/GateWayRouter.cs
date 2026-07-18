using Orient.Runtime;
using Orient.Rpc.Server;

namespace GateWay;

public sealed class GateWayRouter
{
    private readonly OrientExecutor executor;
    private readonly GateWayConfig config;
    private readonly GateWaySessionTable sessionTable;
    private readonly BackendPoolRegistry poolRegistry;

    public GateWayRouter(
        OrientExecutor executor,
        GateWayConfig config,
        BackendPoolRegistry poolRegistry,
        GateWaySessionTable sessionTable)
    {
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
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

        return await sessionTable.GetOrCreateAsync(inbound, serviceId, executor);
    }
}
