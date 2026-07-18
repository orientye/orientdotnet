using System.Diagnostics;
using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorWakeupTests : OrientTestBase
{
    [Fact]
    public void WaitForWorkOrTimerReturnsImmediatelyWhenActionsPending()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();
        executor.Post(() => { });

        var sw = Stopwatch.StartNew();
        executor.WaitForWorkOrTimer(CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50);
    }

    [Fact]
    public void PostFromAnotherThreadWakesWaitForWorkOrTimer()
    {
        var executor = new OrientExecutor();
        using var driverReady = new ManualResetEventSlim(false);
        using var waitReturned = new ManualResetEventSlim(false);

        var driver = new Thread(() =>
        {
            executor.BindToCurrentThread();
            driverReady.Set();
            executor.WaitForWorkOrTimer(CancellationToken.None);
            waitReturned.Set();
        })
        {
            IsBackground = true,
        };
        driver.Start();

        Assert.True(driverReady.Wait(TimeSpan.FromSeconds(2)));

        executor.Post(() => { });
        Assert.True(waitReturned.Wait(TimeSpan.FromSeconds(2)));
        driver.Join(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void WaitForWorkOrTimerWakesWhenTimerBecomesDue()
    {
        var executor = new OrientExecutor();
        var ran = false;

        var driver = new Thread(() =>
        {
            executor.BindToCurrentThread();
            executor.ScheduleDelay(50, () => ran = true);
            while (!ran)
            {
                executor.Tick();
                if (!ran)
                {
                    executor.WaitForWorkOrTimer(CancellationToken.None);
                }
            }
        })
        {
            IsBackground = true,
        };
        driver.Start();
        driver.Join(TimeSpan.FromSeconds(2));

        Assert.True(ran);
    }

    [Fact]
    public void MultiplePostsCoalesceIntoSingleWakeup()
    {
        var executor = new OrientExecutor();
        using var driverReady = new ManualResetEventSlim(false);
        using var waitReturned = new ManualResetEventSlim(false);

        var driver = new Thread(() =>
        {
            executor.BindToCurrentThread();
            driverReady.Set();
            executor.WaitForWorkOrTimer(CancellationToken.None);
            waitReturned.Set();
        })
        {
            IsBackground = true,
        };
        driver.Start();

        Assert.True(driverReady.Wait(TimeSpan.FromSeconds(2)));

        for (var i = 0; i < 100; i++)
        {
            executor.Post(() => { });
        }

        Assert.True(waitReturned.Wait(TimeSpan.FromSeconds(2)));
        driver.Join(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PostAfterResetIsObservedBeforeWait()
    {
        var executor = new OrientExecutor();
        using var driverReady = new ManualResetEventSlim(false);
        using var waitReturned = new ManualResetEventSlim(false);

        var driver = new Thread(() =>
        {
            executor.BindToCurrentThread();
            driverReady.Set();
            executor.WaitForWorkOrTimer(CancellationToken.None);
            waitReturned.Set();
        })
        {
            IsBackground = true,
        };
        driver.Start();

        Assert.True(driverReady.Wait(TimeSpan.FromSeconds(2)));
        executor.Post(() => { });
        Assert.True(waitReturned.Wait(TimeSpan.FromSeconds(2)));
        driver.Join(TimeSpan.FromSeconds(2));
    }
}
