using Orient.Runtime;

namespace Orient.Tests;

public class OrientLoopExceptionIsolationTests : OrientTestBase
{
    [Fact]
    public void TickContinuesProcessingActionsAfterOneThrows()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        var executed = new List<int>();
        loop.Post(() => executed.Add(1));
        loop.Post(() => throw new InvalidOperationException("boom"));
        loop.Post(() => executed.Add(2));

        loop.Tick();

        Assert.Equal(new[] { 1, 2 }, executed);
    }

    [Fact]
    public void TickRaisesUnhandledExceptionForFailingAction()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        Exception? captured = null;
        loop.UnhandledException += ex => captured = ex;

        var failure = new InvalidOperationException("action failed");
        loop.Post(() => throw failure);

        loop.Tick();

        Assert.Same(failure, captured);
    }

    [Fact]
    public void TickContinuesProcessingTimersAfterOneThrows()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        var executed = new List<int>();
        loop.ScheduleDelay(0, () => executed.Add(1));
        loop.ScheduleDelay(0, () => throw new InvalidOperationException("timer boom"));
        loop.ScheduleDelay(0, () => executed.Add(2));

        loop.Tick();

        Assert.Equal(new[] { 1, 2 }, executed);
    }

    [Fact]
    public void TickRaisesUnhandledExceptionForFailingTimer()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        Exception? captured = null;
        loop.UnhandledException += ex => captured = ex;

        var failure = new InvalidOperationException("timer failed");
        loop.ScheduleDelay(0, () => throw failure);

        loop.Tick();

        Assert.Same(failure, captured);
    }

    [Fact]
    public void TickDoesNotPropagateExceptionsWhenNoHandlerSubscribed()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        loop.Post(() => throw new InvalidOperationException("no handler"));

        var followUpRan = false;
        loop.Post(() => followUpRan = true);

        var exception = Record.Exception(() => loop.Tick());

        Assert.Null(exception);
        Assert.True(followUpRan);
    }

    [Fact]
    public void TickSurvivesExceptionsThrownByUnhandledExceptionHandler()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        loop.UnhandledException += _ => throw new InvalidOperationException("handler boom");

        var followUpRan = false;
        loop.Post(() => throw new InvalidOperationException("first"));
        loop.Post(() => followUpRan = true);

        var exception = Record.Exception(() => loop.Tick());

        Assert.Null(exception);
        Assert.True(followUpRan);
    }
}
