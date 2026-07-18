# OrientLoop.InvokeAsync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the four `OrientLoop.InvokeAsync` overloads in `Orient.Runtime` so same-process cross-loop business calls schedule on the target loop and complete on the caller loop as `OrientTask`.

**Architecture:** Add static methods on `OrientLoop` that create a caller-owned `OrientTaskCompletionSource`, post an `async OrientTask` runner to the target loop (or run inline when `callerLoop == targetLoop`), and complete the caller task only via `callerLoop.Post`. Target runners catch all failures locally and never rely on `DrainActions` exception isolation. Tests use a caller `LoopTestDriver` plus a background target-loop pump thread.

**Tech Stack:** C# / .NET 8, `Orient.Runtime`, xUnit, existing `Orient.TestHelper.LoopTestDriver`, `Orient.TestHelper.OrientTestBase`

**Spec:** `docs/superpowers/specs/2026-07-05-orientloop-invokeasync-design.md`

**Note on commits:** Do not commit unless the user explicitly asks. This plan omits commit steps.

---

## File Map

| File | Action | Responsibility |
| --- | --- | --- |
| `Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs` | Create | `partial` implementation of four public overloads + private helpers |
| `Orient.Runtime/Loop/OrientLoop.cs` | Modify | Declare `public sealed partial class OrientLoop` (change `class` → `partial class`) |
| `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs` | Create | Cross-loop / same-loop / error propagation tests + local dual-loop harness |
| `Doc/architecture.md` | Modify | Mark §5.2 runtime primitive as implemented |
| `Doc/TODO.txt` | Modify | Remove completed `InvokeAsync` item; keep `LoopRoute` pending |

---

### Task 1: Test Harness And Invalid Call-Site Tests

**Files:**
- Create: `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs`
- Modify: `Orient.Runtime/Loop/OrientLoop.cs` (make class `partial` only)

- [ ] **Step 1: Make `OrientLoop` partial**

In `Orient.Runtime/Loop/OrientLoop.cs`, change:

```csharp
public sealed class OrientLoop
```

to:

```csharp
public sealed partial class OrientLoop
```

- [ ] **Step 2: Write failing invalid call-site tests**

Create `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs`:

```csharp
using Orient.Runtime;

namespace Orient.Tests;

public class OrientLoopInvokeAsyncTests : OrientTestBase
{
    [Fact]
    public void InvokeAsyncWithoutCurrentLoopThrowsRequireCurrentOrMessage()
    {
        var targetLoop = new OrientLoop();
        var exception = RunOnFreshThread(() =>
            OrientLoop.InvokeAsync(targetLoop, () => 1));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("OrientLoop must be provided", exception!.Message);
    }

    [Fact]
    public void InvokeAsyncNullTargetLoopThrows()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        Assert.Throws<ArgumentNullException>(() =>
            OrientLoop.InvokeAsync(null!, () => 1));
    }

    [Fact]
    public void InvokeAsyncNullActionThrows()
    {
        var loop = new OrientLoop();
        loop.BindToCurrentThread();

        Assert.Throws<ArgumentNullException>(() =>
            OrientLoop.InvokeAsync(loop, (Func<int>)null!));
    }

    private static Exception? RunOnFreshThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        thread.Start();
        thread.Join();
        return captured;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```bash
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter "FullyQualifiedName~OrientLoopInvokeAsyncTests" -v minimal
```

Expected: FAIL — `OrientLoop` does not contain a definition for `InvokeAsync`.

- [ ] **Step 4: Add shared validation to all overloads**

Create `Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs` with `RequireCallerLoop()` and parameter validation on every overload. Only the sync-value overload proceeds past validation in this task; the other three overloads throw `NotImplementedException` after validation until their tasks land.

```csharp
namespace Orient.Runtime;

public sealed partial class OrientLoop
{
    public static OrientTask<T> InvokeAsync<T>(OrientLoop targetLoop, Func<OrientTask<T>> action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerLoop();
        throw new NotImplementedException();
    }

