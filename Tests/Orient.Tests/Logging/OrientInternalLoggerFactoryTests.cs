using System.Collections.Concurrent;
using DotNetty.Common.Internal.Logging;
using Orient.Logging;
using Orient.Rpc.Logging;

namespace Orient.Tests.Logging;

[CollectionDefinition("DotNettyLogging", DisableParallelization = true)]
public sealed class DotNettyLoggingCollection;

[Collection("DotNettyLogging")]
public sealed class OrientInternalLoggerFactoryTests
{
    private sealed class RecordingSink : IOrientLogSink
    {
        public ConcurrentQueue<OrientLogEvent> Events { get; } = new();

        public void Write(IReadOnlyList<OrientLogEvent> batch)
        {
            foreach (var logEvent in batch)
            {
                Events.Enqueue(logEvent);
            }
        }

        public void Flush()
        {
        }
    }

    [Fact]
    public async Task Info_message_reaches_orient_log_service_sink()
    {
        var sink = new RecordingSink();
        await using var service = new OrientLogService(sink);
        service.Start();

        using (OrientDotNettyLogging.Install(service))
        {
            var logger = InternalLoggerFactory.GetInstance("DotNetty.Test");

            logger.Info("connected to {}", "server");

            Assert.True(SpinWait.SpinUntil(
                () => sink.Events.Any(logEvent => logEvent.Message == "connected to server"),
                TimeSpan.FromSeconds(2)));
        }

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(OrientLogLevel.Info, logEvent.Level);
        Assert.Equal("DotNetty.Test", logEvent.Category);
        Assert.Equal("connected to server", logEvent.Message);
    }

    [Fact]
    public void Install_restores_previous_factory()
    {
        var previous = InternalLoggerFactory.DefaultFactory;
        var loggerFactory = new RecordingOrientLoggerFactory();

        using (OrientDotNettyLogging.Install(loggerFactory))
        {
            Assert.IsType<OrientInternalLoggerFactory>(InternalLoggerFactory.DefaultFactory);
        }

        Assert.Same(previous, InternalLoggerFactory.DefaultFactory);
    }
}
