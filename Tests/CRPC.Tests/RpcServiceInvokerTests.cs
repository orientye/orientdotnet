using Orient.Runtime;
using Orient.Rpc;
using Orient.Rpc.CRpc;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;
using DotNetty.Transport.Channels.Embedded;

namespace CRPC.Tests;

public class RpcServiceInvokerTests : CrpcTestBase
{
    [Fact]
    public void InvokeAsyncReturnsServiceResult()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();
        var service = new ByteReturnService(1000);

        (int code, byte[] body)? result = null;
        var request = CRpcTestMessages.CreateRequest(1000, methodId: 1, reqSequence: 1);
        var registry = new CRpcConnectionRegistry(loop);
        var connection = registry.Register(new EmbeddedChannel());
        var task = RpcServiceInvoker.InvokeAsync(service, new CRpcContext(connection), request);
        var awaiter = task.GetAwaiter();
        if (!awaiter.IsCompleted)
        {
            throw new InvalidOperationException("Expected synchronous completion.");
        }

        result = awaiter.GetResult();

        Assert.NotNull(result);
        Assert.Equal(0, result.Value.code);
        Assert.Equal(new byte[] { 9 }, result.Value.body);
    }

    [Fact]
    public void BuildCrpcResponseSetsMessageTypeResponse()
    {
        var request = CRpcTestMessages.CreateRequest(1000, methodId: 1, reqSequence: 1);
        var expectedBody = new byte[] { 9 };
        var response = RpcServiceInvoker.BuildCrpcResponse(request, code: 0, body: expectedBody);

        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal(expectedBody, response.Body);
    }

    [Fact]
    public void BuildFrameworkErrorResponseUsesEmptyBodyAndStatusCode()
    {
        var request = CRpcTestMessages.CreateRequest(1000, methodId: 2, reqSequence: 9);
        var response = RpcServiceInvoker.BuildFrameworkErrorResponse(request, CRpcStatusCode.ServiceNotFound);

        Assert.Equal(CRpcMessageType.Response, response.MessageType);
        Assert.Equal(1000, response.ServiceId);
        Assert.Equal(2, response.MethodId);
        Assert.Equal(9, response.ReqSequence);
        Assert.Equal((int)CRpcStatusCode.ServiceNotFound, response.ResultCode);
        Assert.Empty(response.Body);
    }

    [Fact]
    public void IsFrameworkCodeIdentifiesReservedRange()
    {
        Assert.True(CRpcStatusCodeExtensions.IsFrameworkCode((int)CRpcStatusCode.ServiceNotFound));
        Assert.True(CRpcStatusCodeExtensions.IsFrameworkCode(1999));
        Assert.False(CRpcStatusCodeExtensions.IsFrameworkCode(0));
        Assert.False(CRpcStatusCodeExtensions.IsFrameworkCode(500));
        Assert.False(CRpcStatusCodeExtensions.IsFrameworkCode(10001));
    }

    [Fact]
    public void IsApplicationCodeIdentifiesApplicationRange()
    {
        Assert.False(CRpcStatusCodeExtensions.IsApplicationCode(0));
        Assert.False(CRpcStatusCodeExtensions.IsApplicationCode(1001));
        Assert.False(CRpcStatusCodeExtensions.IsApplicationCode(10000));
        Assert.True(CRpcStatusCodeExtensions.IsApplicationCode(10001));
        Assert.True(CRpcStatusCodeExtensions.IsApplicationCode(20001));
    }

    private sealed class ByteReturnService : IRpcService
    {
        public ByteReturnService(ushort serviceId) => ServiceId = serviceId;

        public ushort ServiceId { get; }

        public ushort GetServiceId() => ServiceId;

        public OrientTask<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req) =>
            OrientTask.FromResult((0, new byte[] { 9 }));
    }
}