    public static OrientTask InvokeAsync(OrientLoop targetLoop, Func<OrientTask> action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerLoop();
        throw new NotImplementedException();
    }

    public static OrientTask<T> InvokeAsync<T>(OrientLoop targetLoop, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerLoop();
        throw new NotImplementedException();
    }

    public static OrientTask InvokeAsync(OrientLoop targetLoop, Action action)
    {
        ArgumentNullException.ThrowIfNull(targetLoop);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerLoop();
        throw new NotImplementedException();
    }

    private static OrientLoop RequireCallerLoop()
    {
        var callerLoop = Current
            ?? throw new InvalidOperationException(
                "A OrientLoop must be provided explicitly or available via OrientLoop.Current.");

        callerLoop.EnsureInLoopThread();
        return callerLoop;
    }
}
```

Invalid call-site tests use the sync-value overload. `EnsureInLoopThread()` remains internal defense only; there is no separate black-box test for it because `OrientLoop.Current` is `[ThreadStatic]`.

- [ ] **Step 5: Re-run invalid call-site tests**

Run the same `dotnet test` filter.

Expected: PASS for the three invalid call-site tests; other tests not added yet.

---

### Task 2: Cross-Loop Synchronous Value Overload

**Files:**
- Modify: `Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs`

- [ ] **Step 1: Add dual-loop pump helper to test file**

Append to `OrientLoopInvokeAsyncTests.cs`:

```csharp
private sealed class TargetLoopPump : IDisposable
{
    private readonly OrientLoop targetLoop;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Thread thread;
    private volatile Exception? pumpFailure;

    public TargetLoopPump(OrientLoop targetLoop)
    {
        this.targetLoop = targetLoop;
        thread = new Thread(PumpMain)
        {
            IsBackground = true,
            Name = "InvokeAsync target pump",
        };
        thread.Start();
    }

    public void EnsureNoFailure()
    {
        if (pumpFailure is not null)
        {
            throw new InvalidOperationException("Target loop pump failed.", pumpFailure);
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            throw new InvalidOperationException("Target loop pump thread did not exit.");
        }

        EnsureNoFailure();
    }

    private void PumpMain()
    {
        targetLoop.BindToCurrentThread();
        try
        {
            while (!cancellation.Token.IsCancellationRequested)
            {
                targetLoop.Tick();
                Thread.Sleep(1);
            }
        }
        catch (Exception exception)
        {
            pumpFailure = exception;
        }
        finally
        {
#if DEBUG
            OrientLoop.ResetDebugThreadBindingForTests();
#endif
        }
    }
}

private static void PumpCallerUntil(OrientLoop callerLoop, Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition() && DateTime.UtcNow < deadline)
    {
        callerLoop.Tick();
        Thread.Sleep(1);
    }

    if (!condition())
    {
        throw new TimeoutException("Caller loop pump timed out.");
    }
}
```

- [ ] **Step 2: Write failing cross-loop sync value test**

Add:

```csharp
[Fact]
public void InvokeAsyncSyncValueRunsOnTargetLoopAndCompletesOnCallerLoop()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    int? actionThreadId = null;
    int? continuationThreadId = null;
    int? result = null;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientLoop.InvokeAsync(
            targetLoop,
            () =>
            {
                actionThreadId = Environment.CurrentManagedThreadId;
                return 42;
            });

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            continuationThreadId = Environment.CurrentManagedThreadId;
            result = awaiter.GetResult();
        });

        PumpCallerUntil(callerLoop, () => result is not null, TimeSpan.FromSeconds(2));

        Assert.Equal(callerThreadId, continuationThreadId);
        Assert.NotEqual(callerThreadId, actionThreadId);
    });

    targetPump.EnsureNoFailure();
    Assert.Equal(42, result);
}
```

- [ ] **Step 3: Run test to verify it fails**

Run:

```bash
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter "FullyQualifiedName~InvokeAsyncSyncValueRunsOnTargetLoopAndCompletesOnCallerLoop" -v minimal
```

Expected: FAIL (`NotImplementedException` or no completion).

- [ ] **Step 4: Implement sync-value cross-loop path**

In `OrientLoop.InvokeAsync.cs`, implement helpers and sync-value overload:

```csharp
public static OrientTask<T> InvokeAsync<T>(OrientLoop targetLoop, Func<T> action)
{
    ArgumentNullException.ThrowIfNull(targetLoop);
    ArgumentNullException.ThrowIfNull(action);

    var callerLoop = RequireCallerLoop();
    var source = new OrientTaskCompletionSource<T>(callerLoop);

    if (ReferenceEquals(callerLoop, targetLoop))
    {
        RunSyncOnSameLoop(action, source);
        return source.Task;
    }

    targetLoop.Post(() => RunSyncOnTargetLoop(action, source, callerLoop));
    return source.Task;
}

