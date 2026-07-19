using Orient.Rpc.Transport;

namespace Orient.Tests.Transport;

public sealed class TcpChannelHostOptionsTests
{
    [Fact]
    public void DefaultsAreSuitableForExistingClientTransport()
    {
        var options = new TcpChannelHostOptions();

        Assert.Equal(TcpChannelHostOptions.DefaultIoThreadCount, options.IoThreadCount);
        Assert.Equal(TcpChannelHostOptions.DefaultConnectTimeoutSeconds, options.ConnectTimeoutSeconds);
        Assert.True(options.TcpNoDelay);
        Assert.False(options.ChannelLoggingEnabled);
        Assert.Equal("tcp-channel", options.LoggingName);
    }

    [Fact]
    public void ValidateRejectsInvalidThreadCount()
    {
        var options = new TcpChannelHostOptions { IoThreadCount = 0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

        Assert.Equal("IoThreadCount", exception.ParamName);
    }

    [Fact]
    public void ValidateRejectsInvalidConnectTimeout()
    {
        var options = new TcpChannelHostOptions { ConnectTimeoutSeconds = 0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());

        Assert.Equal("ConnectTimeoutSeconds", exception.ParamName);
    }
}
