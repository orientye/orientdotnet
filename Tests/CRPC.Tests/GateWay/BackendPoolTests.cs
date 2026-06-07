namespace CRPC.Tests.GateWayTests;

public class BackendPoolTests : CrpcTestBase
{
    [Fact]
    public void RoundRobinAlternatesHealthyEndpoints()
    {
        var pool = new global::GateWay.BackendPool(1000, new global::GateWay.RoundRobinPicker());
        var endpointA = new global::GateWay.BackendEndpoint("127.0.0.1", 7999);
        var endpointB = new global::GateWay.BackendEndpoint("127.0.0.1", 8001);
        pool.AddEndpoint(endpointA);
        pool.AddEndpoint(endpointB);

        var first = pool.Pick();
        var second = pool.Pick();
        var third = pool.Pick();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Same(first, third);
    }

    [Fact]
    public void PickSkipsUnhealthyEndpoints()
    {
        var pool = new global::GateWay.BackendPool(1000, new global::GateWay.RoundRobinPicker());
        var endpointA = new global::GateWay.BackendEndpoint("127.0.0.1", 7999);
        var endpointB = new global::GateWay.BackendEndpoint("127.0.0.1", 8001);
        pool.AddEndpoint(endpointA);
        pool.AddEndpoint(endpointB);
        pool.MarkUnhealthy(endpointA);

        var picked = pool.Pick();

        Assert.Same(endpointB, picked);
    }

    [Fact]
    public void PickReturnsNullWhenAllUnhealthy()
    {
        var pool = new global::GateWay.BackendPool(1000, new global::GateWay.RoundRobinPicker());
        var endpoint = new global::GateWay.BackendEndpoint("127.0.0.1", 7999);
        pool.AddEndpoint(endpoint);
        pool.MarkUnhealthy(endpoint);

        Assert.Null(pool.Pick());
    }
}