private static void RunSyncOnSameLoop<T>(Func<T> action, OrientTaskCompletionSource<T> source)
{
    try
    {
        source.TrySetResult(action());
    }
    catch (Exception exception)
    {
        source.TrySetException(exception);
    }
}

private static void RunSyncOnTargetLoop<T>(
    Func<T> action,
    OrientTaskCompletionSource<T> source,
    OrientLoop callerLoop)
{
    try
    {
        var result = action();
        callerLoop.Post(() => source.TrySetResult(result));
    }
    catch (Exception exception)
    {
        callerLoop.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 5: Run sync-value test**

Expected: PASS.

---

### Task 3: Cross-Loop Synchronous Void Overload

**Files:**
- Modify: `Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing sync void test**

```csharp
[Fact]
public void InvokeAsyncSyncVoidRunsOnTargetLoopAndCompletesOnCallerLoop()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    int? actionThreadId = null;
    var completed = false;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientLoop.InvokeAsync(
            targetLoop,
            () => actionThreadId = Environment.CurrentManagedThreadId);

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            awaiter.GetResult();
            completed = true;
        });

        PumpCallerUntil(callerLoop, () => completed, TimeSpan.FromSeconds(2));
        Assert.NotEqual(callerThreadId, actionThreadId);
    });

    Assert.True(completed);
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL (`NotImplementedException`).

- [ ] **Step 3: Implement sync void overload**

```csharp
public static OrientTask InvokeAsync(OrientLoop targetLoop, Action action)
{
    ArgumentNullException.ThrowIfNull(targetLoop);
    ArgumentNullException.ThrowIfNull(action);

    var callerLoop = RequireCallerLoop();
    var source = new OrientTaskCompletionSource<OrientUnit>(callerLoop);

    if (ReferenceEquals(callerLoop, targetLoop))
    {
        RunVoidOnSameLoop(action, source);
        return new OrientTask(source.Task);
    }

    targetLoop.Post(() => RunVoidOnTargetLoop(action, source, callerLoop));
    return new OrientTask(source.Task);
}

private static void RunVoidOnSameLoop(Action action, OrientTaskCompletionSource<OrientUnit> source)
{
    try
    {
        action();
        source.TrySetResult(OrientUnit.Value);
    }
    catch (Exception exception)
    {
        source.TrySetException(exception);
    }
}

private static void RunVoidOnTargetLoop(
    Action action,
    OrientTaskCompletionSource<OrientUnit> source,
    OrientLoop callerLoop)
{
    try
    {
        action();
        callerLoop.Post(() => source.TrySetResult(OrientUnit.Value));
    }
    catch (Exception exception)
    {
        callerLoop.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 4: Run sync void test**

Expected: PASS.

---

### Task 4: Cross-Loop Asynchronous Value Overload

**Files:**
- Modify: `Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing async value test**

