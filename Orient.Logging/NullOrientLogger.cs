namespace Orient.Logging;

public sealed class NullOrientLogger : IOrientLogger
{
    public static NullOrientLogger Instance { get; } = new();
    private NullOrientLogger() { }
    public string Category => "Null";
    public bool IsEnabled(OrientLogLevel level) => false;
    public void Log(OrientLogLevel level, int eventId, string message, Exception? exception = null) { }
}

public sealed class NullOrientLoggerFactory : IOrientLoggerFactory
{
    public static NullOrientLoggerFactory Instance { get; } = new();
    private NullOrientLoggerFactory() { }
    public IOrientLogger CreateLogger(string category) => NullOrientLogger.Instance;
}
