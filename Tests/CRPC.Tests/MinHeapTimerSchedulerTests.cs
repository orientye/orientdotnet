using System.Diagnostics;
using CRpc.Async;

namespace CRPC.Tests;

public class MinHeapTimerSchedulerTests : CrpcTestBase
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
}
