namespace Orient.Logging;

public sealed class ConsoleOrientLogSink : IOrientLogSink
{
    private readonly TextWriter writer;

    public ConsoleOrientLogSink(TextWriter? writer = null)
    {
        this.writer = writer ?? Console.Out;
    }

    public void Write(IReadOnlyList<OrientLogEvent> batch)
    {
        foreach (var e in batch)
        {
            writer.Write(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            writer.Write(" [");
            writer.Write(LevelName(e.Level));
            writer.Write("] [T:");
            writer.Write(e.ManagedThreadId);
            writer.Write("] ");
            writer.Write(e.Category);
            writer.Write(' ');
            writer.Write(e.Message);
            if (e.Exception is not null)
            {
                writer.Write(" :: ");
                writer.Write(e.Exception);
            }
            writer.WriteLine();
        }
    }

    public void Flush() => writer.Flush();

    private static string LevelName(OrientLogLevel level) => level switch
    {
        OrientLogLevel.Trace => "TRACE",
        OrientLogLevel.Debug => "DEBUG",
        OrientLogLevel.Info => "INFO",
        OrientLogLevel.Warn => "WARN",
        OrientLogLevel.Error => "ERROR",
        OrientLogLevel.Fatal => "FATAL",
        _ => level.ToString().ToUpperInvariant(),
    };
}
