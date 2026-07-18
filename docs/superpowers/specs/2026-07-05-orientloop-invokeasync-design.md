# OrientExecutor.InvokeAsync Design

>
> **Updated 2026-07-18:** Renamed `OrientLoop` → `OrientExecutor` (and related vocabulary) to match the current Runtime API. Historical date/filename kept.
>


Date: 2026-07-05

## Summary

`OrientExecutor.InvokeAsync` is the runtime primitive for same-process cross-executor business calls. It lets code running on one `OrientExecutor` schedule work on another `OrientExecutor` and receive the result back on the caller executor as an `OrientTask`.

This design only covers the `Orient.Runtime` primitive. It does not add RPC routing, generated LocalRef code, cancellation, deadlines, or ownership-transfer APIs.

## Goals

- Provide a small public API for same-process cross-executor calls.
- Preserve the existing executor ownership model: business state and `OrientTaskCompletionSource` completion stay on their owner executor thread.
- Make cross-executor scheduling explicit and reusable so business code does not hand-roll `Post` + completion-source plumbing.
- Keep the first implementation independent of `Orient.Rpc`; `Orient.Runtime` remains BCL-only.
- Document that `InvokeAsync` guarantees scheduling and completion ownership, not object thread safety.

## Non-Goals

- Do not implement `CRpcServer` / HTTP `ExecutorRoute` dispatch.
- Do not generate LocalRef wrappers.
- Do not add cancellation token support or deadlines.
- Do not add runtime object graph validation for captured closure state.
- Do not add `InvokeOwnedAsync` or zero-copy ownership transfer.
- Do not serialize or clone data inside `OrientExecutor.InvokeAsync`.

## Public API

Add four static overloads to `OrientExecutor`:

```csharp
public static OrientTask<T> InvokeAsync<T>(
    OrientExecutor targetExecutor,
    Func<OrientTask<T>> action);

public static OrientTask InvokeAsync(
    OrientExecutor targetExecutor,
    Func<OrientTask> action);

public static OrientTask<T> InvokeAsync<T>(
    OrientExecutor targetExecutor,
    Func<T> action);

public static OrientTask InvokeAsync(
    OrientExecutor targetExecutor,
    Action action);
```

The overloads cover two axes:

- synchronous target action vs target action returning `OrientTask`
- result value vs no result value

All overloads must be called from a bound caller `OrientExecutor` thread. The returned task belongs to the caller executor.

Invalid call-site errors should follow existing runtime conventions:

- when `OrientExecutor.Current` is null, throw `InvalidOperationException` with the same message shape as `OrientExecutor.RequireCurrentOr()`

`RequireCallerExecutor()` should also call `EnsureInExecutorThread()` on the resolved caller executor as an internal defense-in-depth check. Because `OrientExecutor.Current` is `[ThreadStatic]`, a caller thread with `Current == null` cannot reach the `EnsureInExecutorThread()` path through ordinary public usage. The first testable public contract is therefore "no current executor throws"; do not add a separate black-box test that expects the `EnsureInExecutorThread()` message from another thread.

## Operational Preconditions

`InvokeAsync` does not start executor drivers. It only schedules work through `Post`.

Both the caller executor and the target executor must already be bound and actively driven by a host such as `OrientExecutorHost`, `OrientExecutorRunner`, or a test harness that calls `Tick()`. If the target executor receives a posted runner but is not pumped, the returned caller task remains pending indefinitely. This matches existing `OrientExecutor.Post` semantics and is not a special hang mode introduced by `InvokeAsync`.

## Threading Semantics

`InvokeAsync` has one caller executor and one target executor:

- `callerExecutor` is `OrientExecutor.Current` at the call site.
- `targetExecutor` is the explicit destination.
- The returned `OrientTask` is completed on `callerExecutor`.
- The supplied `action` executes on `targetExecutor`.
- If `callerExecutor == targetExecutor`, the action may run directly without a mailbox round trip.
- If the loops differ, the implementation must use `targetExecutor.Post` to enter the target executor and `callerExecutor.Post` to complete the caller task.

The implementation must never call `TrySetResult`, `TrySetException`, or `TrySetCanceled` for the caller task from the target executor thread.

## Execution Flow

For a cross-executor asynchronous value call:

