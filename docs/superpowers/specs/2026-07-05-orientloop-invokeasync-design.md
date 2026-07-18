# OrientLoop.InvokeAsync Design

Date: 2026-07-05

## Summary

`OrientLoop.InvokeAsync` is the runtime primitive for same-process cross-loop business calls. It lets code running on one `OrientLoop` schedule work on another `OrientLoop` and receive the result back on the caller loop as an `OrientTask`.

This design only covers the `Orient.Runtime` primitive. It does not add RPC routing, generated LocalRef code, cancellation, deadlines, or ownership-transfer APIs.

## Goals

- Provide a small public API for same-process cross-loop calls.
- Preserve the existing loop ownership model: business state and `OrientTaskCompletionSource` completion stay on their owner loop thread.
- Make cross-loop scheduling explicit and reusable so business code does not hand-roll `Post` + completion-source plumbing.
- Keep the first implementation independent of `Orient.Rpc`; `Orient.Runtime` remains BCL-only.
- Document that `InvokeAsync` guarantees scheduling and completion ownership, not object thread safety.

## Non-Goals

- Do not implement `CRpcServer` / HTTP `LoopRoute` dispatch.
- Do not generate LocalRef wrappers.
- Do not add cancellation token support or deadlines.
- Do not add runtime object graph validation for captured closure state.
- Do not add `InvokeOwnedAsync` or zero-copy ownership transfer.
- Do not serialize or clone data inside `OrientLoop.InvokeAsync`.

## Public API

Add four static overloads to `OrientLoop`:

```csharp
public static OrientTask<T> InvokeAsync<T>(
    OrientLoop targetLoop,
    Func<OrientTask<T>> action);

public static OrientTask InvokeAsync(
    OrientLoop targetLoop,
    Func<OrientTask> action);

public static OrientTask<T> InvokeAsync<T>(
    OrientLoop targetLoop,
    Func<T> action);

public static OrientTask InvokeAsync(
    OrientLoop targetLoop,
    Action action);
```

The overloads cover two axes:

- synchronous target action vs target action returning `OrientTask`
- result value vs no result value

All overloads must be called from a bound caller `OrientLoop` thread. The returned task belongs to the caller loop.

Invalid call-site errors should follow existing runtime conventions:

- when `OrientLoop.Current` is null, throw `InvalidOperationException` with the same message shape as `OrientLoop.RequireCurrentOr()`

`RequireCallerLoop()` should also call `EnsureInLoopThread()` on the resolved caller loop as an internal defense-in-depth check. Because `OrientLoop.Current` is `[ThreadStatic]`, a caller thread with `Current == null` cannot reach the `EnsureInLoopThread()` path through ordinary public usage. The first testable public contract is therefore "no current loop throws"; do not add a separate black-box test that expects the `EnsureInLoopThread()` message from another thread.

## Operational Preconditions

`InvokeAsync` does not start loop drivers. It only schedules work through `Post`.

Both the caller loop and the target loop must already be bound and actively driven by a host such as `OrientLoopHost`, `OrientLoopRunner`, or a test harness that calls `Tick()`. If the target loop receives a posted runner but is not pumped, the returned caller task remains pending indefinitely. This matches existing `OrientLoop.Post` semantics and is not a special hang mode introduced by `InvokeAsync`.

## Threading Semantics

`InvokeAsync` has one caller loop and one target loop:

- `callerLoop` is `OrientLoop.Current` at the call site.
- `targetLoop` is the explicit destination.
- The returned `OrientTask` is completed on `callerLoop`.
- The supplied `action` executes on `targetLoop`.
- If `callerLoop == targetLoop`, the action may run directly without a mailbox round trip.
- If the loops differ, the implementation must use `targetLoop.Post` to enter the target loop and `callerLoop.Post` to complete the caller task.

The implementation must never call `TrySetResult`, `TrySetException`, or `TrySetCanceled` for the caller task from the target loop thread.

## Execution Flow

For a cross-loop asynchronous value call:

