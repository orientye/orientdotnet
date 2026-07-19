using System.Collections.Concurrent;
using Orient.Logging;
using Orient.Runtime;

namespace Orient.Tests.Logging;

public sealed class OrientExecutorLoggingTests
{
    private static readonly object ConsoleErrorGate = new();

    [Fact]
    public void Unhandled_with_null_factory_writes_stderr()
    {
        lock (ConsoleErrorGate)
        {
            var originalError = Console.Error;
            using var error = new StringWriter();
            try
            {
                Console.SetError(error);
                var executor = new OrientExecutor();
                executor.Post(() => throw new InvalidOperationException("null-factory-failure"));

                executor.Tick();

                Assert.Contains("OrientExecutor unhandled exception", error.ToString());
                Assert.Contains("null-factory-failure", error.ToString());
            }
            finally
            {
                Console.SetError(originalError);
#if DEBUG
                OrientExecutor.ResetDebugThreadBindingForTests();
#endif
            }
        }
    }

    [Fact]
    public async Task Unhandled_with_service_factory_writes_to_sink_not_only_stderr()
    {
        var sink = new ConcurrentBagSink();
        await using var service = new OrientLogService(
            sink,
            capacity: 128,
            batchSize: 16,
            minLevel: OrientLogLevel.Error);
        service.Start();

        lock (ConsoleErrorGate)
        {
            var originalError = Console.Error;
            using var error = new StringWriter();
            try
            {
                Console.SetError(error);
                var executor = new OrientExecutor(new OrientExecutorOptions
                {
                    LoggerFactory = service
                });
                executor.Post(() => throw new InvalidOperationException("service-factory-failure"));

                executor.Tick();

                Assert.True(SpinWait.SpinUntil(
                    () => sink.Events.Any(e => e.EventId == 1001),
                    TimeSpan.FromSeconds(2)));
                var logged = Assert.Single(sink.Events.Where(e => e.EventId == 1001));
                Assert.Equal(OrientLogLevel.Error, logged.Level);
                Assert.Equal("Orient.Runtime.OrientExecutor", logged.Category);
                Assert.Contains("OrientExecutor unhandled exception", logged.Message);
                Assert.Equal("service-factory-failure", logged.Exception?.Message);
                Assert.DoesNotContain("service-factory-failure", error.ToString());
            }
            finally
            {
                Console.SetError(originalError);
#if DEBUG
                OrientExecutor.ResetDebugThreadBindingForTests();
#endif
            }
        }
    }

    private sealed class ConcurrentBagSink : IOrientLogSink
    {
        public ConcurrentBag<OrientLogEvent> Events { get; } = new();

        public void Write(IReadOnlyList<OrientLogEvent> batch)
        {
            foreach (var logEvent in batch)
            {
                Events.Add(logEvent);
            }
        }

        public void Flush()
        {
        }
    }
}
