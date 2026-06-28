# CRpcLoop Wakeup + Loop-Owned Timer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `Tick + Sleep(1)` busy-wait with loop-owned wakeup and min-heap timer scheduling; adjust `Tick` to actions → due timers → actions; keep RPC timeout on owner loop with documented response/timeout race semantics.

**Architecture:** Introduce `ICRpcLoopTimerScheduler` with default `MinHeapTimerScheduler` (absolute `Stopwatch` deadlines). `CRpcLoop` owns `ManualResetEventSlim`, calls `Set()` on `Post`, and exposes `WaitForWorkOrTimer(ct)` that consults only `GetDelayUntilNextWakeup(now)`. Drivers (`CRpcServerLoop`, `CRpcLoopRunner`, `CRpcLoopHost`) become thin `Tick + WaitForWorkOrTimer` loops and drop `sleepMilliseconds` from the hot path.

**Tech Stack:** C# / .NET 8, xUnit, custom `CRpcTask`, existing `CRpcLoop` / `CRpcClient`.

**Spec reference:** `Doc/architecture.md` §9.5, §10 step 1.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `CRpc/Async/ICRpcLoopTimerScheduler.cs` | Internal scheduler contract: schedule, run due, next wakeup delay |
| `CRpc/Async/MinHeapTimerScheduler.cs` | Default `PriorityQueue` backend |
| `CRpc/Async/CRpcLoopOptions.cs` | Optional `TimerSchedulerFactory`; default min-heap |
| `CRpc/Async/CRpcLoop.cs` | Wakeup, `WaitForWorkOrTimer`, new `Tick` order, delegate timers |
| `CRpc/Async/CRpcLoopRunner.cs` | `Tick + WaitForWorkOrTimer`; remove `sleepMilliseconds` |
| `CRpc/Rpc/CRpc/Server/CRpcServerLoop.cs` | Same driver change |
| `CRpc/Rpc/CRpc/Server/CRpcLoopHost.cs` | Forward without `sleepMilliseconds` |
| `Tests/CRPC.Tests/MinHeapTimerSchedulerTests.cs` | Scheduler unit tests |
| `Tests/CRPC.Tests/CRpcLoopWakeupTests.cs` | Post wakeup + wait behavior |
| `Tests/CRPC.Tests/CRpcLoopTickOrderTests.cs` | actions-before-timers ordering |
| `Tests/CRPC.Tests/CRpcClientTests.cs` | RPC response vs timeout race |
| `Tests/CRPC.Tests/CRpcLoopRunnerTests.cs` | Remove `sleepMilliseconds` usage |
| `Tests/CRPC.Tests/UnitTest1.cs` | Update `CRpcLoopTests` / server loop test |

---

### Task 1: Timer Scheduler Interface + Min-Heap Backend

**Files:**
- Create: `CRpc/Async/ICRpcLoopTimerScheduler.cs`
- Create: `CRpc/Async/MinHeapTimerScheduler.cs`
- Test: `Tests/CRPC.Tests/MinHeapTimerSchedulerTests.cs`

- [ ] **Step 1: Write failing scheduler tests**

Create `Tests/CRPC.Tests/MinHeapTimerSchedulerTests.cs`:

```csharp
using System.Diagnostics;
using CRpc.Async;

namespace CRPC.Tests;

public class MinHeapTimerSchedulerTests : CrpcTestBase
{
    [Fact]
    public void GetDelayUntilNextWakeupReturnsNullWhenEmpty()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();

        Assert.Null(scheduler.GetDelayUntilNextWakeup(now));
    }

    [Fact]
    public void GetDelayUntilNextWakeupReturnsZeroWhenDue()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        scheduler.ScheduleAt(now, () => { });

        Assert.Equal(TimeSpan.Zero, scheduler.GetDelayUntilNextWakeup(now));
    }

    [Fact]
    public void RunDueTimersInvokesCallbackWhenDue()
    {
        var scheduler = new MinHeapTimerScheduler();
        var ran = false;
        var now = Stopwatch.GetTimestamp();
        scheduler.ScheduleAt(now, () => ran = true);

        var count = scheduler.RunDueTimers(now, maxTimers: 8);

        Assert.Equal(1, count);
        Assert.True(ran);
    }

    [Fact]
    public void RunDueTimersSkipsCanceledTimer()
    {
        var scheduler = new MinHeapTimerScheduler();
        var ran = false;
        var now = Stopwatch.GetTimestamp();
        var timer = scheduler.ScheduleAt(now, () => ran = true);
        timer.Cancel();

        scheduler.RunDueTimers(now, maxTimers: 8);

        Assert.False(ran);
    }

    [Fact]
    public void GetDelayUntilNextWakeupReturnsPositiveDelayForFutureTimer()
    {
        var scheduler = new MinHeapTimerScheduler();
        var now = Stopwatch.GetTimestamp();
        var future = now + Stopwatch.Frequency / 10; // ~100ms
        scheduler.ScheduleAt(future, () => { });

        var delay = scheduler.GetDelayUntilNextWakeup(now);

        Assert.NotNull(delay);
        Assert.True(delay!.Value.TotalMilliseconds > 50);
    }
}
```

