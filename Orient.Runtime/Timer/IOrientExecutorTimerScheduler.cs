namespace Orient.Runtime;

internal interface IOrientExecutorTimerScheduler
{
    OrientExecutorTimer ScheduleAt(long dueTimestamp, Action callback);

    int RunDueTimers(long now, int maxTimers);

    TimeSpan? GetDelayUntilNextWakeup(long now);
}
