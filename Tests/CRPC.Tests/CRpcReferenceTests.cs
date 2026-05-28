using CRpc.Async;
using CRpc.Rpc;
using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Codec;

namespace CRPC.Tests;

public class CRpcReferenceTests : CrpcTestBase
{
    [Fact]
    public void ProxyActivatorBindsGeneratedClientThroughInterface()
    {
        var rpcClient = new RecordingRpcClient();

        var proxy = CRpcProxyActivator.Create<TestGeneratedClient>(rpcClient);

        Assert.Same(rpcClient, proxy.Client);
        Assert.Equal(1, proxy.BindCount);
    }

    [Fact]
    public void ProxyActivatorRejectsTypeWithoutGeneratedClientInterface()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CRpcProxyActivator.Create<InvalidGeneratedClient>(new RecordingRpcClient()));

        Assert.Contains(nameof(ICRpcGeneratedClient), exception.Message);
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

    private sealed class TestGeneratedClient : ICRpcGeneratedClient
    {
        public IRpcClient? Client { get; private set; }

        public int BindCount { get; private set; }

        public void BindRpcClient(IRpcClient client)
        {
            Client = client;
            BindCount++;
        }
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

        public void RegisterPushHandler(ushort serviceId, ushort methodId, CRpcPushHandler handler)
        {
        }
    }
}