- [ ] **Step 2: Run tests and verify they fail to compile**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~MinHeapTimerSchedulerTests" --no-restore
```

Expected: build fails — `MinHeapTimerScheduler` / `ICRpcLoopTimerScheduler` not found.

- [ ] **Step 3: Implement interface and min-heap scheduler**

Create `CRpc/Async/ICRpcLoopTimerScheduler.cs`:

```csharp
namespace CRpc.Async;

internal interface ICRpcLoopTimerScheduler
{
    CRpcLoopTimer ScheduleAt(long dueTimestamp, Action callback);
    int RunDueTimers(long now, int maxTimers);
    TimeSpan? GetDelayUntilNextWakeup(long now);
}
```

Create `CRpc/Async/MinHeapTimerScheduler.cs`:

```csharp
using System.Diagnostics;

namespace CRpc.Async;

internal sealed class MinHeapTimerScheduler : ICRpcLoopTimerScheduler
{
    private readonly PriorityQueue<ScheduledTimer, long> timers = new();

    public CRpcLoopTimer ScheduleAt(long dueTimestamp, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new CRpcLoopTimer(callback);
        timers.Enqueue(new ScheduledTimer(timer), dueTimestamp);
        return timer;
    }

    public int RunDueTimers(long now, int maxTimers)
    {
        var ran = 0;
        while (ran < maxTimers
               && timers.TryPeek(out var scheduledTimer, out var dueTimestamp)
               && dueTimestamp <= now)
        {
            timers.Dequeue();
            ran++;
            scheduledTimer.Timer.Invoke();
        }

        return ran;
    }

    public TimeSpan? GetDelayUntilNextWakeup(long now)
    {
        if (!timers.TryPeek(out _, out var dueTimestamp))
        {
            return null;
        }

        if (dueTimestamp <= now)
        {
            return TimeSpan.Zero;
        }

        var ticks = dueTimestamp - now;
        var milliseconds = ticks * 1000.0 / Stopwatch.Frequency;
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private readonly record struct ScheduledTimer(CRpcLoopTimer Timer);
}
```

Move `CRpcLoopTimer` to `CRpc/Async/CRpcLoopTimer.cs` (same namespace, `internal sealed`) so both `CRpcLoop` and scheduler can use it. Timer callback exceptions propagate to `CRpcLoop.RunExpiredTimers`, which wraps `RunDueTimers` in try/catch per the existing `HandleUnhandledException` pattern.

- [ ] **Step 4: Run scheduler tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~MinHeapTimerSchedulerTests" --no-restore
```

Expected: PASS (5 tests).

---

### Task 2: CRpcLoopOptions + Wire Scheduler into CRpcLoop

**Files:**
- Create: `CRpc/Async/CRpcLoopOptions.cs`
- Create: `CRpc/Async/CRpcLoopTimer.cs` (extract from `CRpcLoop.cs`)
- Modify: `CRpc/Async/CRpcLoop.cs`

- [ ] **Step 1: Add options type**

Create `CRpc/Async/CRpcLoopOptions.cs`:

```csharp
namespace CRpc.Async;

public sealed class CRpcLoopOptions
{
    public Func<ICRpcLoopTimerScheduler>? TimerSchedulerFactory { get; init; }

    internal ICRpcLoopTimerScheduler CreateTimerScheduler()
    {
        return TimerSchedulerFactory?.Invoke() ?? new MinHeapTimerScheduler();
    }
}
```

- [ ] **Step 2: Refactor CRpcLoop constructor and timer delegation**

Modify `CRpcLoop`:

- Add `public CRpcLoop()` and `public CRpcLoop(CRpcLoopOptions? options)`.
- Replace `private readonly PriorityQueue<...> timers` with `private readonly ICRpcLoopTimerScheduler timerScheduler`.
- `ScheduleDelay` computes `due = Stopwatch.GetTimestamp() + MillisecondsToStopwatchTicks(ms)` and calls `timerScheduler.ScheduleAt(due, action)`.
- Add internal `ScheduleAt(long dueTimestamp, Action action)` for future deadline use.
- Move timer invoke exception handling into `CRpcLoop` (wrap `timerScheduler.RunDueTimers` in try/catch per timer — mirror current `RunExpiredTimers`).

- [ ] **Step 3: Build and run existing loop tests (baseline)**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopExceptionIsolationTests|FullyQualifiedName~CRpcLoopRegistryTests" --no-restore
```

Expected: PASS — behavior unchanged except internal structure.

---

### Task 3: New Tick Order (actions → timers → actions)

**Files:**
- Modify: `CRpc/Async/CRpcLoop.cs`
- Test: `Tests/CRPC.Tests/CRpcLoopTickOrderTests.cs`

- [ ] **Step 1: Write failing tick-order test**

Create `Tests/CRPC.Tests/CRpcLoopTickOrderTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopTickOrderTests" --no-restore
```

Expected: FAIL — current `Tick` runs timers before actions (`TickRunsPostedActionsBeforeDueTimers`).

- [ ] **Step 3: Change Tick implementation**

Replace `Tick` body with:

```csharp
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

