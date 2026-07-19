using Orient.Logging;

namespace Orient.Tests.Logging;

public sealed class ConsoleOrientLogSinkTests
{
    [Fact]
    public void Formats_timestamp_level_thread_category_message()
    {
        var writer = new StringWriter();
        var sink = new ConsoleOrientLogSink(writer);
        var ev = new OrientLogEvent(
            new DateTimeOffset(2026, 7, 19, 15, 26, 10, 123, TimeSpan.Zero),
            OrientLogLevel.Warn,
            1,
            "Orient.Rpc.Client.CRpcClient",
            12,
            "RPC call timed out",
            null);
        sink.Write(new[] { ev });
        var line = writer.ToString();
        Assert.Contains("[WARN]", line);
        Assert.Contains("[T:12]", line);
        Assert.Contains("Orient.Rpc.Client.CRpcClient", line);
        Assert.Contains("RPC call timed out", line);
    }
}
