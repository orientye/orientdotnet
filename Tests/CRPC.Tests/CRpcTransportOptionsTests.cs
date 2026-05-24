using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcTransportOptionsTests
{
    [Fact]
    public void CRpcServerOptionsDefaultsMatchLegacyHardcodedValues()
    {
        var options = new CRpcServerOptions();

        Assert.Equal(CRpcServerOptions.DefaultPort, options.Port);
        Assert.Equal(CRpcServerOptions.DefaultMaxFrameLength, options.MaxFrameLength);
        Assert.Equal(CRpcServerOptions.DefaultHashLength, options.HashLength);
        Assert.Equal(CRpcServerOptions.DefaultCompressThreshold, options.CompressThreshold);
        Assert.Equal(CRpcServerOptions.DefaultBossThreadCount, options.BossThreadCount);
        Assert.Equal(CRpcServerOptions.DefaultWorkerThreadCount, options.WorkerThreadCount);
        Assert.Equal(CRpcServerOptions.DefaultSoBacklog, options.SoBacklog);
    }

    [Fact]
    public void CRpcClientOptionsDefaultsMatchLegacyHardcodedValues()
    {
        var options = new CRpcClientOptions();

        Assert.Equal(CRpcClientOptions.DefaultIoThreadCount, options.IoThreadCount);
        Assert.Equal(CRpcClientOptions.DefaultConnectTimeoutSeconds, options.ConnectTimeoutSeconds);
        Assert.Equal(CRpcClientOptions.DefaultHeartbeatIdleSeconds, options.HeartbeatIdleSeconds);
        Assert.Equal(CRpcClientOptions.DefaultMaxFrameLength, options.MaxFrameLength);
        Assert.Equal(CRpcClientOptions.DefaultHashLength, options.HashLength);
        Assert.Equal(CRpcClientOptions.DefaultCompressThreshold, options.CompressThreshold);
        Assert.Equal(CRpcClientOptions.DefaultCallTimeoutMilliseconds, options.CallTimeoutMilliseconds);
    }

    [Fact]
    public void CRpcClientExposesConfiguredOptions()
    {
        var loop = new CRpc.Async.CRpcLoop();
        var configured = new CRpcClientOptions { HashLength = 8, CompressThreshold = 256 };
        var client = new CRpcClient(loop, configured);

        Assert.Same(configured, client.Options);
    }

    [Fact]
    public void CRpcServerExposesConfiguredOptions()
    {
        var loop = new CRpc.Async.CRpcLoop();
        var configured = new CRpcServerOptions { Port = 9001, SoBacklog = 1024 };
        var server = new CRpcServer(loop, configured);

        Assert.Same(configured, server.Options);
    }
}
