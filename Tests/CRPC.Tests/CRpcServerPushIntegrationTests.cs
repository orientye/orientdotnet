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
            new CRpcMessageEncoder(),
            new CRpcServerHandler(server));

        channel.Pipeline.FireChannelActive();
        loop.Tick();

        Assert.False(channel.WriteInbound(CRpcTestMessages.CreateRequest(PushOnRequestService.ServiceId)));
        loop.Tick();
        loop.Tick();

        var push = ReadOutboundCrpcMessage(channel);
        var response = ReadOutboundCrpcMessage(channel);

        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal(CRpcMessageType.Push, push.MessageType);
        Assert.Equal(PushOnRequestService.ServiceId, push.ServiceId);
        Assert.Equal(PushOnRequestService.PushMethodId, push.MethodId);
        Assert.Equal([7, 8, 9], push.Body);
    }

    private static CRpcMessage ReadOutboundCrpcMessage(EmbeddedChannel channel)
    {
        var outbound = channel.ReadOutbound<object>();
        return outbound is IByteBuffer buffer
            ? CRpcMessage.ReadFrom(buffer)
            : (CRpcMessage)outbound!;
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
