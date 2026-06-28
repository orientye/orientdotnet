using System.Collections.Concurrent;
using System.Diagnostics;

namespace Orient.Runtime;

public sealed class OrientLoop
{
    [ThreadStatic]
    private static OrientLoop? current;

#if DEBUG
    [ThreadStatic]
    private static OrientLoop? boundLoopOnThread;
#endif

    public static OrientLoop? Current => current;

    /// <summary>
    /// Returns <paramref name="loop"/> when provided; otherwise <see cref="Current"/>.
    /// Throws if neither is available.
    /// </summary>
    public static OrientLoop RequireCurrentOr(OrientLoop? loop = null)
    {
        return loop ?? Current
            ?? throw new InvalidOperationException(
                "A OrientLoop must be provided explicitly or available via OrientLoop.Current.");
    }

    private readonly ConcurrentQueue<Action> actions = new();
    private readonly IOrientLoopTimerScheduler timerScheduler;
    private readonly ManualResetEventSlim wakeup = new(initialState: false);
    private int threadId;

    public OrientLoop()
        : this(null)
    {
    }

    public OrientLoop(OrientLoopOptions? options)
    {
        timerScheduler = (options ?? new OrientLoopOptions()).CreateTimerScheduler();
    }

    public bool IsInLoopThread => threadId != 0 && Environment.CurrentManagedThreadId == threadId;

    /// <summary>
    /// Raised on the loop thread when an action or timer callback throws.
    /// Exceptions thrown from this handler are caught and written to <see cref="Console.Error"/>
    /// to keep the loop alive.
    /// </summary>
    public event Action<Exception>? UnhandledException;

    public void BindToCurrentThread()
    {
#if DEBUG
        if (boundLoopOnThread is not null && !ReferenceEquals(boundLoopOnThread, this))
        {
            throw new InvalidOperationException(
                "This thread is already bound to a different OrientLoop. Use one business thread per loop.");
        }

        boundLoopOnThread = this;
#endif
        threadId = Environment.CurrentManagedThreadId;
        current = this;
    }

#if DEBUG
    /// <summary>
    /// Clears DEBUG thread binding state. For test harnesses only (e.g. <c>CRpc.TestHelper.CrpcTestBase</c>).
    /// </summary>
    public static void ResetDebugThreadBindingForTests()
    {
        if (boundLoopOnThread is not null
            && boundLoopOnThread.threadId == Environment.CurrentManagedThreadId)
        {
            boundLoopOnThread.threadId = 0;
        }

        boundLoopOnThread = null;
        current = null;
    }
#endif

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        actions.Enqueue(action);
        wakeup.Set();
    }

    internal OrientLoopTimer ScheduleDelay(int millisecondsDelay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (millisecondsDelay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        }

        EnsureLoopThread();
        var dueTimestamp = Stopwatch.GetTimestamp() + MillisecondsToStopwatchTicks(millisecondsDelay);
        return ScheduleAt(dueTimestamp, action);
    }

    internal OrientLoopTimer ScheduleAt(long dueTimestamp, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnsureLoopThread();
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
            EnsureLoopThread();
        }

        DrainActions(maxActions);
        RunExpiredTimers(maxActions);
        DrainActions(maxActions);
    }

    public void WaitForWorkOrTimer(CancellationToken cancellationToken)
    {
        EnsureLoopThread();

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
            Console.Error.WriteLine($"OrientLoop unhandled exception: {exception}");
            return;
        }

        try
        {
            handler(exception);
        }
        catch (Exception handlerException)
        {
            Console.Error.WriteLine($"OrientLoop unhandled exception handler threw: {handlerException}");
            Console.Error.WriteLine($"original exception: {exception}");
        }
    }

    private static long MillisecondsToStopwatchTicks(int millisecondsDelay)
    {
        return millisecondsDelay * Stopwatch.Frequency / 1000;
    }

    internal void EnsureInLoopThread()
    {
        if (!IsInLoopThread)
        {
            throw new InvalidOperationException("OrientLoop operations must run on the loop thread.");
        }
    }

    private void EnsureLoopThread() => EnsureInLoopThread();
}
