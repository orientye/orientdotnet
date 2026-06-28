using Orient.Runtime;

namespace Orient.Tests;

public class OrientLoopTickOrderTests : OrientTestBase
{
    [Fact]
    public void TickRunsPostedActionsBeforeDueTimers()
    {
        var loop = new OrientLoop();
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
        var loop = new OrientLoop();
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
