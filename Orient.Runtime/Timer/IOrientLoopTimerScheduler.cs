namespace Orient.Runtime;

internal interface IOrientLoopTimerScheduler
{
    OrientLoopTimer ScheduleAt(long dueTimestamp, Action callback);

    int RunDueTimers(long now, int maxTimers);

    TimeSpan? GetDelayUntilNextWakeup(long now);
}
