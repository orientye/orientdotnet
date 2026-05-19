using System.Collections.Concurrent;
using System.Diagnostics;

namespace CRpc.Async;

public sealed class CRpcLoop
{
    [ThreadStatic]
    private static CRpcLoop? current;

    public static CRpcLoop? Current => current;

    /// <summary>
    /// Returns <paramref name="loop"/> when provided; otherwise <see cref="Current"/>.
    /// Throws if neither is available.
    /// </summary>
    public static CRpcLoop RequireCurrentOr(CRpcLoop? loop = null)
    {
        return loop ?? Current
            ?? throw new InvalidOperationException(
                "A CRpcLoop must be provided explicitly or available via CRpcLoop.Current.");
    }

    private readonly ConcurrentQueue<Action> actions = new();
    private readonly PriorityQueue<ScheduledTimer, long> timers = new();
    private int threadId;

    public bool IsInLoopThread => threadId != 0 && Environment.CurrentManagedThreadId == threadId;

    /// <summary>
    /// Raised on the loop thread when an action or timer callback throws.
    /// Exceptions thrown from this handler are caught and written to <see cref="Console.Error"/>
    /// to keep the loop alive.
    /// </summary>
    public event Action<Exception>? UnhandledException;

    public void BindToCurrentThread()
    {
        threadId = Environment.CurrentManagedThreadId;
        current = this;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        actions.Enqueue(action);
    }

    internal CRpcLoopTimer ScheduleDelay(int millisecondsDelay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (millisecondsDelay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        }

        EnsureLoopThread();
        var timer = new CRpcLoopTimer(action);
        timers.Enqueue(
            new ScheduledTimer(timer),
            Stopwatch.GetTimestamp() + MillisecondsToStopwatchTicks(millisecondsDelay));
        return timer;
    }

    public void Tick(int maxActions = 1024)
    {
        if (threadId == 0)
        {
            BindToCurrentThread();
        }
        else
        {
            current = this;
        }

        RunExpiredTimers();

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

    private void RunExpiredTimers()
    {
        var now = Stopwatch.GetTimestamp();
        while (timers.TryPeek(out var scheduledTimer, out var dueTimestamp) && dueTimestamp <= now)
        {
            timers.Dequeue();
            try
            {
                scheduledTimer.Timer.Invoke();
            }
            catch (Exception exception)
            {
                HandleUnhandledException(exception);
            }
        }
    }

    private void HandleUnhandledException(Exception exception)
    {
        var handler = UnhandledException;
        if (handler is null)
        {
            Console.Error.WriteLine($"CRpcLoop unhandled exception: {exception}");
            return;
        }

        try
        {
            handler(exception);
        }
        catch (Exception handlerException)
        {
            Console.Error.WriteLine($"CRpcLoop unhandled exception handler threw: {handlerException}");
            Console.Error.WriteLine($"original exception: {exception}");
        }
    }

    private static long MillisecondsToStopwatchTicks(int millisecondsDelay)
    {
        return millisecondsDelay * Stopwatch.Frequency / 1000;
    }

    private void EnsureLoopThread()
    {
        if (!IsInLoopThread)
        {
            throw new InvalidOperationException("CRpcLoop timer operations must run on the loop thread.");
        }
    }

    private readonly record struct ScheduledTimer(CRpcLoopTimer Timer);
}

internal sealed class CRpcLoopTimer
{
    private readonly Action action;

    public CRpcLoopTimer(Action action)
    {
        this.action = action;
    }

    public bool IsCanceled { get; private set; }

    public void Cancel()
    {
        IsCanceled = true;
    }

    public void Invoke()
    {
        if (!IsCanceled)
        {
            action();
        }
    }
}
