# OrientLoop → OrientExecutor Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mechanically rename `OrientLoop` and its family to `OrientExecutor` across Runtime, Rpc, tools, tests, examples, docs, and rules — with **zero behavior changes**.

**Architecture:** Pure rename. Keep mailbox / timer / `Tick` / `WaitForWorkOrTimer` / `OrientTask` semantics identical. Hard-cut: no `Obsolete` aliases; delete `CRpcLoopHost` / `CRpcClientLoopHost`. Existing tests are the regression suite (not new feature TDD).

**Tech Stack:** C# / .NET, `orient-dotnet.sln`, xUnit, DotNetty (IO names unchanged)

**Spec:** `docs/superpowers/specs/2026-07-18-orientloop-to-orientexecutor-rename-design.md`

**Commit policy:** This repo does **not** auto-commit. Skip every “Commit” step unless the user explicitly asks to commit.

---

## File structure (rename map)

### Orient.Runtime — move `Loop/` → `Executor/`

| From | To |
| --- | --- |
| `Orient.Runtime/Loop/OrientLoop.cs` | `Orient.Runtime/Executor/OrientExecutor.cs` |
| `Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs` | `Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs` |
| `Orient.Runtime/Loop/OrientLoopHost.cs` | `Orient.Runtime/Executor/OrientExecutorHost.cs` |
| `Orient.Runtime/Loop/OrientLoopRunner.cs` | `Orient.Runtime/Executor/OrientExecutorRunner.cs` |
| `Orient.Runtime/Loop/OrientLoopOptions.cs` | `Orient.Runtime/Executor/OrientExecutorOptions.cs` |
| `Orient.Runtime/Timer/OrientLoopTimer.cs` | `Orient.Runtime/Timer/OrientExecutorTimer.cs` |
| `Orient.Runtime/Timer/IOrientLoopTimerScheduler.cs` | `Orient.Runtime/Timer/IOrientExecutorTimerScheduler.cs` |

Also update in place (no move): `MinHeapTimerScheduler.cs`, `OrientTask*.cs`, `OrientTaskCompletionSource.cs`, `OrientAsyncMethodBuilder*.cs`.

### Orient.Rpc / TestHelper / Tool

| From | To / Action |
| --- | --- |
| `Orient.Rpc/Transport/LoopInboundHandler.cs` | `ExecutorInboundHandler.cs` |
| `Orient.Rpc/Server/CRpcLoopHost.cs` | **delete** |
| `Orient.Rpc/Client/CRpcClientLoopHost.cs` | **delete** |
| `Orient.TestHelper/LoopTestDriver.cs` | `ExecutorTestDriver.cs` |
| `CRpcServer.Loop` / `CRpcPushContext.Loop` | `.Executor` |
| `TcpChannelHost.OwnerLoop` | `.OwnerExecutor` |
| `CRpcClient.PendingCall.Loop` | `.Executor` |
| `PumpAwaitableOnOwnerLoop` | `PumpAwaitableOnOwnerExecutor` |
| `boundLoopOnThread` | `boundExecutorOnThread` |
| `Tool/orient-crpc-plugin/.../OrientCrpcGen.cs` | emit `context.Executor` |

### Tests / Examples / Docs

- Rename test helpers/files: `DedicatedLoopThread` → `DedicatedExecutorThread`; `OrientLoop*Tests` / `CRpcLoop*Tests` / `LoopInboundHandlerTests` → `*Executor*` names
- Update HelloWorld / GateWay examples
- Update `Example/HelloWorld/Server/Http/*` and `Example/GateWay/GateWay.Core/*`, not only their `Program.cs` entry points
- Update `Doc/architecture.md`, `Doc/protocol.md`, `.cursor/rules/orientdotnet-general.mdc`
- Leave dated `docs/superpowers/specs/2026-07-05-*` and `plans/2026-07-05-*` as history (optional one-line note only)

---

