using System.Collections.Concurrent;
using Orient.Logging;

namespace Orient.Tests.Logging;

internal sealed class RecordingOrientLoggerFactory : IOrientLoggerFactory
{
    public ConcurrentBag<RecordedLogEntry> Entries { get; } = new();

    public IOrientLogger CreateLogger(string category) => new RecordingOrientLogger(category, Entries);

    private sealed class RecordingOrientLogger(
        string category,
        ConcurrentBag<RecordedLogEntry> entries) : IOrientLogger
    {
        public string Category { get; } = category;

        public bool IsEnabled(OrientLogLevel level) => true;

        public void Log(
            OrientLogLevel level,
            int eventId,
            string message,
            Exception? exception = null)
        {
            entries.Add(new RecordedLogEntry(Category, level, eventId, message, exception));
        }
    }
}

internal sealed record RecordedLogEntry(
    string Category,
    OrientLogLevel Level,
    int EventId,
    string Message,
    Exception? Exception);
