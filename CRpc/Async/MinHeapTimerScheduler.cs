using System.Diagnostics;

namespace CRpc.Async;

internal sealed class MinHeapTimerScheduler : ICRpcLoopTimerScheduler
{
    private readonly PriorityQueue<ScheduledTimer, long> timers = new();

    public CRpcLoopTimer ScheduleAt(long dueTimestamp, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new CRpcLoopTimer(callback);
        timers.Enqueue(new ScheduledTimer(timer), dueTimestamp);
        return timer;
    }

    public int RunDueTimers(long now, int maxTimers)
    {
        var ran = 0;
        while (ran < maxTimers
               && timers.TryPeek(out var scheduledTimer, out var dueTimestamp)
               && dueTimestamp <= now)
        {
            timers.Dequeue();
            ran++;
            scheduledTimer.Timer.Invoke();
        }

        return ran;
    }

    public TimeSpan? GetDelayUntilNextWakeup(long now)
    {
        if (!timers.TryPeek(out _, out var dueTimestamp))
        {
            return null;
        }

        if (dueTimestamp <= now)
        {
            return TimeSpan.Zero;
        }

        var ticks = dueTimestamp - now;
        return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
    }

    private readonly record struct ScheduledTimer(CRpcLoopTimer Timer);
}