### Task 1: Rename Orient.Runtime core types + directory

**Files:**
- Move/rename all files under `Orient.Runtime/Loop/` → `Orient.Runtime/Executor/` (table above)
- Rename timer types under `Orient.Runtime/Timer/`
- Modify: `Orient.Runtime/Timer/MinHeapTimerScheduler.cs`
- Modify: `Orient.Runtime/Task/OrientTask.cs`, `OrientTask.Generic.cs`, `OrientTaskCompletionSource.cs`, `OrientAsyncMethodBuilder.cs`, `OrientAsyncMethodBuilder.Generic.cs`

- [ ] **Step 1: Rename types and members in Runtime (symbol rename)**

Apply this mapping everywhere in `Orient.Runtime` (types, methods, messages, comments):

```text
OrientLoop                         → OrientExecutor
OrientLoopHost                     → OrientExecutorHost
OrientLoopRunner                   → OrientExecutorRunner
OrientLoopOptions                  → OrientExecutorOptions
OrientLoopTimer                    → OrientExecutorTimer
IOrientLoopTimerScheduler          → IOrientExecutorTimerScheduler
IsInLoopThread                     → IsInExecutorThread
EnsureInLoopThread                 → EnsureInExecutorThread
EnsureLoopThread                   → EnsureExecutorThread
boundLoopOnThread                  → boundExecutorOnThread
```

Keep method names: `BindToCurrentThread`, `Post`, `Tick`, `WaitForWorkOrTimer`, `ScheduleDelay`, `ScheduleAt`, `InvokeAsync`, `Current`, `RequireCurrentOr`.

Example after rename (`OrientExecutor.cs` excerpt):

```csharp
public sealed partial class OrientExecutor
{
    [ThreadStatic]
    private static OrientExecutor? current;

    public static OrientExecutor? Current => current;

    public bool IsInExecutorThread => threadId != 0 && Environment.CurrentManagedThreadId == threadId;

    public void Post(Action action) { /* unchanged body */ }
    public void Tick(int maxActions = 1024) { /* unchanged body */ }
}
```

Exception strings must change, e.g.:

```csharp
throw new InvalidOperationException(
    "OrientTaskCompletionSource must be used from its OrientExecutor executor thread.");
```

- [ ] **Step 2: Move files to `Orient.Runtime/Executor/` and rename files**

On Windows PowerShell from repo root:

```powershell
New-Item -ItemType Directory -Force -Path Orient.Runtime/Executor | Out-Null
git mv Orient.Runtime/Loop/OrientLoop.cs Orient.Runtime/Executor/OrientExecutor.cs
git mv Orient.Runtime/Loop/OrientLoop.InvokeAsync.cs Orient.Runtime/Executor/OrientExecutor.InvokeAsync.cs
git mv Orient.Runtime/Loop/OrientLoopHost.cs Orient.Runtime/Executor/OrientExecutorHost.cs
git mv Orient.Runtime/Loop/OrientLoopRunner.cs Orient.Runtime/Executor/OrientExecutorRunner.cs
git mv Orient.Runtime/Loop/OrientLoopOptions.cs Orient.Runtime/Executor/OrientExecutorOptions.cs
git mv Orient.Runtime/Timer/OrientLoopTimer.cs Orient.Runtime/Timer/OrientExecutorTimer.cs
git mv Orient.Runtime/Timer/IOrientLoopTimerScheduler.cs Orient.Runtime/Timer/IOrientExecutorTimerScheduler.cs
Remove-Item -Recurse -Force Orient.Runtime/Loop -ErrorAction SilentlyContinue
```

If `git mv` fails because content already edited, use normal move then `git add`.

- [ ] **Step 3: Build Runtime only**

Run:

```powershell
dotnet build Orient.Runtime/Orient.Runtime.csproj
```