```csharp
[Fact]
public void InvokeAsyncAsyncValueRunsOnTargetLoopAndResumesOnCallerLoop()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    int? actionThreadId = null;
    int? continuationThreadId = null;
    int? result = null;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientLoop.InvokeAsync(
            targetLoop,
            async () =>
            {
                actionThreadId = Environment.CurrentManagedThreadId;
                await OrientTask.Delay(1, targetLoop);
                return 7;
            });

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            continuationThreadId = Environment.CurrentManagedThreadId;
            result = awaiter.GetResult();
        });

        PumpCallerUntil(callerLoop, () => result is not null, TimeSpan.FromSeconds(2));
        Assert.Equal(callerThreadId, continuationThreadId);
        Assert.NotEqual(callerThreadId, actionThreadId);
    });

    Assert.Equal(7, result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL.

- [ ] **Step 3: Implement async-value overload + shared async runner**

Important: start cross-loop async runners with `targetLoop.Post(() => RunAsyncOnTargetLoop(...))`. Do **not** use `targetLoop.Post(async () => ...)` because `Post` takes `Action`.

```csharp
public static OrientTask<T> InvokeAsync<T>(OrientLoop targetLoop, Func<OrientTask<T>> action)
{
    ArgumentNullException.ThrowIfNull(targetLoop);
    ArgumentNullException.ThrowIfNull(action);

    var callerLoop = RequireCallerLoop();
    var source = new OrientTaskCompletionSource<T>(callerLoop);

    if (ReferenceEquals(callerLoop, targetLoop))
    {
        RunAsyncOnSameLoop(action, source);
        return source.Task;
    }

    targetLoop.Post(() => RunAsyncOnTargetLoop(action, source, callerLoop));
    return source.Task;
}

private static async OrientTask RunAsyncOnSameLoop<T>(
    Func<OrientTask<T>> action,
    OrientTaskCompletionSource<T> source)
{
    try
    {
        source.TrySetResult(await action());
    }
    catch (TaskCanceledException)
    {
        source.TrySetCanceled();
    }
    catch (Exception exception)
    {
        source.TrySetException(exception);
    }
}

private static async OrientTask RunAsyncOnTargetLoop<T>(
    Func<OrientTask<T>> action,
    OrientTaskCompletionSource<T> source,
    OrientLoop callerLoop)
{
    try
    {
        var result = await action();
        callerLoop.Post(() => source.TrySetResult(result));
    }
    catch (TaskCanceledException)
    {
        callerLoop.Post(() => source.TrySetCanceled());
    }
    catch (Exception exception)
    {
        callerLoop.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 4: Run async value test**

Expected: PASS.

---

### Task 5: Cross-Loop Asynchronous Void Overload

**Files:**
- Modify: `Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing async void test**

```csharp
[Fact]
public void InvokeAsyncAsyncVoidRunsOnTargetLoopAndCompletesOnCallerLoop()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    int? actionThreadId = null;
    var completed = false;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientLoop.InvokeAsync(
            targetLoop,
            async () =>
            {
                actionThreadId = Environment.CurrentManagedThreadId;
                await OrientTask.Delay(1, targetLoop);
            });

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            awaiter.GetResult();
            completed = true;
        });

        PumpCallerUntil(callerLoop, () => completed, TimeSpan.FromSeconds(2));
        Assert.NotEqual(callerThreadId, actionThreadId);
    });

    Assert.True(completed);
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL.

- [ ] **Step 3: Implement async void overload**

Use the same `Post(() => RunAsyncVoidOnTargetLoop(...))` pattern; do not post an `async` lambda directly.

