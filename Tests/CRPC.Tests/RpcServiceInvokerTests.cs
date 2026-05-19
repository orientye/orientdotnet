using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Codec;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class RpcServiceInvokerTests
{
    [Fact]
    public void InvokeAsyncReturnsServiceResult()
    {
        var loop = new CRpcLoop();
        var service = new ByteReturnService(1000);
        loop.Post(() => loop.RegisterService(service));
        loop.Tick();

        (int code, byte[] body)? result = null;
        var request = CreateRequest(1000, 1);
        loop.Post(() =>
        {
            var task = RpcServiceInvoker.InvokeAsync(service, new CRpcContext(), request);
            var awaiter = task.GetAwaiter();
            if (!awaiter.IsCompleted)
            {
                throw new InvalidOperationException("Expected synchronous completion.");
            }

            result = awaiter.GetResult();
        });
        loop.Tick();

        Assert.NotNull(result);
        Assert.Equal(0, result.Value.code);
        Assert.Equal(new byte[] { 9 }, result.Value.body);
    }

    private static CRpcMessage CreateRequest(ushort serviceId, ushort methodId)
    {
        var header = CRpcMessageHeader.valueOf(CRpcMessageState.STATE_NONE, 0, 1, serviceId, methodId);
        header.addState(CRpcMessageState.NONE_ENCRYPT);
        return CRpcMessage.valueOf(header, Array.Empty<byte>());
    }

    private sealed class ByteReturnService : IRpcService
    {
        public ByteReturnService(ushort serviceId) => ServiceId = serviceId;

        public ushort ServiceId { get; }

        public ushort GetServiceId() => ServiceId;

        public CRpcTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req) =>
            CRpcTask.FromResult((0, new byte[] { 9 }));
    }
}