Expected: **FAIL** is OK if project still referenced elsewhere later; for Runtime alone it should **PASS** (Runtime has no Orient.Rpc dependency). If FAIL, fix remaining `OrientLoop*` symbols inside Runtime only.

- [ ] **Step 4: Commit (only if user asks)**

```bash
git add Orient.Runtime
git commit -m "$(cat <<'EOF'
refactor: rename OrientLoop to OrientExecutor in Runtime

EOF
)"
```

---

### Task 2: Update Orient.Rpc + delete host aliases

**Files:**
- Modify: `Orient.Rpc/Server/CRpcServer.cs` (`Loop` → `Executor`, ctor param, `EnsureOwnerExecutorThread`)
- Modify: `Orient.Rpc/Server/CRpcServerHandler.cs` (`server.Loop` → `server.Executor`)
- Modify: `Orient.Rpc/Server/CRpcConnection.cs`, `CRpcConnectionRegistry.cs`, `RpcServiceRegistry.cs`
- Modify: `Orient.Rpc/Client/CRpcClient.cs`, `CRpcReferenceBuilder.cs`, `CRpcPushContext.cs`
- Modify: `Orient.Rpc/Transport/TcpChannelHost.cs` (`ownerLoop` → `ownerExecutor`, `OwnerLoop` → `OwnerExecutor`)
- Rename: `Orient.Rpc/Transport/LoopInboundHandler.cs` → `ExecutorInboundHandler.cs`
- Modify: `Orient.Rpc/Client/CRpcClientPipelineFactory.cs` (construct `ExecutorInboundHandler`)
- Delete: `Orient.Rpc/Server/CRpcLoopHost.cs`, `Orient.Rpc/Client/CRpcClientLoopHost.cs`
- Modify: any other Rpc file still mentioning `OrientLoop` (grep)

- [ ] **Step 1: Apply property/field renames in Rpc**

```csharp
// CRpcServer
public CRpcServer(OrientExecutor executor, CRpcServerOptions? options = null)
{
    Executor = executor;
    // ...
}
public OrientExecutor Executor { get; }

// CRpcPushContext
public OrientExecutor Executor { get; }

// TcpChannelHost
private readonly OrientExecutor ownerExecutor;
public OrientExecutor OwnerExecutor => ownerExecutor;
```

Replace call sites:

```csharp
server.Executor.Post(() => { /* ... */ });
ownerExecutor.Post(() => { /* ... */ });
```

- Rename `CRpcClient.PendingCall.Loop` → `Executor`.
- Rename `PumpAwaitableOnOwnerLoop` → `PumpAwaitableOnOwnerExecutor`.

- [ ] **Step 2: Rename inbound handler**

```csharp
namespace Orient.Rpc.Transport;

public sealed class ExecutorInboundHandler : ChannelHandlerAdapter
{
    private readonly TcpChannelHost host;

    public ExecutorInboundHandler(TcpChannelHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
    }
    // ChannelRead / ChannelInactive / ExceptionCaught unchanged
}
```

```csharp
// CRpcClientPipelineFactory
pipeline.AddLast("handler", new ExecutorInboundHandler(host));
```

- [ ] **Step 3: Delete RPC host aliases**

Delete files:

- `Orient.Rpc/Server/CRpcLoopHost.cs`
- `Orient.Rpc/Client/CRpcClientLoopHost.cs`

Ensure no remaining references (they should already be unused or only docs/examples — fix in Task 4 if needed for build).

- [ ] **Step 4: Build Rpc**

```powershell
dotnet build Orient.Rpc/Orient.Rpc.csproj
```

Expected: **PASS**. If FAIL, fix remaining `OrientLoop` / `OwnerLoop` / `CRpcLoopHost` references in Rpc only.

- [ ] **Step 5: Commit (only if user asks)**

```bash
git add Orient.Rpc
git commit -m "$(cat <<'EOF'
refactor: rename OrientLoop usages to OrientExecutor in Rpc

EOF
)"
```

---