    DrainActions(maxActions);
    RunExpiredTimers(maxTimers: maxActions);
    DrainActions(maxActions);
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
```

Call `RunDueTimers(..., maxTimers: 1)` per iteration so each timer callback gets the same isolated exception handling as today's `RunExpiredTimers`.

- [ ] **Step 4: Run tick-order and exception-isolation tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopTickOrderTests|FullyQualifiedName~CRpcLoopExceptionIsolationTests" --no-restore
```

Expected: PASS.

---

### Task 4: Wakeup + WaitForWorkOrTimer

**Files:**
- Modify: `CRpc/Async/CRpcLoop.cs`
- Test: `Tests/CRPC.Tests/CRpcLoopWakeupTests.cs`

- [ ] **Step 1: Write failing wakeup tests**

Create `Tests/CRPC.Tests/CRpcLoopWakeupTests.cs`:

```csharp
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
            loop.ScheduleDelay(30, () => ran = true);
            loop.WaitForWorkOrTimer(CancellationToken.None);
            loop.Tick();
        })
        {
            IsBackground = true,
        };
        driver.Start();
        driver.Join(TimeSpan.FromSeconds(2));

        Assert.True(ran);
    }
}
```

`WaitForWorkOrTimer` is **driver-thread only** (same thread as `BindToCurrentThread`); cross-thread callers use `Post` to wake the driver.

- [ ] **Step 2: Run tests and verify failure**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopWakeupTests" --no-restore
```

Expected: FAIL — `WaitForWorkOrTimer` missing.

- [ ] **Step 3: Implement wakeup on CRpcLoop**

Add to `CRpcLoop`:

```csharp
private readonly object wakeupGate = new();
private readonly ManualResetEventSlim wakeup = new(initialState: false);

public void Post(Action action)
{
    ArgumentNullException.ThrowIfNull(action);
    lock (wakeupGate)
    {
        actions.Enqueue(action);
        wakeup.Set();
    }
}

public void WaitForWorkOrTimer(CancellationToken cancellationToken)
{
    lock (wakeupGate)
    {
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
            Monitor.Exit(wakeupGate);
            try
            {
                wakeup.Wait(cancellationToken);
            }
            finally
            {
                Monitor.Enter(wakeupGate);
            }
            return;
        }

        Monitor.Exit(wakeupGate);
        try
        {
            wakeup.Wait(delay.Value, cancellationToken);
        }
        finally
        {
            Monitor.Enter(wakeupGate);
        }
    }
}

private bool HasDueTimers()
{
    return timerScheduler.GetDelayUntilNextWakeup(Stopwatch.GetTimestamp()) == TimeSpan.Zero;
}
```

**Worker note:** Prefer a helper that waits outside the lock without manual `Monitor.Exit/Enter` if you extract `PrepareWait()` / `CompleteWait()` — but behavior must match: `Reset → check mailbox + due timers → Wait`.

- [ ] **Step 4: Run wakeup tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopWakeupTests" --no-restore
```

Expected: PASS.

---

### Task 5: Update Drivers — Remove Sleep(1)

**Files:**
- Modify: `CRpc/Rpc/CRpc/Server/CRpcServerLoop.cs`
- Modify: `CRpc/Rpc/CRpc/Server/CRpcLoopHost.cs`
- Modify: `CRpc/Async/CRpcLoopRunner.cs`
- Modify: `Tests/CRPC.Tests/CRpcLoopRunnerTests.cs`
- Modify: `Tests/CRPC.Tests/UnitTest1.cs`

- [ ] **Step 1: Change CRpcServerLoop**

Replace body with:

```csharp
public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(loop);
    loop.BindToCurrentThread();

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            loop.Tick();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CRpcServerLoop: unexpected exception escaped Tick: {exception}");
        }

        loop.WaitForWorkOrTimer(cancellationToken);
    }
}
```

Mark old overload obsolete if kept temporarily:

```csharp
[Obsolete("Use RunUntilCancelled(loop, cancellationToken). sleepMilliseconds is no longer used.")]
public static void RunUntilCancelled(CRpcLoop loop, CancellationToken cancellationToken, int sleepMilliseconds)
    => RunUntilCancelled(loop, cancellationToken);
```

