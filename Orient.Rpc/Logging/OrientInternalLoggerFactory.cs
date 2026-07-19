using Orient.Logging;
using Microsoft.Extensions.Logging;

namespace Orient.Rpc.Logging;

public sealed class OrientInternalLoggerFactory : ILoggerFactory
{
    private readonly IOrientLoggerFactory loggerFactory;

    public OrientInternalLoggerFactory(IOrientLoggerFactory loggerFactory)
    {
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return new LoggerAdapter(loggerFactory.CreateLogger(categoryName));
    }

    public void AddProvider(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
    }

    public void Dispose()
    {
    }

    private sealed class LoggerAdapter(IOrientLogger logger) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            TryMapLevel(logLevel, out var level) && logger.IsEnabled(level);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!TryMapLevel(logLevel, out var level) || !logger.IsEnabled(level))
            {
                return;
            }

            logger.Log(level, eventId.Id, formatter(state, exception), exception);
        }

        private static bool TryMapLevel(LogLevel logLevel, out OrientLogLevel level)
        {
            level = logLevel switch
            {
                LogLevel.Trace => OrientLogLevel.Trace,
                LogLevel.Debug => OrientLogLevel.Debug,
                LogLevel.Information => OrientLogLevel.Info,
                LogLevel.Warning => OrientLogLevel.Warn,
                LogLevel.Error => OrientLogLevel.Error,
                LogLevel.Critical => OrientLogLevel.Fatal,
                _ => default,
            };

            return logLevel != LogLevel.None;
        }
    }
}
