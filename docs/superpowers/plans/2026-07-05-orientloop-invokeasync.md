# OrientExecutor.InvokeAsync Implementation Plan

>
> **Updated 2026-07-18:** Renamed `OrientLoop` → `OrientExecutor` (and related vocabulary) to match the current Runtime API. Historical date/filename kept.
>


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the four `OrientExecutor.InvokeAsync` overloads in `Orient.Runtime` so same-process cross-executor business calls schedule on the target executor and complete on the caller executor as `OrientTask`.

**Architecture:** Add static methods on `OrientExecutor` that create a caller-owned `OrientTaskCompletionSource`, post an `async OrientTask` runner to the target executor (or run inline when `callerExecutor == targetExecutor`), and complete the caller task only via `callerExecutor.Post`. Target runners catch all failures locally and never rely on `DrainActions` exception isolation. Tests use a caller `ExecutorTestDriver` plus a background target-executor pump thread.

**Tech Stack:** C# / .NET 8, `Orient.Runtime`, xUnit, existing `Orient.TestHelper.ExecutorTestDriver`, `Orient.TestHelper.OrientTestBase`

**Spec:** `docs/superpowers/specs/2026-07-05-orientloop-invokeasync-design.md`

**Note on commits:** Do not commit unless the user explicitly asks. This plan omits commit steps.

---

## File Map

| File | Action | Responsibility |
| --- | --- | --- |
| `Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs` | Create | `partial` implementation of four public overloads + private helpers |
| `Orient.Runtime/Executor/OrientExecutor.cs` | Modify | Declare `public sealed partial class OrientExecutor` (change `class` → `partial class`) |
| `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs` | Create | Cross-executor / same-executor / error propagation tests + local dual-executor harness |
| `Doc/architecture.md` | Modify | Mark §5.2 runtime primitive as implemented |
| `Doc/TODO.txt` | Modify | Remove completed `InvokeAsync` item; keep `ExecutorRoute` pending |

---

### Task 1: Test Harness And Invalid Call-Site Tests

**Files:**
- Create: `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs`
- Modify: `Orient.Runtime/Executor/OrientExecutor.cs` (make class `partial` only)

- [ ] **Step 1: Make `OrientExecutor` partial**

In `Orient.Runtime/Executor/OrientExecutor.cs`, change:

```csharp
public sealed class OrientExecutor
```

to:

```csharp
public sealed partial class OrientExecutor
```

- [ ] **Step 2: Write failing invalid call-site tests**

Create `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs`:

```csharp
using Orient.Runtime;

namespace Orient.Tests;

public class OrientExecutorInvokeAsyncTests : OrientTestBase
{
    [Fact]
    public void InvokeAsyncWithoutCurrentLoopThrowsRequireCurrentOrMessage()
    {
        var targetExecutor = new OrientExecutor();
        var exception = RunOnFreshThread(() =>
            OrientExecutor.InvokeAsync(targetExecutor, () => 1));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("OrientExecutor must be provided", exception!.Message);
    }

    [Fact]
    public void InvokeAsyncNullTargetLoopThrows()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        Assert.Throws<ArgumentNullException>(() =>
            OrientExecutor.InvokeAsync(null!, () => 1));
    }

    [Fact]
    public void InvokeAsyncNullActionThrows()
    {
        var executor = new OrientExecutor();
        executor.BindToCurrentThread();

        Assert.Throws<ArgumentNullException>(() =>
            OrientExecutor.InvokeAsync(executor, (Func<int>)null!));
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
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter "FullyQualifiedName~OrientExecutorInvokeAsyncTests" -v minimal
```

Expected: FAIL — `OrientExecutor` does not contain a definition for `InvokeAsync`.

- [ ] **Step 4: Add shared validation to all overloads**

Create `Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs` with `RequireCallerExecutor()` and parameter validation on every overload. Only the sync-value overload proceeds past validation in this task; the other three overloads throw `NotImplementedException` after validation until their tasks land.

