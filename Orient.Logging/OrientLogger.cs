namespace Orient.Logging;

public sealed class OrientLogger : IOrientLogger
{
    private readonly OrientLogService service;
    private readonly OrientLogLevel minLevel;

    public OrientLogger(OrientLogService service, string category, OrientLogLevel minLevel)
    {
        this.service = service;
        Category = category;
        this.minLevel = minLevel;
    }

    public string Category { get; }

    public bool IsEnabled(OrientLogLevel level) => level >= minLevel;

    public void Log(OrientLogLevel level, int eventId, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var ev = new OrientLogEvent(
            DateTimeOffset.UtcNow,
            level,
            eventId,
            Category,
            Environment.CurrentManagedThreadId,
            message,
            exception);
        service.TryWrite(ev);
    }
}
