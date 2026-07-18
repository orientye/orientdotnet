# Design: Rename `OrientLoop` → `OrientExecutor`

Date: 2026-07-18  
Status: Approved  
Scope: Naming / API surface only — **no behavior changes**

## Goal

Rename the business single-thread execution unit from `OrientLoop` to `OrientExecutor` so that:

1. The name reflects **serial business execution** (mailbox + timer + `OrientTask` continuations), not a generic “loop”.
2. It is harder to confuse with DotNetty `IEventLoop` / IO event loops.
3. The public API no longer centers on the word `Loop`.

## Non-goals

- No scheduler / timer algorithm changes
- No `OrientTask` rename
- No DotNetty `IEventLoopGroup` rename
- No multi-executor routing (`LoopRoute`) or other feature work
- No long-lived `Obsolete` aliases for `OrientLoop*`

## Naming map

| Old | New |
| --- | --- |
| `OrientLoop` | `OrientExecutor` |
| `OrientLoopHost` | `OrientExecutorHost` |
| `OrientLoopRunner` | `OrientExecutorRunner` |
| `OrientLoopOptions` | `OrientExecutorOptions` |
| `OrientLoopTimer` | `OrientExecutorTimer` |
| `IOrientLoopTimerScheduler` | `IOrientExecutorTimerScheduler` |
| `OrientLoop.Current` | `OrientExecutor.Current` |
| `OrientLoop.InvokeAsync` | `OrientExecutor.InvokeAsync` |
| `OrientLoop.RequireCurrentOr` | `OrientExecutor.RequireCurrentOr` |
| `OrientLoop.ResetDebugThreadBindingForTests` | `OrientExecutor.ResetDebugThreadBindingForTests` |
| `IsInLoopThread` | `IsInExecutorThread` |
| `EnsureInLoopThread` / `EnsureLoopThread` | `EnsureInExecutorThread` / `EnsureExecutorThread` |
| `BindToCurrentThread` | keep |
| `Post` / `Tick` / `WaitForWorkOrTimer` / `ScheduleDelay` / `ScheduleAt` | keep |

### Related public / helper renames

| Old | New |
| --- | --- |
| `CRpcServer.Loop` | `CRpcServer.Executor` |
| `CRpcPushContext.Loop` | `CRpcPushContext.Executor` |
| `TcpChannelHost.OwnerLoop` | `TcpChannelHost.OwnerExecutor` |
| field / param `ownerLoop` | `ownerExecutor` |
| `EnsureOwnerLoopThread` | `EnsureOwnerExecutorThread` |
| `LoopTestDriver` | `ExecutorTestDriver` |
| `DedicatedLoopThread` | `DedicatedExecutorThread` |
| `LoopInboundHandler` | `ExecutorInboundHandler` |
| `CRpcLoopHost` / `CRpcClientLoopHost` | **delete**; callers use `OrientExecutorHost` |

### Directory

- `Orient.Runtime/Loop/` → `Orient.Runtime/Executor/`
- File names follow type names (`OrientExecutor.cs`, `OrientExecutorHost.cs`, …)

### Documentation vocabulary

- “业务 loop / owner loop / loop 线程” → “业务 executor / owner executor / executor 线程”
- Keep contrasting explicitly with DotNetty IO event loops

## Compatibility policy

**Hard cut inside this repository.**

- Do not keep `using OrientLoop = OrientExecutor` or `[Obsolete]` wrappers for `OrientLoop*`.
- Delete RPC thin aliases `CRpcLoopHost` / `CRpcClientLoopHost`.
- Update codegen (`Tool/orient-crpc-plugin`) so generated code uses `context.Executor` / `OrientTask.CompletedTask(context.Executor)`.
- Historical docs under `docs/superpowers/specs/2026-07-05-*` and `plans/2026-07-05-*` may remain as written history; optionally add a one-line note that the type was renamed. They are **not** required to be fully rewritten.

## File-level checklist

### Orient.Runtime

