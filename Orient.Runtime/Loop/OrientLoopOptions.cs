namespace Orient.Runtime;

public sealed class OrientLoopOptions
{
    internal Func<IOrientLoopTimerScheduler>? TimerSchedulerFactory { get; init; }

    internal IOrientLoopTimerScheduler CreateTimerScheduler()
    {
        return TimerSchedulerFactory?.Invoke() ?? new MinHeapTimerScheduler();
    }
}
