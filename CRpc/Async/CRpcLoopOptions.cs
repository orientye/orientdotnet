namespace CRpc.Async;

public sealed class CRpcLoopOptions
{
    internal Func<ICRpcLoopTimerScheduler>? TimerSchedulerFactory { get; init; }

    internal ICRpcLoopTimerScheduler CreateTimerScheduler()
    {
        return TimerSchedulerFactory?.Invoke() ?? new MinHeapTimerScheduler();
    }
}
