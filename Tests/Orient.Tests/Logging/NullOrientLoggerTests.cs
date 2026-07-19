using Orient.Logging;

namespace Orient.Tests.Logging;

public sealed class NullOrientLoggerTests
{
    [Fact]
    public void Null_factory_returns_logger_that_does_not_throw()
    {
        var factory = NullOrientLoggerFactory.Instance;
        var logger = factory.CreateLogger("test");
        Assert.False(logger.IsEnabled(OrientLogLevel.Trace));
        Assert.False(logger.IsEnabled(OrientLogLevel.Error));
        logger.Log(OrientLogLevel.Error, 0, "msg", null);
    }
}
