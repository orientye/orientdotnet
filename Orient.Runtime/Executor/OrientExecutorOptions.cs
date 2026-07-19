using Orient.Logging;

namespace Orient.Runtime;

public sealed class OrientExecutorOptions
{
    public IOrientLoggerFactory? LoggerFactory { get; init; }

    internal Func<IOrientExecutorTimerScheduler>? TimerSchedulerFactory { get; init; }

    internal IOrientLogger CreateLogger(string category)
    {
        return (LoggerFactory ?? NullOrientLoggerFactory.Instance).CreateLogger(category);
    }

    internal IOrientExecutorTimerScheduler CreateTimerScheduler()
    {
        return TimerSchedulerFactory?.Invoke() ?? new MinHeapTimerScheduler();
    }
}