### Task 3: Update TestHelper + codegen plugin

**Files:**
- Rename: `Orient.TestHelper/LoopTestDriver.cs` → `ExecutorTestDriver.cs`
- Modify: `Orient.TestHelper/OrientTestBase.cs`
- Modify: `Tool/orient-crpc-plugin/OrientCrpcPlugin/OrientCrpcGen.cs`

- [ ] **Step 1: Rename test driver**

```csharp
/// <summary>
/// Runs a single <see cref="OrientExecutor"/> on a dedicated background thread.
/// </summary>
public sealed class ExecutorTestDriver : IDisposable
{
    private readonly OrientExecutor executor;

    public ExecutorTestDriver(OrientExecutor executor)
    {
        // same body; thread name can be "ExecutorTestDriver"
    }
}
```

`OrientTestBase` should call:

```csharp
OrientExecutor.ResetDebugThreadBindingForTests();
```

- [ ] **Step 2: Update codegen**

In `OrientCrpcGen.cs`, change emitted stub line from:

```csharp
return OrientTask.CompletedTask(context.Loop);
```

to:

```csharp
return OrientTask.CompletedTask(context.Executor);
```

- [ ] **Step 3: Build helper + plugin**

```powershell
dotnet build Orient.TestHelper/Orient.TestHelper.csproj
dotnet build Tool/orient-crpc-plugin/OrientCrpcPlugin/OrientCrpcPlugin.csproj
```

Expected: **PASS**.

- [ ] **Step 4: Commit (only if user asks)**

```bash
git add Orient.TestHelper Tool/orient-crpc-plugin
git commit -m "$(cat <<'EOF'
refactor: rename loop test helpers and codegen to OrientExecutor

EOF
)"
```

---

### Task 4: Update tests and examples (mechanical)

**Files:**
- Modify/rename all under `Tests/Orient.Tests/` that reference old names
- Rename helper: `Tests/Orient.Tests/DedicatedLoopThread.cs` → `DedicatedExecutorThread.cs`
- Rename test files for clarity, e.g.:
  - `OrientLoopTests.cs` → `OrientExecutorTests.cs`
  - `OrientLoopInvokeAsyncTests.cs` → `OrientExecutorInvokeAsyncTests.cs`
  - `OrientLoopThreadBindingTests.cs` → `OrientExecutorThreadBindingTests.cs`
  - `CRpcLoopWakeupTests.cs` → `OrientExecutorWakeupTests.cs` (class already `OrientLoopWakeupTests` → `OrientExecutorWakeupTests`)
  - `CRpcLoopTickOrderTests.cs` → `OrientExecutorTickOrderTests.cs`
  - `CRpcLoopRunnerTests.cs` → `OrientExecutorRunnerTests.cs`
  - `CRpcLoopExceptionIsolationTests.cs` → `OrientExecutorExceptionIsolationTests.cs`
  - `Transport/LoopInboundHandlerTests.cs` → `ExecutorInboundHandlerTests.cs`
- Modify examples:
  - `Example/HelloWorld/Server/Program.cs` (+ Http helpers if needed)
  - `Example/HelloWorld/Server/Http/UnifiedServer.cs`, `PortUnificationHandler.cs`, `HttpListenServer.cs`, `GreeterHttpHandler.cs`
  - `Example/HelloWorld/Client/Program.cs`, `HelloworldClient.cs`, `GreeterClient.cs`
  - `Example/GateWay/GateWayServer/Program.cs`
  - `Example/GateWay/GateWay.Core/GateWayServerHandler.cs`, `GateWayRouter.cs`, `GateWaySessionTable.cs`, `IBackendClientFactory.cs`
  - `Example/GateWay/Client/Program.cs`

- [ ] **Step 1: Global replace patterns in tests/examples**

Apply consistently:

