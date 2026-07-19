using System.Collections.Concurrent;
using System.Diagnostics;
using Orient.Logging;

namespace Orient.Runtime;

public sealed partial class OrientExecutor
{
    [ThreadStatic]
    private static OrientExecutor? current;

#if DEBUG
    [ThreadStatic]
    private static OrientExecutor? boundExecutorOnThread;
#endif

    public static OrientExecutor? Current => current;

    /// <summary>
    /// Returns <paramref name="executor"/> when provided; otherwise <see cref="Current"/>.
    /// Throws if neither is available.
    /// </summary>
    public static OrientExecutor RequireCurrentOr(OrientExecutor? executor = null)
    {
        return executor ?? Current
            ?? throw new InvalidOperationException(
                "A OrientExecutor must be provided explicitly or available via OrientExecutor.Current.");
    }

    private readonly ConcurrentQueue<Action> actions = new();
    private readonly IOrientExecutorTimerScheduler timerScheduler;
    private readonly ManualResetEventSlim wakeup = new(initialState: false);
    private int threadId;

    public OrientExecutor()
        : this(null)
    {
    }

    public OrientExecutor(OrientExecutorOptions? options)
    {
        var resolvedOptions = options ?? new OrientExecutorOptions();
        timerScheduler = resolvedOptions.CreateTimerScheduler();
        Logger = resolvedOptions.CreateLogger("Orient.Runtime.OrientExecutor");
    }

    public bool IsInExecutorThread => threadId != 0 && Environment.CurrentManagedThreadId == threadId;

    internal IOrientLogger Logger { get; }

    /// <summary>
    /// Raised on the executor thread when an action or timer callback throws.
    /// Exceptions thrown from this handler are caught and logged, with
    /// <see cref="Console.Error"/> as a last resort, to keep the executor alive.
    /// </summary>
    public event Action<Exception>? UnhandledException;

    public void BindToCurrentThread()
    {
#if DEBUG
        if (boundExecutorOnThread is not null && !ReferenceEquals(boundExecutorOnThread, this))
        {
            throw new InvalidOperationException(
                "This thread is already bound to a different OrientExecutor. Use one business thread per executor.");
        }

        boundExecutorOnThread = this;
#endif
        threadId = Environment.CurrentManagedThreadId;
        current = this;
    }

#if DEBUG
    /// <summary>
    /// Clears DEBUG thread binding state. For test harnesses only (e.g. <c>Orient.TestHelper.OrientTestBase</c>).
    /// </summary>
    public static void ResetDebugThreadBindingForTests()
    {
        if (boundExecutorOnThread is not null
            && boundExecutorOnThread.threadId == Environment.CurrentManagedThreadId)
        {
            boundExecutorOnThread.threadId = 0;
        }

        boundExecutorOnThread = null;
        current = null;
    }
#endif

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        actions.Enqueue(action);
        wakeup.Set();
    }

    internal OrientExecutorTimer ScheduleDelay(int millisecondsDelay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (millisecondsDelay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        }

        EnsureExecutorThread();
        var dueTimestamp = Stopwatch.GetTimestamp() + MillisecondsToStopwatchTicks(millisecondsDelay);
        return ScheduleAt(dueTimestamp, action);
    }

    internal OrientExecutorTimer ScheduleAt(long dueTimestamp, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureExecutorThread();
        return timerScheduler.ScheduleAt(dueTimestamp, action);
    }

    public void Tick(int maxActions = 1024)
    {
        if (threadId == 0)
        {
            BindToCurrentThread();
        }
        else
        {
            EnsureExecutorThread();
        }

        DrainActions(maxActions);
        RunExpiredTimers(maxActions);
        DrainActions(maxActions);
    }

    public void WaitForWorkOrTimer(CancellationToken cancellationToken)
    {
        EnsureExecutorThread();

        // Reset clears a prior Set so Wait can block; re-check queue/timer after Reset.
        wakeup.Reset();

        if (!actions.IsEmpty || HasDueTimers())
        {
            return;
        }

        var delay = timerScheduler.GetDelayUntilNextWakeup(Stopwatch.GetTimestamp());
        if (delay == TimeSpan.Zero)
        {
            return;
        }

        if (delay is null)
        {
            wakeup.Wait(cancellationToken);
        }
        else
        {
            wakeup.Wait(delay.Value, cancellationToken);
        }
    }

    private void DrainActions(int maxActions)
    {
        for (var i = 0; i < maxActions && actions.TryDequeue(out var action); i++)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                HandleUnhandledException(exception);
            }
        }
    }

    private void RunExpiredTimers(int maxTimers)
    {
        var now = Stopwatch.GetTimestamp();
        for (var i = 0; i < maxTimers; i++)
        {
            if (timerScheduler.GetDelayUntilNextWakeup(now) != TimeSpan.Zero)
            {
                break;
            }

            try
            {
                if (timerScheduler.RunDueTimers(now, maxTimers: 1) == 0)
                {
                    break;
                }
            }
            catch (Exception exception)
            {
                HandleUnhandledException(exception);
            }
        }
    }

    private bool HasDueTimers()
    {
        return timerScheduler.GetDelayUntilNextWakeup(Stopwatch.GetTimestamp()) == TimeSpan.Zero;
    }

    private void HandleUnhandledException(Exception exception)
    {
        var handler = UnhandledException;
        if (handler is null)
        {
            LogErrorOrWriteToStderr("OrientExecutor unhandled exception", exception);
            return;
        }

        try
        {
            handler(exception);
        }
        catch (Exception handlerException)
        {
            LogErrorOrWriteToStderr(
                "OrientExecutor unhandled exception handler threw",
                handlerException);
            LogErrorOrWriteToStderr("original exception", exception);
        }
    }

    private void LogErrorOrWriteToStderr(string message, Exception exception)
    {
        if (Logger.IsEnabled(OrientLogLevel.Error))
        {
            Logger.Log(OrientLogLevel.Error, 1001, message, exception);
            return;
        }

        Console.Error.WriteLine($"{message}: {exception}");
    }

    private static long MillisecondsToStopwatchTicks(int millisecondsDelay)
    {
        return millisecondsDelay * Stopwatch.Frequency / 1000;
    }

    internal void EnsureInExecutorThread()
    {
        if (!IsInExecutorThread)
        {
            throw new InvalidOperationException("OrientExecutor operations must run on the executor thread.");
        }
    }

    private void EnsureExecutorThread() => EnsureInExecutorThread();
}
