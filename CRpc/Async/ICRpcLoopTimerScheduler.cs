namespace CRpc.Async;

internal interface ICRpcLoopTimerScheduler
{
    CRpcLoopTimer ScheduleAt(long dueTimestamp, Action callback);

    int RunDueTimers(long now, int maxTimers);

    TimeSpan? GetDelayUntilNextWakeup(long now);
}
