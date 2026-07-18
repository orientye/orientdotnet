namespace Orient.Runtime;

public sealed class OrientExecutorOptions
{
    internal Func<IOrientExecutorTimerScheduler>? TimerSchedulerFactory { get; init; }

    internal IOrientExecutorTimerScheduler CreateTimerScheduler()
    {
        return TimerSchedulerFactory?.Invoke() ?? new MinHeapTimerScheduler();
    }
}