1. Caller loop validates `targetLoop` and `action`.
2. Caller loop creates `OrientTaskCompletionSource<T>` owned by `callerLoop`.
3. Caller loop posts a target runner to `targetLoop`.
4. Target loop executes `action`.
5. If `action` returns a pending `OrientTask<T>`, target loop awaits it on the target loop.
6. Target loop posts the final result, exception, or cancellation back to `callerLoop`.
7. Caller loop completes the caller-owned completion source.
8. Await continuations of the returned task resume on the caller loop through the existing `OrientTask` continuation rules.

Synchronous overloads follow the same completion path, except the target runner computes the result directly before posting completion back to the caller loop.

For asynchronous overloads, start cross-loop work with `targetLoop.Post(() => RunAsyncOnTargetLoop(...))`, where `RunAsyncOnTargetLoop` is an `async OrientTask` helper that can `await` the action's returned target task on the target loop. Do **not** write `targetLoop.Post(async () => ...)` because `Post` takes `Action` and an `async` lambda becomes fire-and-forget. A plain synchronous `Action` that ignores a pending target task is not acceptable.

## Error And Cancellation Propagation

Exceptions propagate across the loop boundary as task failures:

- If a synchronous `action` throws, the returned caller task faults with that exception.
- If an asynchronous `action` throws before returning its target task, the returned caller task faults with that exception.
- If the target `OrientTask<T>` or `OrientTask` faults, the returned caller task faults with the same exception.
- If the target task is canceled, the returned caller task is canceled.

All fault and cancellation completion must happen on the caller loop.

The first implementation does not expose caller-driven cancellation. Cancellation can only be propagated if the target action's returned `OrientTask` is already canceled by target-side logic.

### Target Runner Must Not Rely On Loop Exception Isolation

`OrientLoop.Tick()` isolates exceptions thrown by posted actions through `UnhandledException` and keeps the loop alive. If a target-side runner throws before posting completion back to the caller loop, the caller task would remain pending forever even though the target loop continued running.

Therefore, every target-side runner posted by `InvokeAsync` must catch all failures itself and forward them to the caller loop through `callerLoop.Post(...)`, where the caller-owned completion source is completed.

This applies to:

- synchronous `action` throws inside the target runner
- asynchronous `action` throws before returning its target task
- faults or cancellation while awaiting the returned target `OrientTask`
- any unexpected exception inside the runner's own scheduling logic

When the returned target task is already canceled, the target runner must complete the caller task with `TrySetCanceled()`, not `TrySetException(...)`.

The implementation must not depend on target-loop `UnhandledException` to fault the caller task.

If the caller task has already completed, later duplicate completion attempts follow existing `OrientTaskCompletionSource` behavior and are ignored.

## Data Boundary

`InvokeAsync` is a scheduling primitive, not a shared-memory safety primitive. It guarantees that target work runs on the target loop and caller completion happens on the caller loop. It does not guarantee that objects captured by the `action` closure are immutable, cloned, or safe to access from both loops.

The design rule is:

> Ordinary cross-loop business communication is message passing by snapshot. Shared mutable objects must not cross loop boundaries.

Allowed values for cross-loop boundaries:

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
- objects whose ownership remains with another loop

Because arbitrary closures cannot be reliably inspected at runtime, `InvokeAsync(Func...)` should be treated as a low-level API. Business-facing cross-loop calls should eventually go through generated or hand-written LocalRef wrappers that enforce snapshot policy.

## Future Boundary Controls

The following controls are intentionally outside this first implementation but should guide follow-up work:

- Generated LocalRef wrappers clone protobuf requests before entering the target loop and clone protobuf replies before returning to the caller loop.
- A Roslyn analyzer can forbid direct business-layer use of bare `OrientLoop.InvokeAsync(Func...)`, except in approved framework, generated, or test files.
- Code generation can reject unsafe LocalRef signatures such as `CRpcContext`, `CRpcConnection`, mutable collections, or unmarked mutable classes.
- Debug-only validation can check LocalRef argument and result types against a whitelist.
- Performance-sensitive zero-copy paths should use explicit ownership-transfer APIs such as a future `InvokeOwnedAsync`, not ordinary `InvokeAsync`.