```csharp
namespace Orient.Runtime;

public sealed partial class OrientExecutor
{
    public static OrientTask<T> InvokeAsync<T>(OrientExecutor targetExecutor, Func<OrientTask<T>> action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerExecutor();
        throw new NotImplementedException();
    }

    public static OrientTask InvokeAsync(OrientExecutor targetExecutor, Func<OrientTask> action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerExecutor();
        throw new NotImplementedException();
    }

    public static OrientTask<T> InvokeAsync<T>(OrientExecutor targetExecutor, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerExecutor();
        throw new NotImplementedException();
    }

    public static OrientTask InvokeAsync(OrientExecutor targetExecutor, Action action)
    {
        ArgumentNullException.ThrowIfNull(targetExecutor);
        ArgumentNullException.ThrowIfNull(action);
        _ = RequireCallerExecutor();
        throw new NotImplementedException();
    }

    private static OrientExecutor RequireCallerExecutor()
    {
        var callerExecutor = Current
            ?? throw new InvalidOperationException(
                "A OrientExecutor must be provided explicitly or available via OrientExecutor.Current.");

        callerExecutor.EnsureInExecutorThread();
        return callerExecutor;
    }
}
```

Invalid call-site tests use the sync-value overload. `EnsureInExecutorThread()` remains internal defense only; there is no separate black-box test for it because `OrientExecutor.Current` is `[ThreadStatic]`.

- [ ] **Step 5: Re-run invalid call-site tests**

Run the same `dotnet test` filter.

Expected: PASS for the three invalid call-site tests; other tests not added yet.

---

### Task 2: Cross-Executor Synchronous Value Overload

**Files:**
- Modify: `Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs`

- [ ] **Step 1: Add dual-executor pump helper to test file**

Append to `OrientExecutorInvokeAsyncTests.cs`:

```csharp
private sealed class TargetExecutorPump : IDisposable
{
    private readonly OrientExecutor targetExecutor;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Thread thread;
    private volatile Exception? pumpFailure;

    public TargetExecutorPump(OrientExecutor targetExecutor)
    {
        this.targetExecutor = targetExecutor;
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
            throw new InvalidOperationException("Target executor pump failed.", pumpFailure);
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            throw new InvalidOperationException("Target executor pump thread did not exit.");
        }

        EnsureNoFailure();
    }

    private void PumpMain()
    {
        targetExecutor.BindToCurrentThread();
        try
        {
            while (!cancellation.Token.IsCancellationRequested)
            {
                targetExecutor.Tick();
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
            OrientExecutor.ResetDebugThreadBindingForTests();
#endif
        }
    }
}

private static void PumpCallerUntil(OrientExecutor callerExecutor, Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition() && DateTime.UtcNow < deadline)
    {
        callerExecutor.Tick();
        Thread.Sleep(1);
    }

    if (!condition())
    {
        throw new TimeoutException("Caller executor pump timed out.");
    }
}
```

- [ ] **Step 2: Write failing cross-executor sync value test**

Add:

```csharp
[Fact]
public void InvokeAsyncSyncValueRunsOnTargetLoopAndCompletesOnCallerLoop()
{
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    int? actionThreadId = null;
    int? continuationThreadId = null;
    int? result = null;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientExecutor.InvokeAsync(
            targetExecutor,
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

        PumpCallerUntil(callerExecutor, () => result is not null, TimeSpan.FromSeconds(2));

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

- [ ] **Step 4: Implement sync-value cross-executor path**

In `OrientExecutor.InvokeAsync.cs`, implement helpers and sync-value overload:

```csharp
public static OrientTask<T> InvokeAsync<T>(OrientExecutor targetExecutor, Func<T> action)
{
    ArgumentNullException.ThrowIfNull(targetExecutor);
    ArgumentNullException.ThrowIfNull(action);

    var callerExecutor = RequireCallerExecutor();
    var source = new OrientTaskCompletionSource<T>(callerExecutor);

    if (ReferenceEquals(callerExecutor, targetExecutor))
    {
        RunSyncOnSameExecutor(action, source);
        return source.Task;
    }

    targetExecutor.Post(() => RunSyncOnTargetExecutor(action, source, callerExecutor));
    return source.Task;
}

private static void RunSyncOnSameExecutor<T>(Func<T> action, OrientTaskCompletionSource<T> source)
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

