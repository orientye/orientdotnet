using System.Collections.Concurrent;

namespace Orient.Logging;

public sealed class OrientLogService : IOrientLoggerFactory, IAsyncDisposable
{
    public const int DefaultCapacity = 8192;
    public const int DefaultBatchSize = 256;

    private readonly IOrientLogSink sink;
    private readonly int capacity;
    private readonly int batchSize;
    private readonly OrientLogLevel minLevel;
    private readonly ConcurrentQueue<OrientLogEvent> queue = new();
    private readonly ManualResetEventSlim signal = new(false);
    private int queued;
    private long dropped;
    private long droppedSinceReport;
    private Thread? thread;
    private int started;
    private volatile bool accepting = true;
    private volatile bool running;

    public OrientLogService(
        IOrientLogSink sink,
        int capacity = DefaultCapacity,
        int batchSize = DefaultBatchSize,
        OrientLogLevel minLevel = OrientLogLevel.Info)
    {
        this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        this.capacity = capacity;
        this.batchSize = batchSize;
        this.minLevel = minLevel;
    }

    public long DroppedCount => Interlocked.Read(ref dropped);

    public void Start()
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
            return;
        running = true;
        thread = new Thread(ConsumeLoop) { IsBackground = true, Name = "orient-log" };
        thread.Start();
    }

    public IOrientLogger CreateLogger(string category) => new OrientLogger(this, category, minLevel);

    public bool TryWrite(OrientLogEvent ev)
    {
        if (!accepting) return false;
        while (true)
        {
            var current = Volatile.Read(ref queued);
            if (current >= capacity)
            {
                Interlocked.Increment(ref dropped);
                Interlocked.Increment(ref droppedSinceReport);
                return false;
            }
            if (Interlocked.CompareExchange(ref queued, current + 1, current) == current)
            {
                if (!accepting)
                {
                    Interlocked.Decrement(ref queued);
                    return false;
                }

                queue.Enqueue(ev);
                signal.Set();
                return true;
            }
        }
    }

    internal bool TryEnqueueForTests(OrientLogEvent ev) => TryWrite(ev);

    private void ConsumeLoop()
    {
        var batch = new List<OrientLogEvent>(batchSize);
        while (running || !queue.IsEmpty)
        {
            batch.Clear();
            while (batch.Count < batchSize && queue.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref queued);
                batch.Add(item);
            }

            if (batch.Count > 0)
            {
                try
                {
                    sink.Write(batch);
                    sink.Flush();
                }
                catch
                {
                    // isolate sink failures; do not crash log thread
                }
            }

            ReportDropsIfNeeded();

            if (batch.Count == 0)
            {
                signal.Reset();
                if (queue.IsEmpty && running)
                {
                    signal.Wait(100);
                }
            }
        }
    }

    private void ReportDropsIfNeeded()
    {
        var n = Interlocked.Exchange(ref droppedSinceReport, 0);
        if (n <= 0) return;
        try
        {
            var summary = new OrientLogEvent(
                DateTimeOffset.UtcNow,
                OrientLogLevel.Warn,
                eventId: 9000,
                category: "Orient.Logging",
                managedThreadId: Environment.CurrentManagedThreadId,
                message: $"Dropped {n} log event(s) due to full queue",
                exception: null);
            sink.Write(new[] { summary });
            sink.Flush();
        }
        catch { }
    }

    public ValueTask DisposeAsync()
    {
        accepting = false;
        running = false;
        signal.Set();
        var joined = thread?.Join(TimeSpan.FromSeconds(5)) != false;
        if (joined)
        {
            // Consumer is stopped: drain any events reserved/enqueued during the shutdown race.
            DrainRemaining();
            signal.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void DrainRemaining()
    {
        var batch = new List<OrientLogEvent>(batchSize > 0 ? batchSize : 16);
        while (queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref queued);
            batch.Add(item);
            if (batch.Count >= batch.Capacity)
            {
                WriteBatchIgnoreErrors(batch);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            WriteBatchIgnoreErrors(batch);
        }

        ReportDropsIfNeeded();
    }

    private void WriteBatchIgnoreErrors(List<OrientLogEvent> batch)
    {
        try
        {
            sink.Write(batch);
            sink.Flush();
        }
        catch
        {
            // isolate sink failures during shutdown drain
        }
    }
}
