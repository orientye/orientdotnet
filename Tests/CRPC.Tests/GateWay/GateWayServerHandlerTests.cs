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
        var sessions = CreateSessionTable();
        var server = new CRpcServer(loop);
        var channel = CreateHandlerChannel(server, sessions, fallbackServiceId: 0);

        ActivateChannel(loop, channel);
        Assert.False(channel.WriteInbound(CreateRequest(serviceId: 1000)));
        loop.Tick();

        var response = ReadOutboundCrpcMessage(channel);
        Assert.True(response.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.Equal(-1, response.getHeader().getResultCode());
    }

    [Fact]
    public void MissingInboundConnectionWritesErrorResponse()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var sessions = CreateSessionTable();
        var server = new CRpcServer(loop);
        var channel = CreateHandlerChannel(server, sessions, fallbackServiceId: 0);

        Assert.False(channel.WriteInbound(CreateRequest(serviceId: 1000)));
        loop.Tick();

        var response = ReadOutboundCrpcMessage(channel);
        Assert.True(response.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.Equal(-1, response.getHeader().getResultCode());
    }

    [Fact]
    public void FallbackRoutesToRegisteredForwarder()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var sessions = CreateSessionTable();
        var forwarder = new RecordingForwarderService(fallbackServiceId: 0);
        var server = new CRpcServer(loop);
        RegisterOnLoop(loop, forwarder);
        var channel = CreateHandlerChannel(server, sessions, fallbackServiceId: 0);

        ActivateChannel(loop, channel);
        Assert.False(channel.WriteInbound(CreateRequest(serviceId: 1000)));
        loop.Tick();

        Assert.Equal(1, forwarder.CallCount);
        var response = ReadOutboundCrpcMessage(channel);
        Assert.True(response.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.Equal(-1, response.getHeader().getResultCode());
    }

    private static global::GateWay.GateWaySessionTable CreateSessionTable()
    {
        return new global::GateWay.GateWaySessionTable(
            new global::GateWay.DefaultBackendClientFactory(),
            new NoOpBackendConnector(),
            new global::GateWay.GateWayPushRelay());
    }

    private static EmbeddedChannel CreateHandlerChannel(
        CRpcServer server,
        global::GateWay.GateWaySessionTable sessions,
        ushort fallbackServiceId)
    {
        return new EmbeddedChannel(
            new CRpcMessageEncoder(
                CRpcServerOptions.DefaultHashLength,
                CRpcServerOptions.DefaultCompressThreshold),
            new global::GateWay.GateWayServerHandler(server, sessions, fallbackServiceId));
    }

    private static CRpcMessage ReadOutboundCrpcMessage(EmbeddedChannel channel)
    {
        var outbound = channel.ReadOutbound<object>();
        return outbound is IByteBuffer buffer
            ? CRpcMessage.valueOf(buffer)
            : (CRpcMessage)outbound!;
    }

    private static CRpcMessage CreateRequest(ushort serviceId)
    {
        var header = CRpcMessageHeader.valueOf(
            CRpcMessageState.STATE_NONE,
            resultCode: 0,
            sn: 1,
            module: serviceId,
            command: 1);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        return CRpcMessage.valueOf(header, Array.Empty<byte>());
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
        public CRpcTask ConnectAsync(CRpc.Rpc.CRpc.Client.CRpcClient client, global::GateWay.GateWayOptions options)
        {
            return CRpcTask.CompletedTask(CRpcLoop.Current);
        }
    }
}
