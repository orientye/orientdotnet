using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorExceptionIsolationTests : OrientTestBase
{
    [Fact]
    public void TickContinuesProcessingActionsAfterOneThrows()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var executed = new List<int>();
        executor.Post(() => executed.Add(1));
        executor.Post(() => throw new InvalidOperationException("boom"));
        executor.Post(() => executed.Add(2));

        executor.Tick();

        Assert.Equal(new[] { 1, 2 }, executed);
    }

    [Fact]
    public void TickRaisesUnhandledExceptionForFailingAction()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        Exception? captured = null;
        executor.UnhandledException += ex => captured = ex;

        var failure = new InvalidOperationException("action failed");
        executor.Post(() => throw failure);

        executor.Tick();

        Assert.Same(failure, captured);
    }

    [Fact]
    public void TickContinuesProcessingTimersAfterOneThrows()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        var executed = new List<int>();
        executor.ScheduleDelay(0, () => executed.Add(1));
        executor.ScheduleDelay(0, () => throw new InvalidOperationException("timer boom"));
        executor.ScheduleDelay(0, () => executed.Add(2));

        executor.Tick();

        Assert.Equal(new[] { 1, 2 }, executed);
    }

    [Fact]
    public void TickRaisesUnhandledExceptionForFailingTimer()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        Exception? captured = null;
        executor.UnhandledException += ex => captured = ex;

        var failure = new InvalidOperationException("timer failed");
        executor.ScheduleDelay(0, () => throw failure);

        executor.Tick();

        Assert.Same(failure, captured);
    }

    [Fact]
    public void TickDoesNotPropagateExceptionsWhenNoHandlerSubscribed()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        executor.Post(() => throw new InvalidOperationException("no handler"));

        var followUpRan = false;
        executor.Post(() => followUpRan = true);

        var exception = Record.Exception(() => executor.Tick());

        Assert.Null(exception);
        Assert.True(followUpRan);
    }

    [Fact]
    public void TickSurvivesExceptionsThrownByUnhandledExceptionHandler()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        executor.UnhandledException += _ => throw new InvalidOperationException("handler boom");

        var followUpRan = false;
        executor.Post(() => throw new InvalidOperationException("first"));
        executor.Post(() => followUpRan = true);

        var exception = Record.Exception(() => executor.Tick());

        Assert.Null(exception);
        Assert.True(followUpRan);
    }
}