- [ ] **Step 2: Change CRpcLoopRunner**

Replace inner `while (!completed)` with:

```csharp
while (!completed)
{
    try
    {
        loop.Tick();
    }
    catch (Exception tickException)
    {
        Console.Error.WriteLine($"CRpcLoopRunner: unexpected exception escaped Tick: {tickException}");
    }

    if (!completed)
    {
        loop.WaitForWorkOrTimer(CancellationToken.None);
    }
}
```

Remove `sleepMilliseconds` parameter from both overloads (or obsolete forwarding overload).

- [ ] **Step 3: Update tests — remove `sleepMilliseconds: 0`**

In `CRpcLoopRunnerTests.cs`, drop the third argument from all `RunUntilComplete` calls.

In `UnitTest1.cs` `ServerLoopPumpsUntilCancellationWithoutReadingConsoleKeys`, change to:

```csharp
CRpcServerLoop.RunUntilCancelled(loop, cts.Token);
```

- [ ] **Step 4: Run driver-related tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcLoopRunnerTests|FullyQualifiedName~CRpcLoopTests.ServerLoopPumpsUntilCancellation" --no-restore
```

Expected: PASS.

---

### Task 6: RPC Timeout Response vs Timeout Race

**Files:**
- Modify: `Tests/CRPC.Tests/CRpcClientTests.cs`
- Verify: `CRpc/Rpc/CRpc/Client/CRpcClient.cs` (no change expected if Tick order fix is sufficient)

- [ ] **Step 1: Add response-wins test**

Add to `CRpcClientTests.cs`:

```csharp
[Fact]
public void CallAsyncResponsePostedBeforeDueTimeoutCompletesWithResult()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();

    var client = new CRpcClient(loop);
    var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
    var awaiter = task.GetAwaiter();
    CRpcMessage? result = null;
    awaiter.OnCompleted(() => result = awaiter.GetResult());

    Thread.Sleep(20); // ensure timeout is due

    var response = CreateResponse(reqSequence: 1);
    client.OnReceiveResponse(response); // Post to mailbox, do not Tick yet

    loop.Tick();

    Assert.Same(response, result);
    Assert.True(awaiter.IsCompleted);
}

[Fact]
public void CallAsyncLateResponseAfterTimeoutIsIgnored()
{
    var loop = new CRpcLoop();
    loop.BindToCurrentThread();

    var client = new CRpcClient(loop);
    var task = client.CallAsync(7, 8, Array.Empty<byte>(), timeout: 1);
    var awaiter = task.GetAwaiter();

    Thread.Sleep(20);
    loop.Tick();

    Assert.Throws<TimeoutException>(() => awaiter.GetResult());

    var lateResponse = CreateResponse(reqSequence: 1);
    client.OnReceiveResponse(lateResponse);
    loop.Tick();

    // Still timeout — late response must not resurrect the call
    Assert.Throws<TimeoutException>(() => awaiter.GetResult());
}
```

- [ ] **Step 2: Run client tests**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~CRpcClientTests" --no-restore
```

Expected: PASS (including new race tests).

---

### Task 7: Full Test Suite + Doc Touch-Up

**Files:**
- Modify: `Doc/architecture.md` (only if API signatures in §9.2 drift from implementation)

- [ ] **Step 1: Run full test project**

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --no-restore
```

Expected: all tests PASS.

- [ ] **Step 2: Align §9.2 API snippet with final signatures**

If `sleepMilliseconds` overloads were removed (not just obsoleted), ensure `Doc/architecture.md` §9.2 / §9.5.7 matches shipped API.

---

## Spec Coverage Checklist

| Spec (§9.5) | Task |
| --- | --- |
| Loop-owned timer | Task 1–2 |
| Absolute deadline / `ScheduleDelay` → `ScheduleAt` | Task 2 |
| Remove `Sleep(1)` | Task 5 |
| `ManualResetEventSlim` + anti lost-wakeup | Task 4 |
| `GetDelayUntilNextWakeup` only on scheduler interface | Task 1 |
| Tick: actions → timers → actions | Task 3 |
| RPC timeout on owner loop | Task 2 (existing client path) |
| Response before timeout wins | Task 6 |
| Late response after timeout ignored | Task 6 |
| Driver `Tick + WaitForWorkOrTimer` | Task 5 |
| `CRpcLoopOptions` factory hook | Task 2 |
| Timing wheel reserved (not implemented) | Task 1 interface only |

---

## Out of Scope (follow §10 steps 2+)

- `TimingWheelTimerScheduler` implementation
- Registry migration to loop
- `CRpcServerOptions` / IO thread configuration
- Replacing `Console.WriteLine` logging

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-22-crpcloop-wakeup-timer-plan.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