## Implementation Shape

The implementation should live in `Orient.Runtime/Loop/` as a `partial` extension of `OrientLoop`, preferably in `OrientLoop.InvokeAsync.cs`, close to `Post` and thread ownership logic in `OrientLoop.cs`. Private helper methods can share the cross-loop completion path across overloads.

The helper shape should keep three responsibilities separate:

- caller-side setup: validate current loop, create caller-owned completion source, choose direct vs posted execution
- target-side execution: run or await the supplied action on the target loop
- caller-side completion: complete the caller-owned source on the caller loop

No helper should require `System.Threading.Tasks.Task` interop. `OrientTask` remains the async primitive.

Recommended internal shape for cross-loop asynchronous overloads:

```csharp
targetLoop.Post(() => RunAsyncOnTargetLoop(action, source, callerLoop));

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
    catch (Exception ex)
    {
        callerLoop.Post(() => source.TrySetException(ex));
    }
}
```

`TaskCanceledException` is allowed here because existing `OrientTaskCompletionSource` already uses it for canceled task results. This is not `System.Threading.Tasks.Task` interop.

The synchronous overloads can use the same `callerLoop.Post` completion path, but without `await`.

Even when `callerLoop == targetLoop`, exception forwarding must still complete the caller-owned source through the normal caller completion path rather than letting exceptions escape into `DrainActions`.

## Same-Loop Behavior

When `callerLoop` and `targetLoop` are the same instance, `InvokeAsync` should execute the action directly on the current loop thread. This preserves normal local call behavior and avoids unnecessary mailbox scheduling.

Even in same-loop mode, the returned task is still caller-owned. If the target action returns a pending `OrientTask`, normal `OrientTask` await behavior resumes on the same loop.

## Testing Plan

Add focused tests under `Tests/Orient.Tests`, likely in a new `OrientLoopInvokeAsyncTests.cs` file.

Required coverage:

- synchronous value action runs on target loop and result continuation resumes on caller loop
- synchronous void action runs on target loop and caller task completes on caller loop
- asynchronous value action awaits target-loop work and resumes caller continuation on caller loop
- asynchronous void action follows the same ownership behavior
- same-loop synchronous invocation completes on the current loop without requiring an extra target-loop mailbox round trip
- synchronous exception faults the caller task on caller loop
- asynchronous target task exception faults the caller task on caller loop
- target-side runner exception is forwarded to the caller task instead of being swallowed by target-loop exception isolation
- target task cancellation cancels the caller task on caller loop
- calling without a current caller loop throws with the `RequireCurrentOr()` message shape
- passing null `targetLoop` or null `action` throws `ArgumentNullException` on every overload

Cross-loop tests should drive two loops with existing helpers such as `LoopTestDriver` and `DedicatedLoopThread`, and both loops must be pumped with `Tick()` until the caller task completes. Tests should assert observable thread IDs rather than inspecting private fields.

For same-loop coverage, prefer behavioral assertions such as "synchronous action completes before `InvokeAsync` returns" rather than asserting that `Post` was not called internally.

## Documentation Updates

After implementation:

- Update `Doc/architecture.md` §5.2 from "target, not implemented" to implemented runtime primitive.
- Keep LocalRef, snapshot generation, `LoopRoute`, cancellation, and ownership-transfer notes marked as future work.
- Update `Doc/TODO.txt` to remove the `OrientLoop.InvokeAsync` missing item while leaving `LoopRoute` / dispatcher as pending.

## Acceptance Criteria

- `OrientLoop.InvokeAsync` exposes the four overloads above.
- Cross-loop action execution happens only on the target loop thread.
- Caller task completion happens only on the caller loop thread.
- Exceptions and cancellation propagate back to the caller task.
- Target-side runner failures are forwarded to the caller task and do not rely on target-loop `UnhandledException`.
- No `System.Threading.Tasks.Task` is introduced into the implementation.
- Tests cover success, exception, cancellation, same-loop, and invalid argument behavior.
- Documentation distinguishes runtime scheduling guarantees from business data snapshot guarantees.
