namespace CRPC.Tests.GateWayTests;

internal static class GateWayTestHelpers
{
    public static global::GateWay.BackendPoolRegistry CreateRegistry(
        ushort serviceId = 1000,
        params (string host, int port)[] endpoints)
    {
        var config = new global::GateWay.GateWayConfig
        {
            Pools =
            [
                new global::GateWay.GateWayPoolConfig
                {
                    ServiceId = serviceId,
                    Endpoints = endpoints.Select(endpoint => new global::GateWay.GateWayEndpointConfig
                    {
                        Host = endpoint.host,
                        Port = endpoint.port,
                    }).ToArray(),
                },
            ],
        };

        return config.BuildRegistry();
    }

    public static global::GateWay.GateWaySessionTable CreateSessionTable(
        global::GateWay.IBackendClientFactory factory,
        global::GateWay.IBackendConnector connector,
        global::GateWay.BackendPoolRegistry? registry = null)
    {
        registry ??= CreateRegistry(1000, ("127.0.0.1", 7999));
        return new global::GateWay.GateWaySessionTable(
            registry,
            factory,
            connector,
            new global::GateWay.GateWayPushRelay());
    }
}