- [ ] Move/rename `Loop/OrientLoop.cs` → `Executor/OrientExecutor.cs`
- [ ] Move/rename `Loop/OrientLoop.InvokeAsync.cs` → `Executor/OrientExecutor.InvokeAsync.cs`
- [ ] Move/rename `Loop/OrientLoopHost.cs` → `Executor/OrientExecutorHost.cs`
- [ ] Move/rename `Loop/OrientLoopRunner.cs` → `Executor/OrientExecutorRunner.cs`
- [ ] Move/rename `Loop/OrientLoopOptions.cs` → `Executor/OrientExecutorOptions.cs`
- [ ] Rename `Timer/OrientLoopTimer.cs` → `OrientExecutorTimer.cs`
- [ ] Rename `Timer/IOrientLoopTimerScheduler.cs` → `IOrientExecutorTimerScheduler.cs`
- [ ] Update `MinHeapTimerScheduler` and any timer references
- [ ] Update `OrientTask*` / `OrientTaskCompletionSource*` / async method builders that mention `OrientLoop` / loop-thread APIs
- [ ] Update exception message strings

### Orient.Rpc

- [ ] `CRpcServer`, `CRpcServerHandler`, `CRpcConnection`, `CRpcConnectionRegistry`
- [ ] `RpcServiceRegistry`
- [ ] `CRpcClient`, `CRpcReferenceBuilder`, `CRpcPushContext`
- [ ] `TcpChannelHost`, `LoopInboundHandler` → `ExecutorInboundHandler`
- [ ] Delete `CRpcLoopHost.cs`, `CRpcClientLoopHost.cs`
- [ ] Pipeline factories / any remaining `OrientLoop` type references

### Tooling / generated samples

- [ ] `Tool/orient-crpc-plugin/.../OrientCrpcGen.cs` (`context.Loop` → `context.Executor`)
- [ ] Regenerated or hand-maintained client stubs that call `context.Loop` (e.g. HelloWorld client)

### TestHelper / Tests / Examples

- [ ] `Orient.TestHelper/LoopTestDriver.cs` → `ExecutorTestDriver.cs`
- [ ] `Orient.TestHelper/OrientTestBase.cs`
- [ ] Rename test files currently named `*Loop*` / `CRpcLoop*` to `*Executor*` where they test the executor
- [ ] `DedicatedLoopThread` → `DedicatedExecutorThread`
- [ ] All test projects + HelloWorld / GateWay examples

### Docs / rules

- [ ] `Doc/architecture.md` (primary)
- [ ] `Doc/protocol.md` if it mentions `OrientLoop`
- [ ] `.cursor/rules/orientdotnet-general.mdc` (still says `CRpcLoop` / `CRpcTask` in places — align to current `OrientExecutor` / `OrientTask`)

## Migration order (single PR, compile checkpoints)

1. **Runtime core**: rename types + move directory; `Orient.Runtime` builds.
2. **Rpc + TestHelper + plugin**: update types/properties; delete host aliases; `Orient.Rpc` builds.
3. **Tests + Examples**: full type/API update; solution builds.
4. **Docs + cursor rules**.
5. **Verify**: `dotnet test` on the solution; ripgrep for leftover public symbols.

Suggested ripgrep acceptance checks (active code + primary docs):

```text
OrientLoop
OrientLoopHost
OrientLoopRunner
OrientLoopOptions
OrientLoopTimer
IOrientLoopTimerScheduler
CRpcLoopHost
CRpcClientLoopHost
IsInLoopThread
EnsureInLoopThread
OwnerLoop
```

Allow hits only in dated historical superpowers specs/plans (optional note) if left untouched.

## Testing / acceptance

- Full solution compile succeeds.
- Existing test suite passes with no intentional behavior changes.
- Examples still start with `new OrientExecutor()` + `OrientExecutorHost` / `OrientExecutorRunner`.
- Architecture doc describes `OrientExecutor` as the business execution unit; no recommendation to use deleted `CRpcLoopHost` aliases.

## Risk notes

- **Large mechanical diff**: prefer rename-first (IDE/symbol rename) then directory move, to keep reviewable history where possible.
- **String assertions**: any tests matching exception text containing `OrientLoop` must update.
- **Codegen drift**: plugin and checked-in generated stubs must stay in sync (`context.Executor`).

## Approved decisions (brainstorming)

- Target name: **`OrientExecutor`** (not `OrientRuntime` / `OrientDispatcher` / `OrientActor*`)
- Scope: **family-wide** public + helper rename
- Compatibility: **hard cut**, delete RPC host aliases
- Behavior: **unchanged**
