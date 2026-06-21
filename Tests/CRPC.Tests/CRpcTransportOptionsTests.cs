using CRpc.Rpc.CRpc.Client;
using CRpc.Rpc.CRpc.Server;

namespace CRPC.Tests;

public class CRpcTransportOptionsTests
{
    [Fact]
    public void CRpcServerOptionsDefaultsMatchExpectedValues()
    {
        var options = new CRpcServerOptions();

        Assert.Equal(CRpcServerOptions.DefaultPort, options.Port);
        Assert.Equal(CRpcServerOptions.DefaultMaxFrameLength, options.MaxFrameLength);
        Assert.Equal(CRpcServerOptions.DefaultBossThreadCount, options.BossThreadCount);
        Assert.Equal(CRpcServerOptions.DefaultWorkerThreadCount, options.WorkerThreadCount);
        Assert.Equal(CRpcServerOptions.DefaultSoBacklog, options.SoBacklog);
        Assert.True(options.HeartbeatEnabled);
        Assert.Equal(CRpcServerOptions.DefaultReadIdleSeconds, options.ReadIdleSeconds);
    }

    [Fact]
    public void CRpcClientOptionsDefaultsMatchExpectedValues()
    {
        var options = new CRpcClientOptions();

        Assert.Equal(CRpcClientOptions.DefaultIoThreadCount, options.IoThreadCount);
        Assert.Equal(CRpcClientOptions.DefaultConnectTimeoutSeconds, options.ConnectTimeoutSeconds);
        Assert.True(options.HeartbeatEnabled);
        Assert.Equal(CRpcClientOptions.DefaultHeartbeatIntervalSeconds, options.HeartbeatIntervalSeconds);
        Assert.Equal(CRpcClientOptions.DefaultMaxFrameLength, options.MaxFrameLength);
        Assert.Equal(CRpcClientOptions.DefaultCallTimeoutMilliseconds, options.CallTimeoutMilliseconds);
    }

    [Fact]
    public void CRpcClientOptionsValidateRejectsNonPositiveInterval()
    {
        var options = new CRpcClientOptions { HeartbeatIntervalSeconds = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void CRpcServerOptionsValidateRequiresReadIdleAtLeastTwiceClientInterval()
    {
        var options = new CRpcServerOptions { ReadIdleSeconds = 10 };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            options.Validate(clientHeartbeatIntervalSeconds: 15));
    }

    [Fact]
    public void CRpcClientExposesConfiguredOptions()
    {
        var loop = new CRpc.Async.CRpcLoop();
        var configured = new CRpcClientOptions { MaxFrameLength = 1024 * 1024 };
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
