namespace Orient.Logging;

public interface IOrientLogger
{
    string Category { get; }
    bool IsEnabled(OrientLogLevel level);
    void Log(OrientLogLevel level, int eventId, string message, Exception? exception = null);
}