1. Caller executor validates `targetExecutor` and `action`.
2. Caller executor creates `OrientTaskCompletionSource<T>` owned by `callerExecutor`.
3. Caller executor posts a target runner to `targetExecutor`.
4. Target executor executes `action`.
5. If `action` returns a pending `OrientTask<T>`, target executor awaits it on the target executor.
6. Target executor posts the final result, exception, or cancellation back to `callerExecutor`.
7. Caller executor completes the caller-owned completion source.
8. Await continuations of the returned task resume on the caller executor through the existing `OrientTask` continuation rules.

Synchronous overloads follow the same completion path, except the target runner computes the result directly before posting completion back to the caller executor.

For asynchronous overloads, start cross-executor work with `targetExecutor.Post(() => RunAsyncOnTargetExecutor(...))`, where `RunAsyncOnTargetExecutor` is an `async OrientTask` helper that can `await` the action's returned target task on the target executor. Do **not** write `targetExecutor.Post(async () => ...)` because `Post` takes `Action` and an `async` lambda becomes fire-and-forget. A plain synchronous `Action` that ignores a pending target task is not acceptable.

## Error And Cancellation Propagation

Exceptions propagate across the executor boundary as task failures:

- If a synchronous `action` throws, the returned caller task faults with that exception.
- If an asynchronous `action` throws before returning its target task, the returned caller task faults with that exception.
- If the target `OrientTask<T>` or `OrientTask` faults, the returned caller task faults with the same exception.
- If the target task is canceled, the returned caller task is canceled.

All fault and cancellation completion must happen on the caller executor.

The first implementation does not expose caller-driven cancellation. Cancellation can only be propagated if the target action's returned `OrientTask` is already canceled by target-side logic.

### Target Runner Must Not Rely On Executor Exception Isolation

`OrientExecutor.Tick()` isolates exceptions thrown by posted actions through `UnhandledException` and keeps the executor alive. If a target-side runner throws before posting completion back to the caller executor, the caller task would remain pending forever even though the target executor continued running.

Therefore, every target-side runner posted by `InvokeAsync` must catch all failures itself and forward them to the caller executor through `callerExecutor.Post(...)`, where the caller-owned completion source is completed.

This applies to:

- synchronous `action` throws inside the target runner
- asynchronous `action` throws before returning its target task
- faults or cancellation while awaiting the returned target `OrientTask`
- any unexpected exception inside the runner's own scheduling logic

When the returned target task is already canceled, the target runner must complete the caller task with `TrySetCanceled()`, not `TrySetException(...)`.

The implementation must not depend on target-executor `UnhandledException` to fault the caller task.

If the caller task has already completed, later duplicate completion attempts follow existing `OrientTaskCompletionSource` behavior and are ignored.

## Data Boundary

`InvokeAsync` is a scheduling primitive, not a shared-memory safety primitive. It guarantees that target work runs on the target executor and caller completion happens on the caller executor. It does not guarantee that objects captured by the `action` closure are immutable, cloned, or safe to access from both loops.

The design rule is:

> Ordinary cross-executor business communication is message passing by snapshot. Shared mutable objects must not cross executor boundaries.

Allowed values for cross-executor boundaries:

- primitive values
- IDs
- enums
- `string`
- genuinely immutable objects
- DTO/protobuf snapshots created by the caller or a generated wrapper

Values that must not cross via ordinary `InvokeAsync` business calls:

- service internal entities
- mutable collections and caches
- `CRpcContext`
- `CRpcConnection`
- reusable buffers or mutable `byte[]` unless copied
- objects whose ownership remains with another executor

Because arbitrary closures cannot be reliably inspected at runtime, `InvokeAsync(Func...)` should be treated as a low-level API. Business-facing cross-executor calls should eventually go through generated or hand-written LocalRef wrappers that enforce snapshot policy.

## Future Boundary Controls

The following controls are intentionally outside this first implementation but should guide follow-up work:

- Generated LocalRef wrappers clone protobuf requests before entering the target executor and clone protobuf replies before returning to the caller executor.
- A Roslyn analyzer can forbid direct business-layer use of bare `OrientExecutor.InvokeAsync(Func...)`, except in approved framework, generated, or test files.
- Code generation can reject unsafe LocalRef signatures such as `CRpcContext`, `CRpcConnection`, mutable collections, or unmarked mutable classes.
- Debug-only validation can check LocalRef argument and result types against a whitelist.
- Performance-sensitive zero-copy paths should use explicit ownership-transfer APIs such as a future `InvokeOwnedAsync`, not ordinary `InvokeAsync`.

