using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Server;
using Orient.Rpc.Transport;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests.GateWayTests;

public class GateWaySessionTableTests : CrpcTestBase
{
    private const ushort GreeterServiceId = 1000;

    [Fact]
    public void GetOrCreateLinkReturnsSameClientForSameConnection()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var factory = new CountingBackendClientFactory();
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector());
        var inbound = RegisterInboundConnection(loop);

        var link1 = OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));
        var link2 = OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        Assert.NotNull(link1);
        Assert.Same(link1, link2);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void RemoveLinkDropsEntry()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var factory = new CountingBackendClientFactory();
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector());
        var inbound = RegisterInboundConnection(loop);

        OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        OrientLoopRunner.RunUntilComplete(loop, async () =>
        {
            await table.RemoveAsync(inbound.ConnectionId);
            return 0;
        });

        OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void ConnectFailureReturnsNullAndMarksEndpointUnhealthy()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var registry = GateWayTestHelpers.CreateRegistry(GreeterServiceId, ("127.0.0.1", 7999));
        var table = GateWayTestHelpers.CreateSessionTable(
            new CountingBackendClientFactory(),
            new FailingBackendConnector(),
            registry);
        var inbound = RegisterInboundConnection(loop);

        var link = OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));

        Assert.Null(link);
        Assert.True(registry.TryGetPool(GreeterServiceId, out var pool));
        Assert.False(pool!.Endpoints[0].IsHealthy);
    }

    [Fact]
    public void NewConnectionsRoundRobinAcrossEndpoints()
    {
        var loop = new OrientLoop();
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

        var linkA = OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inboundA, GreeterServiceId, loop));
        var linkB = OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inboundB, GreeterServiceId, loop));

        Assert.NotNull(linkA);
        Assert.NotNull(linkB);
        Assert.NotEqual(linkA!.Endpoint.Port, linkB!.Endpoint.Port);
    }

    [Fact]
    public void BackendConnectionLostRemovesLinkAndMarksEndpointUnhealthy()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var registry = GateWayTestHelpers.CreateRegistry(GreeterServiceId, ("127.0.0.1", 7999));
        var factory = new CapturingBackendClientFactory(loop);
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector(), registry);
        var inbound = RegisterInboundConnection(loop);

        var link = OrientLoopRunner.RunUntilComplete(
            loop,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, loop));
        Assert.NotNull(link);

        var backendChannel = new EmbeddedChannel();
        SetBackendClientChannel(factory.LastClient!, backendChannel);
        GetClientHost(factory.LastClient!).PostChannelInactive(backendChannel);
        loop.Tick();

        Assert.Null(table.TryGet(inbound.ConnectionId));
        Assert.True(registry.TryGetPool(GreeterServiceId, out var pool));
        Assert.False(pool!.Endpoints[0].IsHealthy);
    }

    private static CRpcConnection RegisterInboundConnection(OrientLoop loop, CRpcServer? server = null)
    {
        server ??= new CRpcServer(loop);
        var channel = new EmbeddedChannel();
        loop.Post(() => server.Connections.Register(channel));
        loop.Tick();

        Assert.True(server.Connections.TryGetByChannel(channel, out var connection));
        return connection;
    }

    private static TcpChannelHost GetClientHost(CRpcClient client)
    {
        var hostField = typeof(CRpcClient).GetField(
            "host",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(hostField);
        return Assert.IsType<TcpChannelHost>(hostField!.GetValue(client));
    }

    private static void SetBackendClientChannel(CRpcClient client, EmbeddedChannel channel)
    {
        var host = GetClientHost(client);
        var channelField = typeof(TcpChannelHost).GetField(
            "channel",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(channelField);
        channelField!.SetValue(host, channel);
    }

    private sealed class CountingBackendClientFactory : global::GateWay.IBackendClientFactory
    {
        public int CreateCount { get; private set; }

        public Orient.Rpc.Client.CRpcClient Create(OrientLoop loop)
        {
            CreateCount++;
            return new Orient.Rpc.Client.CRpcClient(loop);
        }
    }

    private sealed class CapturingBackendClientFactory : global::GateWay.IBackendClientFactory
    {
        private readonly OrientLoop loop;

        public CapturingBackendClientFactory(OrientLoop loop)
        {
            this.loop = loop;
        }

        public Orient.Rpc.Client.CRpcClient? LastClient { get; private set; }

        public Orient.Rpc.Client.CRpcClient Create(OrientLoop loop)
        {
            LastClient = new Orient.Rpc.Client.CRpcClient(this.loop);
            return LastClient;
        }
    }

    private sealed class SuccessBackendConnector : global::GateWay.IBackendConnector
    {
        public OrientTask ConnectAsync(Orient.Rpc.Client.CRpcClient client, global::GateWay.BackendEndpoint endpoint)
        {
            return OrientTask.CompletedTask(OrientLoop.Current);
        }
    }

    private sealed class FailingBackendConnector : global::GateWay.IBackendConnector
    {
        public OrientTask ConnectAsync(Orient.Rpc.Client.CRpcClient client, global::GateWay.BackendEndpoint endpoint)
        {
            throw new InvalidOperationException("connect failed");
        }
    }
}
