using CRpc.Async;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests.GateWayTests;

public class GateWaySessionTableTests : CrpcTestBase
{
    [Fact]
    public void GetOrCreateLinkReturnsSameClientForSameConnection()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var factory = new CountingBackendClientFactory();
        var table = new global::GateWay.GateWaySessionTable(
            factory,
            new SuccessBackendConnector(),
            new global::GateWay.GateWayPushRelay());
        var inbound = RegisterInboundConnection(loop);

        var link1 = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, new global::GateWay.GateWayOptions(), loop));
        var link2 = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, new global::GateWay.GateWayOptions(), loop));

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
        var table = new global::GateWay.GateWaySessionTable(
            factory,
            new SuccessBackendConnector(),
            new global::GateWay.GateWayPushRelay());
        var inbound = RegisterInboundConnection(loop);

        CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, new global::GateWay.GateWayOptions(), loop));

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await table.RemoveAsync(inbound.ConnectionId);
            return 0;
        });

        CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, new global::GateWay.GateWayOptions(), loop));

        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void ConnectFailureReturnsNull()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var table = new global::GateWay.GateWaySessionTable(
            new CountingBackendClientFactory(),
            new FailingBackendConnector(),
            new global::GateWay.GateWayPushRelay());
        var inbound = RegisterInboundConnection(loop);

        var link = CRpcLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, new global::GateWay.GateWayOptions(), loop));

        Assert.Null(link);
    }

    private static CRpcConnection RegisterInboundConnection(CRpcLoop loop)
    {
        var server = new CRpcServer(loop);
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
        public CRpcTask ConnectAsync(CRpc.Rpc.CRpc.Client.CRpcClient client, global::GateWay.GateWayOptions options)
        {
            return CRpcTask.CompletedTask(CRpcLoop.Current);
        }
    }

    private sealed class FailingBackendConnector : global::GateWay.IBackendConnector
    {
        public CRpcTask ConnectAsync(CRpc.Rpc.CRpc.Client.CRpcClient client, global::GateWay.GateWayOptions options)
        {
            throw new InvalidOperationException("connect failed");
        }
    }
}
