using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;
using DotNetty.Buffers;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class CRpcServerPushIntegrationTests : CrpcTestBase
{
    [Fact]
    public void ServiceCanPushToCurrentConnectionWithoutClientAck()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        var service = new PushOnRequestService();
        var server = new CRpcServer(loop);
        loop.RegisterService(service);
        var channel = new EmbeddedChannel(
            new CRpcMessageEncoder(
                CRpcServerOptions.DefaultHashLength,
                CRpcServerOptions.DefaultCompressThreshold),
            new CRpcServerHandler(server));

        channel.Pipeline.FireChannelActive();
        loop.Tick();

        Assert.False(channel.WriteInbound(CreateRequest(PushOnRequestService.ServiceId)));
        loop.Tick();
        loop.Tick();

        var push = ReadOutboundCrpcMessage(channel);
        var response = ReadOutboundCrpcMessage(channel);

        Assert.True(response.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.True(push.getHeader().hasState(CRpcMessageState.STATE_PUSH));
        Assert.False(push.getHeader().hasState(CRpcMessageState.STATE_RESPONSE));
        Assert.Equal(PushOnRequestService.ServiceId, push.getServiceId());
        Assert.Equal(PushOnRequestService.PushMethodId, push.getMethodId());
        Assert.Equal([7, 8, 9], push.getBody());
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

    private sealed class PushOnRequestService : IRpcService
    {
        public const ushort ServiceId = 1234;
        public const ushort PushMethodId = 2;

        public ushort GetServiceId() => ServiceId;

        public async CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)
        {
            var rpcContext = (CRpcContext)context;
            await rpcContext.Connection.SendPushAsync(ServiceId, PushMethodId, [7, 8, 9]);
            return (0, Array.Empty<byte>());
        }
    }
}
