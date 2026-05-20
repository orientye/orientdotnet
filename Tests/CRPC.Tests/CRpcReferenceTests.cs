using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;

namespace CRPC.Tests;

public class CRpcReferenceTests : CrpcTestBase
{
    [Fact]
    public void ProxyActivatorInjectsRpcClientIntoGeneratedClientField()
    {
        var rpcClient = new RecordingRpcClient();

        var proxy = CRpcProxyActivator.Create<TestGeneratedClient>(rpcClient);

        Assert.Same(rpcClient, proxy.__client);
    }

    [Fact]
    public void ProxyActivatorRejectsTypeWithoutGeneratedClientField()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CRpcProxyActivator.Create<InvalidGeneratedClient>(new RecordingRpcClient()));

        Assert.Contains("__client", exception.Message);
    }

    [Fact]
    public void ReferenceBuilderRejectsNonCrpcUrl()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CRpcReference.For<TestGeneratedClient>().Url("http://127.0.0.1:7999"));

        Assert.Contains("crpc://", exception.Message);
    }

    [Fact]
    public void ReferenceBuilderRejectsUrlWithoutPort()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CRpcReference.For<TestGeneratedClient>().Url("crpc://127.0.0.1"));

        Assert.Contains("port", exception.Message);
    }

    private sealed class TestGeneratedClient
    {
        public IRpcClient? __client;
    }

    private sealed class InvalidGeneratedClient
    {
    }

    private sealed class RecordingRpcClient : IRpcClient
    {
        public CRpcTask<CRpcMessage> CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout)
        {
            throw new NotSupportedException();
        }
    }
}