```csharp
public static OrientTask InvokeAsync(OrientLoop targetLoop, Func<OrientTask> action)
{
    ArgumentNullException.ThrowIfNull(targetLoop);
    ArgumentNullException.ThrowIfNull(action);

    var callerLoop = RequireCallerLoop();
    var source = new OrientTaskCompletionSource<OrientUnit>(callerLoop);

    if (ReferenceEquals(callerLoop, targetLoop))
    {
        RunAsyncVoidOnSameLoop(action, source);
        return new OrientTask(source.Task);
    }

    targetLoop.Post(() => RunAsyncVoidOnTargetLoop(action, source, callerLoop));
    return new OrientTask(source.Task);
}

private static async OrientTask RunAsyncVoidOnSameLoop(
    Func<OrientTask> action,
    OrientTaskCompletionSource<OrientUnit> source)
{
    try
    {
        await action();
        source.TrySetResult(OrientUnit.Value);
    }
    catch (TaskCanceledException)
    {
        source.TrySetCanceled();
    }
    catch (Exception exception)
    {
        source.TrySetException(exception);
    }
}

private static async OrientTask RunAsyncVoidOnTargetLoop(
    Func<OrientTask> action,
    OrientTaskCompletionSource<OrientUnit> source,
    OrientLoop callerLoop)
{
    try
    {
        await action();
        callerLoop.Post(() => source.TrySetResult(OrientUnit.Value));
    }
    catch (TaskCanceledException)
    {
        callerLoop.Post(() => source.TrySetCanceled());
    }
    catch (Exception exception)
    {
        callerLoop.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 4: Run async void test**

Expected: PASS.

---

### Task 6: Same-Loop, Exception, And Cancellation Behavior

**Files:**
- Modify: `Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing same-loop sync completion test**

```csharp
[Fact]
public void InvokeAsyncSameLoopSyncCompletesBeforeReturn()
{
    var loop = new OrientLoop();
    loop.BindToCurrentThread();

    var task = OrientLoop.InvokeAsync(loop, () => 99);
    var awaiter = task.GetAwaiter();

    Assert.True(awaiter.IsCompleted);
    Assert.Equal(99, awaiter.GetResult());
}
```

- [ ] **Step 2: Run test**

Expected: PASS once Task 2 landed; if not, implement same-loop branch already present.

- [ ] **Step 3: Write failing sync exception test**

```csharp
[Fact]
public void InvokeAsyncSyncExceptionFaultsCallerTask()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    Exception? captured = null;

    callerDriver.Run(() =>
    {
        var failure = new InvalidOperationException("sync boom");
        var task = OrientLoop.InvokeAsync<int>(
            targetLoop,
            () => throw failure);

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            try
            {
                awaiter.GetResult();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
    });

    Assert.IsType<InvalidOperationException>(captured);
    Assert.Equal("sync boom", captured!.Message);
}
```

- [ ] **Step 4: Write failing async exception test**

```csharp
[Fact]
public void InvokeAsyncAsyncTargetTaskExceptionFaultsCallerTask()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    Exception? captured = null;

    callerDriver.Run(() =>
    {
        var failure = new InvalidOperationException("async boom");
        var task = OrientLoop.InvokeAsync(
            targetLoop,
            () => FaultingTargetAsync(failure, targetLoop));

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            try
            {
                awaiter.GetResult();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
    });

    Assert.IsType<InvalidOperationException>(captured);
    Assert.Equal("async boom", captured!.Message);
}

private static async OrientTask<int> FaultingTargetAsync(Exception failure, OrientLoop targetLoop)
{
    var source = new OrientTaskCompletionSource<int>(targetLoop);
    source.TrySetException(failure);
    return await source.Task;
}
```

- [ ] **Step 5: Write failing runner-swallowed-exception regression test**

This verifies target runner exceptions do not disappear into `UnhandledException`:

```csharp
[Fact]
public void InvokeAsyncTargetRunnerExceptionFaultsCallerTaskInsteadOfUnhandledException()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    Exception? unhandled = null;
    Exception? captured = null;

    targetLoop.UnhandledException += ex => unhandled = ex;

    callerDriver.Run(() =>
    {
        var failure = new InvalidOperationException("runner boom");
        var task = OrientLoop.InvokeAsync(
            targetLoop,
            () => throw failure);

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            try
            {
                awaiter.GetResult();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
    });

    Assert.Null(unhandled);
    Assert.Equal("runner boom", captured!.Message);
}
```

