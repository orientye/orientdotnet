using System.Diagnostics;
using Orient.Runtime;

namespace Orient.Tests;

public class MinHeapTimerSchedulerTests : OrientTestBase
{
    [Fact]
    public void GetDelayUntilNextWakeupReturnsNullWhenEmpty()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();

        Assert.Null(scheduler.GetDelayUntilNextWakeup(now));
    }

    [Fact]
    public void GetDelayUntilNextWakeupReturnsZeroWhenDue()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        scheduler.ScheduleAt(now, () => { });

        Assert.Equal(TimeSpan.Zero, scheduler.GetDelayUntilNextWakeup(now));
    }

    [Fact]
    public void RunDueTimersInvokesCallbackWhenDue()
    {
        var scheduler = new MinHeapTimerScheduler();
        var ran = false;
        var now = Stopwatch.GetTimestamp();
        scheduler.ScheduleAt(now, () => ran = true);

        var count = scheduler.RunDueTimers(now, maxTimers: 8);

        Assert.Equal(1, count);
        Assert.True(ran);
    }

    [Fact]
    public void RunDueTimersSkipsCanceledTimer()
    {
        var scheduler = new MinHeapTimerScheduler();
        var ran = false;
        var now = Stopwatch.GetTimestamp();
        var timer = scheduler.ScheduleAt(now, () => ran = true);
        timer.Cancel();

        scheduler.RunDueTimers(now, maxTimers: 8);

        Assert.False(ran);
    }

    [Fact]
    public void GetDelayUntilNextWakeupReturnsPositiveDelayForFutureTimer()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        var future = now + Stopwatch.Frequency / 10;
        scheduler.ScheduleAt(future, () => { });

        var delay = scheduler.GetDelayUntilNextWakeup(now);

        Assert.NotNull(delay);
        Assert.True(delay!.Value.TotalMilliseconds > 50);
    }

    [Fact]
    public void CancelRemovesTimerFromScheduler()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        var future = now + Stopwatch.Frequency;
        var timer = scheduler.ScheduleAt(future, () => { });

        Assert.Equal(1, scheduler.TimerCount);

        timer.Cancel();

        Assert.Equal(0, scheduler.TimerCount);
        Assert.Null(scheduler.GetDelayUntilNextWakeup(now));
    }

    [Fact]
    public void CancelIsIdempotent()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        var timer = scheduler.ScheduleAt(now + Stopwatch.Frequency, () => { });

        timer.Cancel();
        timer.Cancel();

        Assert.Equal(0, scheduler.TimerCount);
    }

    [Fact]
    public void CancelNonHeadTimerPreservesRemainingOrder()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        var dueFirst = false;
        var dueThird = false;

        scheduler.ScheduleAt(now + Stopwatch.Frequency * 2, () => dueThird = true);
        var canceled = scheduler.ScheduleAt(now + Stopwatch.Frequency, () => { });
        scheduler.ScheduleAt(now, () => dueFirst = true);

        canceled.Cancel();

        Assert.Equal(2, scheduler.TimerCount);
        Assert.Equal(TimeSpan.Zero, scheduler.GetDelayUntilNextWakeup(now));

        scheduler.RunDueTimers(now, maxTimers: 8);
        Assert.True(dueFirst);
        Assert.False(dueThird);

        scheduler.RunDueTimers(now + Stopwatch.Frequency * 2, maxTimers: 8);
        Assert.True(dueThird);
    }

    [Fact]
    public void CancelAfterRunIsHarmless()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        var timer = scheduler.ScheduleAt(now, () => { });

        scheduler.RunDueTimers(now, maxTimers: 8);
        timer.Cancel();

        Assert.Equal(0, scheduler.TimerCount);
        Assert.True(timer.IsCanceled);
    }
}
