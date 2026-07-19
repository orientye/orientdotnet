using Orient.Logging;
using Orient.Tests.Logging;

namespace Orient.Tests.GateWay;

public class GateWayConfigLoaderTests
{
    [Fact]
    public void MissingConfigIsLogged()
    {
        var loggerFactory = new RecordingOrientLoggerFactory();
        var logger = loggerFactory.CreateLogger("GateWay.Config");

        var config = global::GateWay.GateWayConfigLoader.LoadOrDefault(
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json"),
            logger);

        Assert.NotNull(config);
        var entry = Assert.Single(loggerFactory.Entries);
        Assert.Equal("GateWay.Config", entry.Category);
        Assert.Equal(OrientLogLevel.Warn, entry.Level);
        Assert.Equal(
            "Config file not found; using in-memory demo pool (7999 + 8001).",
            entry.Message);
    }
}
