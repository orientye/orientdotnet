using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorTickOrderTests : OrientTestBase
{
    [Fact]
    public void TickRunsPostedActionsBeforeDueTimers()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var log = new List<string>();

        executor.ScheduleDelay(0, () => log.Add("timer"));
        executor.Post(() => log.Add("action"));

        executor.Tick(maxActions: 1024);

        Assert.Equal(new[] { "action", "timer" }, log);
    }

    [Fact]
    public void TickDrainsActionsAgainAfterTimerContinuationPosts()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var log = new List<string>();

        executor.ScheduleDelay(0, () =>
        {
            log.Add("timer");
            executor.Post(() => log.Add("posted-after-timer"));
        });

        executor.Tick(maxActions: 1024);

        Assert.Equal(new[] { "timer", "posted-after-timer" }, log);
    }
}