```text
OrientLoop              → OrientExecutor
OrientLoopHost          → OrientExecutorHost
OrientLoopRunner        → OrientExecutorRunner
OrientLoopOptions       → OrientExecutorOptions
LoopTestDriver          → ExecutorTestDriver
DedicatedLoopThread     → DedicatedExecutorThread
LoopInboundHandler      → ExecutorInboundHandler
CRpcLoopHost            → OrientExecutorHost   (then prefer direct OrientExecutorHost)
CRpcClientLoopHost      → OrientExecutorHost
.IsInLoopThread         → .IsInExecutorThread
EnsureInLoopThread      → EnsureInExecutorThread
.OwnerLoop              → .OwnerExecutor
server.Loop             → server.Executor
context.Loop            → context.Executor
EnsureOwnerLoopThread   → EnsureOwnerExecutorThread
DrainOwnerLoop          → DrainOwnerExecutor   (test helper method names)
PumpAwaitableOnOwnerLoop → PumpAwaitableOnOwnerExecutor
boundLoopOnThread       → boundExecutorOnThread
```

Local variables named `loop` may stay or become `executor`; prefer `executor` at construction sites for readability:

```csharp
var executor = new OrientExecutor();
var server = new CRpcServer(executor);
OrientExecutorRunner.RunUntilComplete(executor, async () => { /* ... */ });
OrientExecutorHost.RunUntilCancelled(executor, cts.Token);
```

- [ ] **Step 2: Update exception-message assertions**

When implementation exception text changes, update exact or substring assertions in:

```text
Tests/Orient.Tests/OrientLoopInvokeAsyncTests.cs
Tests/Orient.Tests/OrientLoopTests.cs
Tests/Orient.Tests/OrientLoopThreadBindingTests.cs
Tests/Orient.Tests/CRpcClientTests.cs
Tests/Orient.Tests/CRpcServerTests.cs
Tests/Orient.Tests/CRpcConnectionTests.cs
Tests/Orient.Tests/Transport/TcpChannelHostTests.cs
```

Apply these message-only replacements without changing tested behavior:

```text
OrientLoop       → OrientExecutor
owner OrientLoop → owner OrientExecutor
loop thread      → executor thread
```

- [ ] **Step 3: Build full solution**

```powershell
dotnet build orient-dotnet.sln
```

Expected: **PASS** with 0 errors. Fix any leftover compile breaks before continuing.

- [ ] **Step 4: Commit (only if user asks)**

```bash
git add Tests Example
git commit -m "$(cat <<'EOF'
refactor: update tests and examples for OrientExecutor rename

EOF
)"
```

---

### Task 5: Docs + cursor rules

**Files:**
- Modify: `Doc/architecture.md` (primary; replace OrientLoop vocabulary)
- Modify: `Doc/protocol.md`
- Modify: `.cursor/rules/orientdotnet-general.mdc` (replace stale `CRpcLoop` / `CRpcTask` with `OrientExecutor` / `OrientTask`)
- Optional note only: `docs/superpowers/specs/2026-07-05-orientloop-invokeasync-design.md`

- [ ] **Step 1: Update architecture + protocol**

In `Doc/architecture.md` and `Doc/protocol.md`:

```text
OrientLoop → OrientExecutor
OrientLoopHost → OrientExecutorHost
OrientLoopRunner → OrientExecutorRunner
CRpcServer.Loop → CRpcServer.Executor
owner loop → owner executor
业务 loop → 业务 executor
Loop.Post → Executor.Post
LoopRoute → ExecutorRoute
```

Remove documentation of `CRpcLoopHost` / `CRpcClientLoopHost` as recommended aliases; point only to `OrientExecutorHost`.

`LoopRoute` → `ExecutorRoute` is documentation-only future terminology; do not implement the routing feature.

Header example:

```markdown
> **范围**：线程、`OrientExecutor`（原 `OrientLoop` / `CRpcLoop`）、`CRpcServer` / ...
```

- [ ] **Step 2: Update `.cursor/rules/orientdotnet-general.mdc`**

