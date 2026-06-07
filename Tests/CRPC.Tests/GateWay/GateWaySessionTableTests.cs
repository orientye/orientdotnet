using CRpc.Async;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests.GateWayTests;

public class GateWaySessionTableTests : CrpcTestBase
{
    private const ushort GreeterServiceId = 1000;

    [Fact]
    public void GetOrCreateLinkReturnsSameClientForSameConnection()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var factory = new CountingBackendClientFactory();
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector());
        var inbound = RegisterInboundConnection(loop);

        var link1 = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));
        var link2 = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        Assert.NotNull(link1);
        Assert.Same(link1, link2);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void RemoveLinkDropsEntry()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var factory = new CountingBackendClientFactory();
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector());
        var inbound = RegisterInboundConnection(loop);

        CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await table.RemoveAsync(inbound.ConnectionId);
            return 0;
        });

        CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void ConnectFailureReturnsNullAndMarksEndpointUnhealthy()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var registry = GateWayTestHelpers.CreateRegistry(GreeterServiceId, ("127.0.0.1", 7999));
        var table = GateWayTestHelpers.CreateSessionTable(
            new CountingBackendClientFactory(),
            new FailingBackendConnector(),
            registry);
        var inbound = RegisterInboundConnection(loop);

        var link = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        Assert.Null(link);
        Assert.True(registry.TryGetPool(GreeterServiceId, out var pool));
        Assert.False(pool!.Endpoints[0].IsHealthy);
    }

    [Fact]
    public void NewConnectionsRoundRobinAcrossEndpoints()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var registry = GateWayTestHelpers.CreateRegistry(
            GreeterServiceId,
            ("127.0.0.1", 7999),
            ("127.0.0.1", 8001));
        var table = GateWayTestHelpers.CreateSessionTable(
            new CountingBackendClientFactory(),
            new SuccessBackendConnector(),
            registry);
        var server = new CRpcServer(loop);
        var inboundA = RegisterInboundConnection(loop, server);
        var inboundB = RegisterInboundConnection(loop, server);

        var linkA = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inboundA, GreeterServiceId, loop));
        var linkB = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inboundB, GreeterServiceId, loop));

        Assert.NotNull(linkA);
        Assert.NotNull(linkB);
        Assert.NotEqual(linkA!.Endpoint.Port, linkB!.Endpoint.Port);
    }

    private static CRpcConnection RegisterInboundConnection(CRpcLoop loop, CRpcServer? server = null)
    {
        server ??= new CRpcServer(loop);
        var channel = new EmbeddedChannel();
        loop.Post(() => server.Connections.Register(channel));
        loop.Tick();

        Assert.True(server.Connections.TryGetByChannel(channel, out var connection));
        return connection;
    }

    private sealed class CountingBackendClientFactory : global::GateWay.IBackendClientFactory
    {
        public int CreateCount { get; private set; }

        public CRpc.Rpc.CRpc.Client.CRpcClient Create(CRpcLoop loop)
        {
            CreateCount++;
            return new CRpc.Rpc.CRpc.Client.CRpcClient(loop);
        }
    }

    private sealed class SuccessBackendConnector : global::GateWay.IBackendConnector
    {
        public CRpcTask ConnectAsync(CRpc.Rpc.CRpc.Client.CRpcClient client, global::GateWay.BackendEndpoint endpoint)
        {
            return CRpcTask.CompletedTask(CRpcLoop.Current);
        }
    }

    private sealed class FailingBackendConnector : global::GateWay.IBackendConnector
    {
        public CRpcTask ConnectAsync(CRpc.Rpc.CRpc.Client.CRpcClient client, global::GateWay.BackendEndpoint endpoint)
        {
            throw new InvalidOperationException("connect failed");
        }
    }
}