private static void RunSyncOnTargetExecutor<T>(
    Func<T> action,
    OrientTaskCompletionSource<T> source,
    OrientExecutor callerExecutor)
{
    try
    {
        var result = action();
        callerExecutor.Post(() => source.TrySetResult(result));
    }
    catch (Exception exception)
    {
        callerExecutor.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 5: Run sync-value test**

Expected: PASS.

---

### Task 3: Cross-Executor Synchronous Void Overload

**Files:**
- Modify: `Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing sync void test**

```csharp
[Fact]
public void InvokeAsyncSyncVoidRunsOnTargetLoopAndCompletesOnCallerLoop()
{
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    int? actionThreadId = null;
    var completed = false;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientExecutor.InvokeAsync(
            targetExecutor,
            () => actionThreadId = Environment.CurrentManagedThreadId);

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            awaiter.GetResult();
            completed = true;
        });

        PumpCallerUntil(callerExecutor, () => completed, TimeSpan.FromSeconds(2));
        Assert.NotEqual(callerThreadId, actionThreadId);
    });

    Assert.True(completed);
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL (`NotImplementedException`).

- [ ] **Step 3: Implement sync void overload**

```csharp
public static OrientTask InvokeAsync(OrientExecutor targetExecutor, Action action)
{
    ArgumentNullException.ThrowIfNull(targetExecutor);
    ArgumentNullException.ThrowIfNull(action);

    var callerExecutor = RequireCallerExecutor();
    var source = new OrientTaskCompletionSource<OrientUnit>(callerExecutor);

    if (ReferenceEquals(callerExecutor, targetExecutor))
    {
        RunVoidOnSameExecutor(action, source);
        return new OrientTask(source.Task);
    }

    targetExecutor.Post(() => RunVoidOnTargetExecutor(action, source, callerExecutor));
    return new OrientTask(source.Task);
}

private static void RunVoidOnSameExecutor(Action action, OrientTaskCompletionSource<OrientUnit> source)
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

private static void RunVoidOnTargetExecutor(
    Action action,
    OrientTaskCompletionSource<OrientUnit> source,
    OrientExecutor callerExecutor)
{
    try
    {
        action();
        callerExecutor.Post(() => source.TrySetResult(OrientUnit.Value));
    }
    catch (Exception exception)
    {
        callerExecutor.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 4: Run sync void test**

Expected: PASS.

---

### Task 4: Cross-Executor Asynchronous Value Overload

**Files:**
- Modify: `Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing async value test**

```csharp
[Fact]
public void InvokeAsyncAsyncValueRunsOnTargetLoopAndResumesOnCallerLoop()
{
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    int? actionThreadId = null;
    int? continuationThreadId = null;
    int? result = null;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientExecutor.InvokeAsync(
            targetExecutor,
            async () =>
            {
                actionThreadId = Environment.CurrentManagedThreadId;
                await OrientTask.Delay(1, targetExecutor);
                return 7;
            });

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            continuationThreadId = Environment.CurrentManagedThreadId;
            result = awaiter.GetResult();
        });

        PumpCallerUntil(callerExecutor, () => result is not null, TimeSpan.FromSeconds(2));
        Assert.Equal(callerThreadId, continuationThreadId);
        Assert.NotEqual(callerThreadId, actionThreadId);
    });

    Assert.Equal(7, result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL.

- [ ] **Step 3: Implement async-value overload + shared async runner**

Important: start cross-executor async runners with `targetExecutor.Post(() => RunAsyncOnTargetExecutor(...))`. Do **not** use `targetExecutor.Post(async () => ...)` because `Post` takes `Action`.

```csharp
public static OrientTask<T> InvokeAsync<T>(OrientExecutor targetExecutor, Func<OrientTask<T>> action)
{
    ArgumentNullException.ThrowIfNull(targetExecutor);
    ArgumentNullException.ThrowIfNull(action);

    var callerExecutor = RequireCallerExecutor();
    var source = new OrientTaskCompletionSource<T>(callerExecutor);

    if (ReferenceEquals(callerExecutor, targetExecutor))
    {
        RunAsyncOnSameExecutor(action, source);
        return source.Task;
    }

    targetExecutor.Post(() => RunAsyncOnTargetExecutor(action, source, callerExecutor));
    return source.Task;
}

