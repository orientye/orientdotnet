using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Server;
using Orient.Rpc.Transport;
using DotNetty.Transport.Channels.Embedded;

namespace Orient.Tests.GateWay;

public class GateWaySessionTableTests : OrientTestBase
{
    private const ushort GreeterServiceId = 1000;

    [Fact]
    public void GetOrCreateLinkReturnsSameClientForSameConnection()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var factory = new CountingBackendClientFactory();
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector());
        var inbound = RegisterInboundConnection(executor);

        var link1 = OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, executor));
        var link2 = OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, executor));

        Assert.NotNull(link1);
        Assert.Same(link1, link2);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void RemoveLinkDropsEntry()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var factory = new CountingBackendClientFactory();
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector());
        var inbound = RegisterInboundConnection(executor);

        OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, executor));

        OrientExecutorRunner.RunUntilComplete(executor, async () =>
        {
            await table.RemoveAsync(inbound.ConnectionId);
            return 0;
        });

        OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, executor));

        Assert.Equal(2, factory.CreateCount);
    }

    [Fact]
    public void ConnectFailureReturnsNullAndMarksEndpointUnhealthy()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var registry = GateWayTestHelpers.CreateRegistry(GreeterServiceId, ("127.0.0.1", 7999));
        var table = GateWayTestHelpers.CreateSessionTable(
            new CountingBackendClientFactory(),
            new FailingBackendConnector(),
            registry);
        var inbound = RegisterInboundConnection(executor);

        var link = OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, executor));

        Assert.Null(link);
        Assert.True(registry.TryGetPool(GreeterServiceId, out var pool));
        Assert.False(pool!.Endpoints[0].IsHealthy);
    }

    [Fact]
    public void NewConnectionsRoundRobinAcrossEndpoints()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var registry = GateWayTestHelpers.CreateRegistry(
            GreeterServiceId,
            ("127.0.0.1", 7999),
            ("127.0.0.1", 8001));
        var table = GateWayTestHelpers.CreateSessionTable(
            new CountingBackendClientFactory(),
            new SuccessBackendConnector(),
            registry);
        var server = new CRpcServer(executor);
        var inboundA = RegisterInboundConnection(executor, server);
        var inboundB = RegisterInboundConnection(executor, server);

        var linkA = OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inboundA, GreeterServiceId, executor));
        var linkB = OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inboundB, GreeterServiceId, executor));

        Assert.NotNull(linkA);
        Assert.NotNull(linkB);
        Assert.NotEqual(linkA!.Endpoint.Port, linkB!.Endpoint.Port);
    }

    [Fact]
    public void BackendConnectionLostRemovesLinkAndMarksEndpointUnhealthy()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        var registry = GateWayTestHelpers.CreateRegistry(GreeterServiceId, ("127.0.0.1", 7999));
        var factory = new CapturingBackendClientFactory(executor);
        var table = GateWayTestHelpers.CreateSessionTable(factory, new SuccessBackendConnector(), registry);
        var inbound = RegisterInboundConnection(executor);

        var link = OrientExecutorRunner.RunUntilComplete(
            executor,
            async () => await table.GetOrCreateAsync(inbound, GreeterServiceId, executor));
        Assert.NotNull(link);

        var backendChannel = new EmbeddedChannel();
        SetBackendClientChannel(factory.LastClient!, backendChannel);
        GetClientHost(factory.LastClient!).PostChannelInactive(backendChannel);
        executor.Tick();

        Assert.Null(table.TryGet(inbound.ConnectionId));
        Assert.True(registry.TryGetPool(GreeterServiceId, out var pool));
        Assert.False(pool!.Endpoints[0].IsHealthy);
    }

    private static CRpcConnection RegisterInboundConnection(OrientExecutor executor, CRpcServer? server = null)
    {
        server ??= new CRpcServer(executor);
        var channel = new EmbeddedChannel();
        executor.Post(() => server.Connections.Register(channel));
        executor.Tick();

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

        public Orient.Rpc.Client.CRpcClient Create(OrientExecutor executor)
        {
            CreateCount++;
            return new Orient.Rpc.Client.CRpcClient(executor);
        }
    }

    private sealed class CapturingBackendClientFactory : global::GateWay.IBackendClientFactory
    {
        private readonly OrientExecutor executor;

        public CapturingBackendClientFactory(OrientExecutor executor)
        {
            this.executor = executor;
        }

        public Orient.Rpc.Client.CRpcClient? LastClient { get; private set; }

        public Orient.Rpc.Client.CRpcClient Create(OrientExecutor executor)
        {
            LastClient = new Orient.Rpc.Client.CRpcClient(this.executor);
            return LastClient;
        }
    }

    private sealed class SuccessBackendConnector : global::GateWay.IBackendConnector
    {
        public OrientTask ConnectAsync(Orient.Rpc.Client.CRpcClient client, global::GateWay.BackendEndpoint endpoint)
        {
            return OrientTask.CompletedTask(OrientExecutor.Current);
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