Replace business-loop rules to use current names, e.g.:

```markdown
- `OrientExecutor` is the business-thread runtime. Business state, pending RPC calls, Orient timers, and `OrientTask` completion must run on the owning `OrientExecutor` thread.
- DotNetty IO threads must not execute business logic directly. They may only enqueue work into the business `OrientExecutor`.
- `OrientExecutor.Post` is the boundary for external thread ingress into the business executor.
```

Keep the “do not use `System.Threading.Tasks.Task` for project async APIs” rules; only fix outdated type names (`CRpcLoop` → `OrientExecutor`, `CRpcTask` → `OrientTask` where those appear).

- [ ] **Step 3: Commit (only if user asks)**

```bash
git add Doc .cursor/rules docs/superpowers/specs/2026-07-18-orientloop-to-orientexecutor-rename-design.md
git commit -m "$(cat <<'EOF'
docs: rename OrientLoop to OrientExecutor in architecture and rules

EOF
)"
```

---

### Task 6: Verification gate

**Files:** none (verification only)

- [ ] **Step 1: Run full test suite**

```powershell
dotnet test orient-dotnet.sln --no-build
```

If needed rebuild first:

```powershell
dotnet test orient-dotnet.sln
```

Expected: **all tests PASS**.

- [ ] **Step 2: Ripgrep leftover public old names (active tree)**

```powershell
rg -n "OrientLoop|OrientLoopHost|OrientLoopRunner|OrientLoopOptions|OrientLoopTimer|IOrientLoopTimerScheduler|CRpcLoopHost|CRpcClientLoopHost|IsInLoopThread|EnsureInLoopThread|OwnerLoop|LoopTestDriver|DedicatedLoopThread|LoopInboundHandler|server\.Loop|context\.Loop|PumpAwaitableOnOwnerLoop|EnsureOwnerLoopThread|boundLoopOnThread|LoopRoute" --glob '!docs/superpowers/specs/2026-07-05-*' --glob '!docs/superpowers/plans/2026-07-05-*'
```

Expected: **no hits** in Runtime/Rpc/Tests/Examples/Doc/rules (except intentional “原 OrientLoop” historical mentions in the new design/plan/architecture header if kept).

Allowed:

- Historical `2026-07-05-*` superpowers docs (untouched)
- Phrases like “原 `OrientLoop`” in the 2026-07-18 design/plan or architecture intro
- DotNetty identifiers such as `IEventLoopGroup` / `MultithreadEventLoopGroup`

- [ ] **Step 3: Mark design status**

Set in `docs/superpowers/specs/2026-07-18-orientloop-to-orientexecutor-rename-design.md`:

```markdown
Status: Implemented
```

- [ ] **Step 4: Final commit (only if user asks)**

```bash
git add -A
git commit -m "$(cat <<'EOF'
refactor: complete OrientLoop to OrientExecutor rename

EOF
)"
```

---

## Spec coverage check

| Spec requirement | Task |
| --- | --- |
| Rename core `OrientLoop*` family | Task 1 |
| Move `Loop/` → `Executor/` | Task 1 |
| Rename timer types | Task 1 |
| Update Rpc properties / owner fields | Task 2 |
| Rename `LoopInboundHandler` | Task 2 |
| Delete `CRpcLoopHost` / `CRpcClientLoopHost` | Task 2 |
| TestHelper + codegen | Task 3 |
| Tests + examples | Task 4 |
| Docs + cursor rules | Task 5 |
| Hard cut / no Obsolete aliases | Tasks 1–2 (no alias code added) |
| Behavior unchanged | Tasks 1–6 (no logic edits; Task 6 tests) |
| Ripgrep acceptance | Task 6 |

## Self-review notes

- No behavior/feature work included.
- Commit steps are optional per repo policy.
- Historical 2026-07-05 docs intentionally out of scope for full rewrite.