- [ ] **Step 6: Write failing cancellation propagation test**

```csharp
[Fact]
public void InvokeAsyncCanceledTargetTaskCancelsCallerTask()
{
    var callerLoop = new OrientLoop();
    var targetLoop = new OrientLoop();
    using var targetPump = new TargetLoopPump(targetLoop);
    using var callerDriver = new LoopTestDriver(callerLoop);

    Exception? captured = null;

    callerDriver.Run(() =>
    {
        var task = OrientLoop.InvokeAsync(
            targetLoop,
            () =>
            {
                var source = new OrientTaskCompletionSource<OrientUnit>(targetLoop);
                source.TrySetCanceled();
                return new OrientTask(source.Task);
            });

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            try
            {
                awaiter.GetResult();
            }
            catch (Exception exception)
            {
                captured = exception;
            }
        });

        PumpCallerUntil(callerLoop, () => captured is not null, TimeSpan.FromSeconds(2));
    });

    Assert.IsType<TaskCanceledException>(captured);
}
```

- [ ] **Step 7: Run all InvokeAsync tests**

Run:

```bash
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter "FullyQualifiedName~OrientLoopInvokeAsyncTests" -v minimal
```

Expected: PASS for entire class.

---

### Task 7: Documentation Updates

**Files:**
- Modify: `Doc/architecture.md`
- Modify: `Doc/TODO.txt`

- [ ] **Step 1: Update architecture status wording**

In `Doc/architecture.md`, replace phrases such as:

- `OrientLoop.InvokeAsync`（**目标 API，尚未实现**）
- `InvokeAsync` 是**目标**框架级跨 loop 调度原语（Runtime 尚未实现）
- `OrientLoop.InvokeAsync`（未来）

with implemented wording, for example:

- `OrientLoop.InvokeAsync` 是 Runtime 提供的跨 loop 调度原语（`Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs`）
- Keep LocalRef / snapshot / `LoopRoute` / cancellation notes as future work

- [ ] **Step 2: Update TODO**

In `Doc/TODO.txt` §P1 item 2, remove the `OrientLoop.InvokeAsync` bullet and keep:

```text
   - 仍缺按 `serviceId`、连接或 shard 路由到多个业务 loop 的 `LoopRoute` / dispatcher。
```

Also fix the architecture reference from `architecture-draft §5.2` to `Doc/architecture.md` §5.2 if still present elsewhere.

- [ ] **Step 3: Run full Orient.Tests suite**

Run:

```bash
dotnet test Tests/Orient.Tests/Orient.Tests.csproj -v minimal
```

Expected: all tests PASS.

---

## Spec Coverage Checklist

| Spec requirement | Task |
| --- | --- |
| Four public overloads | Tasks 2–5 |
| Caller must be bound loop thread | Task 1 `RequireCallerLoop()`; public test covers missing `Current` only |
| Cross-loop uses `targetLoop.Post` + `callerLoop.Post` | Tasks 2–5 |
| Same-loop direct execution | Tasks 2–5 + Task 6 |
| Target runner catches all failures | Tasks 2–5 + Task 6 regression test |
| Cancellation maps to `TrySetCanceled()` | Task 4–5 helpers + Task 6 test |
| No `System.Threading.Tasks.Task` in implementation | Tasks 2–5 |
| Tests for success/exception/cancel/same-loop/invalid args | Tasks 1–6 |
| Documentation updates | Task 7 |
| Data boundary documented as non-runtime responsibility | Task 7 architecture wording only |

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-05-orientloop-invokeasync.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — execute tasks in this session with checkpoints between tasks

Which approach do you want?
