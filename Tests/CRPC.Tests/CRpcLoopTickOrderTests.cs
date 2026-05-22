using CRpc.Async;

namespace CRPC.Tests;

public class CRpcLoopTickOrderTests : CrpcTestBase
{
    [Fact]
    public void TickRunsPostedActionsBeforeDueTimers()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var log = new List<string>();

        loop.ScheduleDelay(0, () => log.Add("timer"));
        loop.Post(() => log.Add("action"));

        loop.Tick(maxActions: 1024);

        Assert.Equal(new[] { "action", "timer" }, log);
    }

    [Fact]
    public void TickDrainsActionsAgainAfterTimerContinuationPosts()
    {
        var loop = new CRpcLoop();
        loop.BindToCurrentThread();

        var log = new List<string>();

        loop.ScheduleDelay(0, () =>
        {
            log.Add("timer");
            loop.Post(() => log.Add("posted-after-timer"));
        });

        loop.Tick(maxActions: 1024);

        Assert.Equal(new[] { "timer", "posted-after-timer" }, log);
    }
}
