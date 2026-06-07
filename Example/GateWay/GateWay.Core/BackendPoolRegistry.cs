namespace GateWay;

public sealed class BackendPoolRegistry
{
    private readonly Dictionary<ushort, BackendPool> pools = new();

    public void Register(BackendPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        pools[pool.ServiceId] = pool;
    }

    public bool TryGetPool(ushort serviceId, out BackendPool pool)
    {
        return pools.TryGetValue(serviceId, out pool!);
    }

    public IReadOnlyCollection<ushort> ServiceIds => pools.Keys;
}