## Implementation Shape

The implementation should live in `Orient.Runtime/Executor/` as a `partial` extension of `OrientExecutor`, preferably in `OrientExecutor.InvokeAsync.cs`, close to `Post` and thread ownership logic in `OrientExecutor.cs`. Private helper methods can share the cross-executor completion path across overloads.

The helper shape should keep three responsibilities separate:

- caller-side setup: validate current executor, create caller-owned completion source, choose direct vs posted execution
- target-side execution: run or await the supplied action on the target executor
- caller-side completion: complete the caller-owned source on the caller executor

No helper should require `System.Threading.Tasks.Task` interop. `OrientTask` remains the async primitive.

Recommended internal shape for cross-executor asynchronous overloads:

```csharp
targetExecutor.Post(() => RunAsyncOnTargetExecutor(action, source, callerExecutor));

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
    catch (Exception ex)
    {
        callerExecutor.Post(() => source.TrySetException(ex));
    }
}
```

`TaskCanceledException` is allowed here because existing `OrientTaskCompletionSource` already uses it for canceled task results. This is not `System.Threading.Tasks.Task` interop.

The synchronous overloads can use the same `callerExecutor.Post` completion path, but without `await`.

Even when `callerExecutor == targetExecutor`, exception forwarding must still complete the caller-owned source through the normal caller completion path rather than letting exceptions escape into `DrainActions`.

## Same-Executor Behavior

When `callerExecutor` and `targetExecutor` are the same instance, `InvokeAsync` should execute the action directly on the current executor thread. This preserves normal local call behavior and avoids unnecessary mailbox scheduling.

Even in same-executor mode, the returned task is still caller-owned. If the target action returns a pending `OrientTask`, normal `OrientTask` await behavior resumes on the same executor.

## Testing Plan

Add focused tests under `Tests/Orient.Tests`, likely in a new `OrientExecutorInvokeAsyncTests.cs` file.

Required coverage:

- synchronous value action runs on target executor and result continuation resumes on caller executor
- synchronous void action runs on target executor and caller task completes on caller executor
- asynchronous value action awaits target-executor work and resumes caller continuation on caller executor
- asynchronous void action follows the same ownership behavior
- same-executor synchronous invocation completes on the current executor without requiring an extra target-executor mailbox round trip
- synchronous exception faults the caller task on caller executor
- asynchronous target task exception faults the caller task on caller executor
- target-side runner exception is forwarded to the caller task instead of being swallowed by target-executor exception isolation
- target task cancellation cancels the caller task on caller executor
- calling without a current caller executor throws with the `RequireCurrentOr()` message shape
- passing null `targetExecutor` or null `action` throws `ArgumentNullException` on every overload

Cross-executor tests should drive two loops with existing helpers such as `ExecutorTestDriver` and `DedicatedExecutorThread`, and both loops must be pumped with `Tick()` until the caller task completes. Tests should assert observable thread IDs rather than inspecting private fields.

For same-executor coverage, prefer behavioral assertions such as "synchronous action completes before `InvokeAsync` returns" rather than asserting that `Post` was not called internally.

## Documentation Updates

After implementation:

- Update `Doc/architecture.md` §5.2 from "target, not implemented" to implemented runtime primitive.
- Keep LocalRef, snapshot generation, `ExecutorRoute`, cancellation, and ownership-transfer notes marked as future work.
- Update `Doc/TODO.txt` to remove the `OrientExecutor.InvokeAsync` missing item while leaving `ExecutorRoute` / dispatcher as pending.

## Acceptance Criteria

- `OrientExecutor.InvokeAsync` exposes the four overloads above.
- Cross-executor action execution happens only on the target executor thread.
- Caller task completion happens only on the caller executor thread.
- Exceptions and cancellation propagate back to the caller task.
- Target-side runner failures are forwarded to the caller task and do not rely on target-executor `UnhandledException`.
- No `System.Threading.Tasks.Task` is introduced into the implementation.
- Tests cover success, exception, cancellation, same-executor, and invalid argument behavior.
- Documentation distinguishes runtime scheduling guarantees from business data snapshot guarantees.
