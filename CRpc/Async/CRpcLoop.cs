using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using CRpc.Rpc;

namespace CRpc.Async;

public sealed class CRpcLoop
{
    [ThreadStatic]
    private static CRpcLoop? current;

#if DEBUG
    [ThreadStatic]
    private static CRpcLoop? boundLoopOnThread;
#endif

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

    private const int InitialServiceCapacity = 106;

    private readonly ConcurrentQueue<Action> actions = new();
    private readonly ICRpcLoopTimerScheduler timerScheduler;
    private readonly Dictionary<ushort, IRpcService> registeredServices = new(InitialServiceCapacity);
    private readonly ManualResetEventSlim wakeup = new(initialState: false);
    private int threadId;

    public CRpcLoop()
        : this(null)
    {
    }

    public CRpcLoop(CRpcLoopOptions? options)
    {
        timerScheduler = (options ?? new CRpcLoopOptions()).CreateTimerScheduler();
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
                "This thread is already bound to a different CRpcLoop. Use one business thread per loop.");
        }

        boundLoopOnThread = this;
#endif
        threadId = Environment.CurrentManagedThreadId;
        current = this;
    }

#if DEBUG
    /// <summary>
    /// Clears DEBUG thread binding state. For tests only (via <c>CRPC.Tests.CrpcTestBase</c>).
    /// </summary>
    internal static void ResetDebugThreadBindingForTests()
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

    public void RegisterService(IRpcService service)
    {
        EnsureLoopThread();
        ArgumentNullException.ThrowIfNull(service);
        registeredServices[service.GetServiceId()] = service;
    }

    public bool TryGetService(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service)
    {
        EnsureLoopThread();
        return registeredServices.TryGetValue(serviceId, out service);
    }

    public void UnregisterService(IRpcService service)
    {
        EnsureLoopThread();
        ArgumentNullException.ThrowIfNull(service);
        var serviceId = service.GetServiceId();
        if (registeredServices.TryGetValue(serviceId, out var registeredService)
            && ReferenceEquals(registeredService, service))
        {
            registeredServices.Remove(serviceId);
        }
    }

    internal void ClearRegisteredServices()
    {
        EnsureLoopThread();
        registeredServices.Clear();
    }

    internal CRpcLoopTimer ScheduleDelay(int millisecondsDelay, Action action)
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

    internal CRpcLoopTimer ScheduleAt(long dueTimestamp, Action action)
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

    internal void EnsureInLoopThread()
    {
        if (!IsInLoopThread)
        {
            throw new InvalidOperationException("CRpcLoop operations must run on the loop thread.");
        }
    }

    private void EnsureLoopThread() => EnsureInLoopThread();
}
