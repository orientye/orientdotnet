using Orient.Logging;
using System.Collections.Concurrent;

namespace Orient.Tests.Logging;

public sealed class OrientLogServiceTests
{
    private sealed class RecordingSink : IOrientLogSink
    {
        public ConcurrentQueue<OrientLogEvent> Events { get; } = new();
        public ConcurrentQueue<int> WriterThreadIds { get; } = new();

        public void Write(IReadOnlyList<OrientLogEvent> batch)
        {
            WriterThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            foreach (var e in batch)
            {
                Events.Enqueue(e);
            }
        }

        public void Flush() { }
    }

    private sealed class GateSink : IOrientLogSink
    {
        private readonly ManualResetEventSlim gate = new(false);
        private int writeEntryCount;
        public ConcurrentQueue<OrientLogEvent> Events { get; } = new();

        public int WriteEntryCount => Volatile.Read(ref writeEntryCount);

        public void Release() => gate.Set();

        public void Write(IReadOnlyList<OrientLogEvent> batch)
        {
            Interlocked.Increment(ref writeEntryCount);
            gate.Wait();
            foreach (var e in batch)
            {
                Events.Enqueue(e);
            }
        }

        public void Flush() { }
    }

    private static OrientLogEvent MakeEvent(string message) =>
        new(DateTimeOffset.UtcNow, OrientLogLevel.Info, 0, "c", 1, message, null);

    [Fact]
    public async Task Enqueued_event_is_written_on_log_thread_with_producer_thread_id()
    {
        var sink = new RecordingSink();
        await using var service = new OrientLogService(sink, capacity: 128, batchSize: 16);
        service.Start();
        var logger = service.CreateLogger("cat");
        var producerTid = Environment.CurrentManagedThreadId;

        logger.Log(OrientLogLevel.Info, 1, "hello");

        Assert.True(SpinWait.SpinUntil(() => sink.Events.Count >= 1, TimeSpan.FromSeconds(2)));
        Assert.True(sink.Events.TryDequeue(out var ev));
        Assert.Equal("hello", ev.Message);
        Assert.Equal(producerTid, ev.ManagedThreadId);
        Assert.Equal("cat", ev.Category);
        Assert.True(sink.WriterThreadIds.TryPeek(out var writerTid));
        Assert.NotEqual(producerTid, writerTid);
    }

    [Fact]
    public async Task Full_queue_drops_and_does_not_block()
    {
        var sink = new RecordingSink();
        await using var service = new OrientLogService(sink, capacity: 2, batchSize: 1);
        // Do not Start — force queue to fill without drain
        var logger = service.CreateLogger("cat");
        Assert.True(service.TryEnqueueForTests(
            new OrientLogEvent(DateTimeOffset.UtcNow, OrientLogLevel.Info, 0, "c", 1, "a", null)));
        Assert.True(service.TryEnqueueForTests(
            new OrientLogEvent(DateTimeOffset.UtcNow, OrientLogLevel.Info, 0, "c", 1, "b", null)));
        Assert.False(service.TryEnqueueForTests(
            new OrientLogEvent(DateTimeOffset.UtcNow, OrientLogLevel.Info, 0, "c", 1, "c", null)));
        Assert.Equal(1, service.DroppedCount);
    }

    [Fact]
    public async Task DisposeAsync_drains_queued_events_to_sink()
    {
        var sink = new RecordingSink();
        await using var service = new OrientLogService(sink, capacity: 128, batchSize: 16);
        service.Start();

        const int count = 5;
        for (var i = 0; i < count; i++)
        {
            Assert.True(service.TryEnqueueForTests(MakeEvent($"msg-{i}")));
        }

        await service.DisposeAsync();

        var messages = new List<string>();
        while (sink.Events.TryDequeue(out var ev))
        {
            messages.Add(ev.Message);
        }

        Assert.Equal(count, messages.Count);
        for (var i = 0; i < count; i++)
        {
            Assert.Contains($"msg-{i}", messages);
        }
    }

    [Fact]
    public async Task Running_service_reports_drop_summary_when_queue_overflows()
    {
        var sink = new GateSink();
        await using var service = new OrientLogService(sink, capacity: 4, batchSize: 1);
        service.Start();

        Assert.True(service.TryEnqueueForTests(MakeEvent("first")));
        Assert.True(SpinWait.SpinUntil(() => sink.WriteEntryCount >= 1, TimeSpan.FromSeconds(2)));

        for (var i = 0; i < 8; i++)
        {
            service.TryEnqueueForTests(MakeEvent($"burst-{i}"));
        }

        Assert.True(service.DroppedCount > 0, "Expected at least one dropped event while consumer is blocked.");

        sink.Release();

        Assert.True(SpinWait.SpinUntil(
            () => sink.Events.Any(e => e.EventId == 9000 || e.Message.Contains("Dropped", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(2)));

        var summary = sink.Events.First(e => e.EventId == 9000 || e.Message.Contains("Dropped", StringComparison.Ordinal));
        Assert.Equal(OrientLogLevel.Warn, summary.Level);
        Assert.Equal("Orient.Logging", summary.Category);
    }
}
