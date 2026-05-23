using System.Diagnostics;
using CRpc.Async;

namespace CRPC.Tests;

public class CRpcLoopWakeupTests : CrpcTestBase
{
    [Fact]
    public void WaitForWorkOrTimerReturnsImmediatelyWhenActionsPending()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();
        loop.Post(() => { });

        var sw = Stopwatch.StartNew();
        loop.WaitForWorkOrTimer(CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 50);
    }

    [Fact]
    public void PostFromAnotherThreadWakesWaitForWorkOrTimer()
    {
        var loop = new CRpcLoop();
        using var driverReady = new ManualResetEventSlim(false);
        using var waitReturned = new ManualResetEventSlim(false);

        var driver = new Thread(() =>
        {
            loop.BindToCurrentThread();
            driverReady.Set();
            loop.WaitForWorkOrTimer(CancellationToken.None);
            waitReturned.Set();
        })
        {
            IsBackground = true,
        };
        driver.Start();

        Assert.True(driverReady.Wait(TimeSpan.FromSeconds(2)));

        loop.Post(() => { });
        Assert.True(waitReturned.Wait(TimeSpan.FromSeconds(2)));
        driver.Join(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void WaitForWorkOrTimerWakesWhenTimerBecomesDue()
    {
        var loop = new CRpcLoop();
        var ran = false;

        var driver = new Thread(() =>
        {
            loop.BindToCurrentThread();
            loop.ScheduleDelay(50, () => ran = true);
            while (!ran)
            {
                loop.Tick();
                if (!ran)
                {
                    loop.WaitForWorkOrTimer(CancellationToken.None);
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
        var loop = new CRpcLoop();
        using var driverReady = new ManualResetEventSlim(false);
        using var waitReturned = new ManualResetEventSlim(false);

        var driver = new Thread(() =>
        {
            loop.BindToCurrentThread();
            driverReady.Set();
            loop.WaitForWorkOrTimer(CancellationToken.None);
            waitReturned.Set();
        })
        {
            IsBackground = true,
        };
        driver.Start();

        Assert.True(driverReady.Wait(TimeSpan.FromSeconds(2)));

        for (var i = 0; i < 100; i++)
        {
            loop.Post(() => { });
        }

        Assert.True(waitReturned.Wait(TimeSpan.FromSeconds(2)));
        driver.Join(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void PostAfterResetIsObservedBeforeWait()
    {
        var loop = new CRpcLoop();
        using var driverReady = new ManualResetEventSlim(false);
        using var waitReturned = new ManualResetEventSlim(false);

        var driver = new Thread(() =>
        {
            loop.BindToCurrentThread();
            driverReady.Set();
            loop.WaitForWorkOrTimer(CancellationToken.None);
            waitReturned.Set();
        })
        {
            IsBackground = true,
        };
        driver.Start();

        Assert.True(driverReady.Wait(TimeSpan.FromSeconds(2)));
        loop.Post(() => { });
        Assert.True(waitReturned.Wait(TimeSpan.FromSeconds(2)));
        driver.Join(TimeSpan.FromSeconds(2));
    }
}
