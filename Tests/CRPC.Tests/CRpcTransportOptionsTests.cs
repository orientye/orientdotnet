using Orient.Runtime;
using Orient.Rpc.Client;
using Orient.Rpc.Codec;
using Orient.Rpc.Server;

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
        var loop = new OrientLoop();
        var configured = new CRpcClientOptions { MaxFrameLength = 1024 * 1024 };
        var client = new CRpcClient(loop, configured);

        Assert.Same(configured, client.Options);
    }

    [Fact]
    public void CRpcServerExposesConfiguredOptions()
    {
        var loop = new OrientLoop();
        var configured = new CRpcServerOptions { Port = 9001, SoBacklog = 1024 };
        var server = new CRpcServer(loop, configured);

        Assert.Same(configured, server.Options);
    }

    [Theory]
    [InlineData(65536)]
    [InlineData(-1)]
    public void CRpcServerOptionsValidateRejectsInvalidPort(int port)
    {
        var options = new CRpcServerOptions { Port = port };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal(nameof(CRpcServerOptions.Port), ex.ParamName);
    }

    [Fact]
    public void CRpcServerOptionsValidateAllowsEphemeralPortZero()
    {
        var options = new CRpcServerOptions { Port = 0 };
        options.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(CRpcServerOptions.MaxMaxFrameLength + 1)]
    public void CRpcServerOptionsValidateRejectsInvalidMaxFrameLength(int maxFrameLength)
    {
        var options = new CRpcServerOptions { MaxFrameLength = maxFrameLength };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal(nameof(CRpcServerOptions.MaxFrameLength), ex.ParamName);
    }

    [Theory]
    [InlineData(CRpcMessage.MinFrameLength)]
    [InlineData(CRpcServerOptions.MaxMaxFrameLength)]
    public void CRpcServerOptionsValidateAcceptsMaxFrameLengthBoundaries(int maxFrameLength)
    {
        var options = new CRpcServerOptions { MaxFrameLength = maxFrameLength };
        options.Validate();
    }

    [Theory]
    [InlineData(0, nameof(CRpcServerOptions.BossThreadCount))]
    [InlineData(0, nameof(CRpcServerOptions.WorkerThreadCount))]
    [InlineData(0, nameof(CRpcServerOptions.SoBacklog))]
    public void CRpcServerOptionsValidateRejectsNonPositiveCounts(int value, string paramName)
    {
        var options = paramName switch
        {
            nameof(CRpcServerOptions.BossThreadCount) => new CRpcServerOptions { BossThreadCount = value },
            nameof(CRpcServerOptions.WorkerThreadCount) => new CRpcServerOptions { WorkerThreadCount = value },
            nameof(CRpcServerOptions.SoBacklog) => new CRpcServerOptions { SoBacklog = value },
            _ => throw new ArgumentOutOfRangeException(nameof(paramName)),
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal(paramName, ex.ParamName);
    }

    [Fact]
    public void CRpcServerOptionsValidateSkipsReadIdleRulesWhenHeartbeatDisabled()
    {
        var options = new CRpcServerOptions
        {
            HeartbeatEnabled = false,
            ReadIdleSeconds = 0,
        };

        options.Validate(clientHeartbeatIntervalSeconds: 15);
    }

    [Fact]
    public void CRpcServerOptionsDefaultsPassValidate()
    {
        new CRpcServerOptions().Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(CRpcClientOptions.MaxMaxFrameLength + 1)]
    public void CRpcClientOptionsValidateRejectsInvalidMaxFrameLength(int maxFrameLength)
    {
        var options = new CRpcClientOptions { MaxFrameLength = maxFrameLength };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal(nameof(CRpcClientOptions.MaxFrameLength), ex.ParamName);
    }

    [Theory]
    [InlineData(0, nameof(CRpcClientOptions.IoThreadCount))]
    [InlineData(0, nameof(CRpcClientOptions.ConnectTimeoutSeconds))]
    [InlineData(0, nameof(CRpcClientOptions.CallTimeoutMilliseconds))]
    public void CRpcClientOptionsValidateRejectsNonPositiveTimeouts(int value, string paramName)
    {
        var options = paramName switch
        {
            nameof(CRpcClientOptions.IoThreadCount) => new CRpcClientOptions { IoThreadCount = value },
            nameof(CRpcClientOptions.ConnectTimeoutSeconds) => new CRpcClientOptions { ConnectTimeoutSeconds = value },
            nameof(CRpcClientOptions.CallTimeoutMilliseconds) => new CRpcClientOptions { CallTimeoutMilliseconds = value },
            _ => throw new ArgumentOutOfRangeException(nameof(paramName)),
        };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal(paramName, ex.ParamName);
    }

    [Fact]
    public void CRpcClientOptionsValidateSkipsHeartbeatIntervalWhenHeartbeatDisabled()
    {
        var options = new CRpcClientOptions
        {
            HeartbeatEnabled = false,
            HeartbeatIntervalSeconds = 0,
        };

        options.Validate();
    }

    [Fact]
    public void CRpcClientOptionsDefaultsPassValidate()
    {
        new CRpcClientOptions().Validate();
    }

    [Fact]
    public void CRpcClientConstructorRejectsInvalidOptionsBeforeConnect()
    {
        var loop = new OrientLoop();
        var options = new CRpcClientOptions { IoThreadCount = 0 };

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new CRpcClient(loop, options));
        Assert.Equal(nameof(CRpcClientOptions.IoThreadCount), ex.ParamName);
    }
}
