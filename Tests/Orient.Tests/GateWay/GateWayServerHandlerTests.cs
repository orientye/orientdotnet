using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.Protocol;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace Orient.Tests.GateWay;

public class GateWayServerHandlerTests : OrientTestBase
{
    [Fact]
    public void NoFallbackRegisteredWritesErrorResponse()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var sessions = GateWayTestHelpers.CreateSessionTable(
            new global::GateWay.DefaultBackendClientFactory(),
            new NoOpBackendConnector());
        var server = new CRpcServer(loop);
        var channel = CreateHandlerChannel(server, sessions, fallbackServiceId: 0);

        ActivateChannel(loop, channel);
        Assert.False(channel.WriteInbound(CRpcTestMessages.CreateRequest(serviceId: 1000)));
        loop.Tick();

        var response = ReadOutboundCrpcMessage(channel);
        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal((int)CRpcStatusCode.ServiceNotFound, response.ResultCode);
    }

    [Fact]
    public void MissingInboundConnectionWritesErrorResponse()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var sessions = GateWayTestHelpers.CreateSessionTable(
            new global::GateWay.DefaultBackendClientFactory(),
            new NoOpBackendConnector());
        var server = new CRpcServer(loop);
        var channel = CreateHandlerChannel(server, sessions, fallbackServiceId: 0);

        Assert.False(channel.WriteInbound(CRpcTestMessages.CreateRequest(serviceId: 1000)));
        loop.Tick();

        var response = ReadOutboundCrpcMessage(channel);
        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal((int)CRpcStatusCode.ServiceNotFound, response.ResultCode);
    }

    [Fact]
    public void FallbackRoutesToRegisteredForwarder()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var sessions = GateWayTestHelpers.CreateSessionTable(
            new global::GateWay.DefaultBackendClientFactory(),
            new NoOpBackendConnector());
        var forwarder = new RecordingForwarderService(fallbackServiceId: 0);
        var server = new CRpcServer(loop);
        RegisterOnServer(server, forwarder);
        var channel = CreateHandlerChannel(server, sessions, fallbackServiceId: 0);

        ActivateChannel(loop, channel);
        Assert.False(channel.WriteInbound(CRpcTestMessages.CreateRequest(serviceId: 1000)));
        loop.Tick();

        Assert.Equal(1, forwarder.CallCount);
        var response = ReadOutboundCrpcMessage(channel);
        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal(-1, response.ResultCode);
    }

    private static EmbeddedChannel CreateHandlerChannel(
        CRpcServer server,
        global::GateWay.GateWaySessionTable sessions,
        ushort fallbackServiceId)
    {
        return new EmbeddedChannel(
            new CRpcMessageEncoder(),
            new global::GateWay.GateWayServerHandler(server, sessions, fallbackServiceId));
    }

    private static CRpcMessage ReadOutboundCrpcMessage(EmbeddedChannel channel)
    {
        var outbound = channel.ReadOutbound<object>();
        return outbound is IByteBuffer buffer
            ? CRpcMessage.ReadFrom(buffer)
            : (CRpcMessage)outbound!;
    }

    private static void RegisterOnServer(CRpcServer server, IRpcService service)
    {
        server.Loop.Post(() => server.Services.Register(service));
        server.Loop.Tick();
    }

    private static void ActivateChannel(OrientLoop loop, EmbeddedChannel channel)
    {
        channel.Pipeline.FireChannelActive();
        loop.Tick();
    }

    private sealed class RecordingForwarderService : IRpcService
    {
        private readonly ushort fallbackServiceId;

        public RecordingForwarderService(ushort fallbackServiceId)
        {
            this.fallbackServiceId = fallbackServiceId;
        }

        public int CallCount { get; private set; }

        public ushort GetServiceId() => fallbackServiceId;

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            CallCount++;
            return OrientTask.FromResult((-1, Array.Empty<byte>()), OrientLoop.Current);
        }
    }

    private sealed class NoOpBackendConnector : global::GateWay.IBackendConnector
    {
        public OrientTask ConnectAsync(Orient.Rpc.Client.CRpcClient client, global::GateWay.BackendEndpoint endpoint)
        {
            return OrientTask.CompletedTask(OrientLoop.Current);
        }
    }
}