private static async OrientTask RunAsyncOnSameExecutor<T>(
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

private static async OrientTask RunAsyncOnTargetExecutor<T>(
    Func<OrientTask<T>> action,
    OrientTaskCompletionSource<T> source,
    OrientExecutor callerExecutor)
{
    try
    {
        var result = await action();
        callerExecutor.Post(() => source.TrySetResult(result));
    }
    catch (TaskCanceledException)
    {
        callerExecutor.Post(() => source.TrySetCanceled());
    }
    catch (Exception exception)
    {
        callerExecutor.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 4: Run async value test**

Expected: PASS.

---

### Task 5: Cross-Executor Asynchronous Void Overload

**Files:**
- Modify: `Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs`
- Modify: `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing async void test**

```csharp
[Fact]
public void InvokeAsyncAsyncVoidRunsOnTargetLoopAndCompletesOnCallerLoop()
{
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    int? actionThreadId = null;
    var completed = false;

    callerDriver.Run(() =>
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var task = OrientExecutor.InvokeAsync(
            targetExecutor,
            async () =>
            {
                actionThreadId = Environment.CurrentManagedThreadId;
                await OrientTask.Delay(1, targetExecutor);
            });

        var awaiter = task.GetAwaiter();
        awaiter.OnCompleted(() =>
        {
            awaiter.GetResult();
            completed = true;
        });

        PumpCallerUntil(callerExecutor, () => completed, TimeSpan.FromSeconds(2));
        Assert.NotEqual(callerThreadId, actionThreadId);
    });

    Assert.True(completed);
}
```

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL.

- [ ] **Step 3: Implement async void overload**

Use the same `Post(() => RunAsyncVoidOnTargetExecutor(...))` pattern; do not post an `async` lambda directly.

```csharp
public static OrientTask InvokeAsync(OrientExecutor targetExecutor, Func<OrientTask> action)
{
    ArgumentNullException.ThrowIfNull(targetExecutor);
    ArgumentNullException.ThrowIfNull(action);

    var callerExecutor = RequireCallerExecutor();
    var source = new OrientTaskCompletionSource<OrientUnit>(callerExecutor);

    if (ReferenceEquals(callerExecutor, targetExecutor))
    {
        RunAsyncVoidOnSameExecutor(action, source);
        return new OrientTask(source.Task);
    }

    targetExecutor.Post(() => RunAsyncVoidOnTargetExecutor(action, source, callerExecutor));
    return new OrientTask(source.Task);
}

private static async OrientTask RunAsyncVoidOnSameExecutor(
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

private static async OrientTask RunAsyncVoidOnTargetExecutor(
    Func<OrientTask> action,
    OrientTaskCompletionSource<OrientUnit> source,
    OrientExecutor callerExecutor)
{
    try
    {
        await action();
        callerExecutor.Post(() => source.TrySetResult(OrientUnit.Value));
    }
    catch (TaskCanceledException)
    {
        callerExecutor.Post(() => source.TrySetCanceled());
    }
    catch (Exception exception)
    {
        callerExecutor.Post(() => source.TrySetException(exception));
    }
}
```

- [ ] **Step 4: Run async void test**

Expected: PASS.

---

### Task 6: Same-Executor, Exception, And Cancellation Behavior

**Files:**
- Modify: `Tests/Orient.Tests/OrientExecutorInvokeAsyncTests.cs`

- [ ] **Step 1: Write failing same-executor sync completion test**

```csharp
[Fact]
public void InvokeAsyncSameLoopSyncCompletesBeforeReturn()
{
    var executor = new OrientExecutor();
    executor.BindToCurrentThread();

    var task = OrientExecutor.InvokeAsync(executor, () => 99);
    var awaiter = task.GetAwaiter();

    Assert.True(awaiter.IsCompleted);
    Assert.Equal(99, awaiter.GetResult());
}
```

- [ ] **Step 2: Run test**

Expected: PASS once Task 2 landed; if not, implement same-executor branch already present.

- [ ] **Step 3: Write failing sync exception test**

```csharp
[Fact]
public void InvokeAsyncSyncExceptionFaultsCallerTask()
{
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    Exception? captured = null;

    callerDriver.Run(() =>
    {
        var failure = new InvalidOperationException("sync boom");
        var task = OrientExecutor.InvokeAsync<int>(
            targetExecutor,
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

        PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
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
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    Exception? captured = null;

    callerDriver.Run(() =>
    {
        var failure = new InvalidOperationException("async boom");
        var task = OrientExecutor.InvokeAsync(
            targetExecutor,
            () => FaultingTargetAsync(failure, targetExecutor));

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

        PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
    });

    Assert.IsType<InvalidOperationException>(captured);
    Assert.Equal("async boom", captured!.Message);
}

private static async OrientTask<int> FaultingTargetAsync(Exception failure, OrientExecutor targetExecutor)
{
    var source = new OrientTaskCompletionSource<int>(targetExecutor);
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
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    Exception? unhandled = null;
    Exception? captured = null;

    targetExecutor.UnhandledException += ex => unhandled = ex;

    callerDriver.Run(() =>
    {
        var failure = new InvalidOperationException("runner boom");
        var task = OrientExecutor.InvokeAsync(
            targetExecutor,
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

        PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
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
    var callerExecutor = new OrientExecutor();
    var targetExecutor = new OrientExecutor();
    using var targetPump = new TargetExecutorPump(targetExecutor);
    using var callerDriver = new ExecutorTestDriver(callerExecutor);

    Exception? captured = null;

    callerDriver.Run(() =>
    {
        var task = OrientExecutor.InvokeAsync(
            targetExecutor,
            () =>
            {
                var source = new OrientTaskCompletionSource<OrientUnit>(targetExecutor);
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

        PumpCallerUntil(callerExecutor, () => captured is not null, TimeSpan.FromSeconds(2));
    });

    Assert.IsType<TaskCanceledException>(captured);
}
```

- [ ] **Step 7: Run all InvokeAsync tests**

Run:

```bash
dotnet test Tests/Orient.Tests/Orient.Tests.csproj --filter "FullyQualifiedName~OrientExecutorInvokeAsyncTests" -v minimal
```

Expected: PASS for entire class.

---

### Task 7: Documentation Updates

**Files:**
- Modify: `Doc/architecture.md`
- Modify: `Doc/TODO.txt`

- [ ] **Step 1: Update architecture status wording**

In `Doc/architecture.md`, replace phrases such as:

- `OrientExecutor.InvokeAsync`（**目标 API，尚未实现**）
- `InvokeAsync` 是**目标**框架级跨 executor 调度原语（Runtime 尚未实现）
- `OrientExecutor.InvokeAsync`（未来）

with implemented wording, for example:

- `OrientExecutor.InvokeAsync` 是 Runtime 提供的跨 executor 调度原语（`Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs`）
- Keep LocalRef / snapshot / `ExecutorRoute` / cancellation notes as future work

- [ ] **Step 2: Update TODO**

In `Doc/TODO.txt` §P1 item 2, remove the `OrientExecutor.InvokeAsync` bullet and keep:

```text
   - 仍缺按 `serviceId`、连接或 shard 路由到多个业务 executor 的 `ExecutorRoute` / dispatcher。
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
| Caller must be bound executor thread | Task 1 `RequireCallerExecutor()`; public test covers missing `Current` only |
| Cross-executor uses `targetExecutor.Post` + `callerExecutor.Post` | Tasks 2–5 |
| Same-executor direct execution | Tasks 2–5 + Task 6 |
| Target runner catches all failures | Tasks 2–5 + Task 6 regression test |
| Cancellation maps to `TrySetCanceled()` | Task 4–5 helpers + Task 6 test |
| No `System.Threading.Tasks.Task` in implementation | Tasks 2–5 |
| Tests for success/exception/cancel/same-executor/invalid args | Tasks 1–6 |
| Documentation updates | Task 7 |
| Data boundary documented as non-runtime responsibility | Task 7 architecture wording only |

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-05-orientloop-invokeasync.md`. Two execution options:

**1. Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** — execute tasks in this session with checkpoints between tasks

Which approach do you want?
