namespace Orient.Logging;

public sealed class OrientLogEvent
{
    public OrientLogEvent(
        DateTimeOffset timestamp,
        OrientLogLevel level,
        int eventId,
        string category,
        int managedThreadId,
        string message,
        Exception? exception)
    {
        Timestamp = timestamp;
        Level = level;
        EventId = eventId;
        Category = category;
        ManagedThreadId = managedThreadId;
        Message = message;
        Exception = exception;
    }

    public DateTimeOffset Timestamp { get; }
    public OrientLogLevel Level { get; }
    public int EventId { get; }
    public string Category { get; }
    public int ManagedThreadId { get; }
    public string Message { get; }
    public Exception? Exception { get; }
}
