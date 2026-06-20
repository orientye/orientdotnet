using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Buffers;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests.GateWayTests;

public class GateWayServerHandlerTests : CrpcTestBase
{
    [Fact]
    public void NoFallbackRegisteredWritesErrorResponse()
    {
        var loop = new CRpcLoop();
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
        Assert.Equal(-1, response.ResultCode);
    }

    [Fact]
    public void MissingInboundConnectionWritesErrorResponse()
    {
        var loop = new CRpcLoop();
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
        Assert.Equal(-1, response.ResultCode);
    }

    [Fact]
    public void FallbackRoutesToRegisteredForwarder()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var sessions = GateWayTestHelpers.CreateSessionTable(
            new global::GateWay.DefaultBackendClientFactory(),
            new NoOpBackendConnector());
        var forwarder = new RecordingForwarderService(fallbackServiceId: 0);
        var server = new CRpcServer(loop);
        RegisterOnLoop(loop, forwarder);
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

    private static void RegisterOnLoop(CRpcLoop loop, IRpcService service)
    {
        loop.Post(() => loop.RegisterService(service));
        loop.Tick();
    }

    private static void ActivateChannel(CRpcLoop loop, EmbeddedChannel channel)
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

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            CallCount++;
            return CRpcTask.FromResult((-1, Array.Empty<byte>()), CRpcLoop.Current);
        }
    }

    private sealed class NoOpBackendConnector : global::GateWay.IBackendConnector
    {
        public CRpcTask ConnectAsync(CRpc.Rpc.CRpc.Client.CRpcClient client, global::GateWay.BackendEndpoint endpoint)
        {
            return CRpcTask.CompletedTask(CRpcLoop.Current);
        }
    }
}
