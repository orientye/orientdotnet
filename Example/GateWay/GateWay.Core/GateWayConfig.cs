namespace GateWay;

public sealed class GateWayConfig
{
    public int ListenPort { get; init; } = 7000;

    public int DefaultTimeoutMs { get; init; } = 5000;

    public ushort FallbackServiceId { get; init; } = 0;

    public IReadOnlyList<GateWayPoolConfig> Pools { get; init; } = Array.Empty<GateWayPoolConfig>();

    public static GateWayConfig CreateDefaultDemo()
    {
        return new GateWayConfig
        {
            Pools =
            [
                new GateWayPoolConfig
                {
                    ServiceId = 1000,
                    PickPolicy = "roundRobin",
                    Endpoints =
                    [
                        new GateWayEndpointConfig { Host = "127.0.0.1", Port = 7999 },
                        new GateWayEndpointConfig { Host = "127.0.0.1", Port = 8001 },
                    ],
                },
            ],
        };
    }

    public BackendPoolRegistry BuildRegistry(IBackendPicker? picker = null)
    {
        var registry = new BackendPoolRegistry();
        var poolPicker = picker ?? new RoundRobinPicker();

        foreach (var poolConfig in Pools)
        {
            var pool = new BackendPool(poolConfig.ServiceId, poolPicker);
            foreach (var endpointConfig in poolConfig.Endpoints)
            {
                pool.AddEndpoint(new BackendEndpoint(endpointConfig.Host, endpointConfig.Port, endpointConfig.Weight));
            }

            registry.Register(pool);
        }

        return registry;
    }
}

public sealed class GateWayPoolConfig
{
    public ushort ServiceId { get; init; }

    public string PickPolicy { get; init; } = "roundRobin";

    public IReadOnlyList<GateWayEndpointConfig> Endpoints { get; init; } = Array.Empty<GateWayEndpointConfig>();
}

public sealed class GateWayEndpointConfig
{
    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; }

    public int Weight { get; init; } = 1;
}
