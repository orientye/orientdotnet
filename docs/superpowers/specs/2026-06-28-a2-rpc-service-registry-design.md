# A2: RPC Service Registry Design

**Status:** Draft (pending review)  
**Date:** 2026-06-28  
**Implementation:** Completed by `docs/superpowers/plans/2026-06-28-a2-runtime-rpc-split.md`.  
**Related:** `Doc/architecture.md` (ServiceRegistry target), `docs/superpowers/specs/2026-06-27-crpc-server-lifecycle-design.md` (supersedes §Goal / §Decisions §1 / §7 / §Invariants registry items), `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md`

**Follow-up spec:** `docs/superpowers/specs/2026-06-28-orient-runtime-rpc-split-design.md` (Runtime/Rpc split + `OrientLoop` / `OrientTask` rename). Combined implementation plan covers both.

---

## Goal

Move the RPC service registry off the business loop and onto the CRpc server endpoint. After A2:

```text
Orient.Runtime (future) = execution only: loop, task, timer, Post, runner
Orient.Rpc (future)     = RPC: registry, server, client, codec, transport
CRpcServer              = transport endpoint + RpcServiceRegistry (Services)
```

Application code registers RPC services on the server, not on the loop:

```csharp
server.Services.Register(impl);   // replaces loop.RegisterService(impl)
```

`Orient.DataManager` and other non-RPC consumers can depend on `Orient.Runtime` without seeing `IRpcService` or registration APIs.

This implements the architecture-draft target: **independent `ServiceRegistry` type**, owned by the RPC layer rather than embedded in `CRpcLoop`.

---

## Non-Goals

- External injection or shared `RpcServiceRegistry` across multiple `CRpcServer` instances (v1)
- Cross-loop service routing or service discovery
- Service middleware, filters, or versioning on the registry
- Automatic `Services.Clear()` on `CRpcServer.StopAsync`
- HTTP handlers uniformly routing through the registry (HelloWorld HTTP may continue direct impl calls)
- `Orient.Runtime` / `Orient.Rpc` csproj split (separate spec; A2 is a prerequisite)
- Renaming `CRpcLoop` → `OrientLoop` (optional follow-up in split spec)

---

## Background

Today `CRpcLoop` holds an inline `Dictionary<ushort, IRpcService>` and exposes:

| API | Location |
| --- | --- |
| `RegisterService` | `CRpcLoop` |
| `TryGetService` | `CRpcLoop` |
| `UnregisterService` | `CRpcLoop` |
| `ClearRegisteredServices` | `CRpcLoop` (internal) |

Dispatch paths call `server.Loop.TryGetService`:

- `CRpcServerHandler`
- `GateWayServerHandler` (including fallback serviceId)

The 2026-06-27 server lifecycle spec intentionally kept the registry on the loop so multiple endpoints sharing one loop could use one registration. A2 revises that decision: **registry belongs to RPC server ownership**, not the execution loop. Non-RPC code on the same loop (e.g. `DataManagerSession`) should not carry RPC registration semantics.

---

## Decisions

### 1. New type: `RpcServiceRegistry` (Orient.Rpc)

Location: `Orient.Rpc.Server` (or equivalent namespace under future `Orient.Rpc` assembly; until split, under `CRpc.Rpc.CRpc.Server`).

```csharp
public sealed class RpcServiceRegistry
{
    public RpcServiceRegistry(CRpcLoop loop);

    public void Register(IRpcService service);
    public bool TryGet(ushort serviceId, [MaybeNullWhen(false)] out IRpcService service);
    public void Unregister(IRpcService service);
    public void Clear();
}
```

**Threading:** All methods call `loop.EnsureInLoopThread()` (same rules as today's `RegisterService`).

**Semantics (unchanged from current loop registry):**

| Operation | Behavior |
| --- | --- |
| `Register` | `ArgumentNullException` if null; overwrites existing entry for same `serviceId` |
| `TryGet` | Returns false if not found |
| `Unregister` | Removes only if the instance is still the registered one for that id |
| `Clear` | Removes all entries |

**Ownership:** Each registry holds a reference to exactly one owner loop. Registry state is accessed only on that loop's thread.

### 2. `CRpcServer` owns registry (Option A — v1)

v1: **no constructor that accepts an external registry.**

```csharp
public sealed class CRpcServer
{
    public CRpcLoop Loop { get; }
    public RpcServiceRegistry Services { get; }

    public CRpcServer(CRpcLoop loop, CRpcServerOptions? options = null)
    {
        Loop = loop;
        Services = new RpcServiceRegistry(loop);
        // ...
    }
}
```

- Property name: **`Services`** (user-facing)
- Type name: **`RpcServiceRegistry`**
- One `CRpcServer` → one registry; register via `server.Services.Register`
- **Non-goal:** two servers on the same loop sharing one registry (register once, serve on two ports). If needed later, add an optional injected-registry constructor without breaking v1 API.

`RpcServiceRegistry` remains **public** so tests and advanced/custom dispatch code can construct and reason about it directly. However, `CRpcServer` v1 does **not** accept an externally-created registry; a standalone `new RpcServiceRegistry(loop)` does not affect `CRpcServer` dispatch unless custom code explicitly uses that registry. Normal hosts use `server.Services`.

### 3. Remove registry from `CRpcLoop`

Delete from `CRpcLoop`:

- `registeredServices` field
- `RegisterService`, `TryGetService`, `UnregisterService`
- `ClearRegisteredServices`

After removal, `CRpcLoop` contains only execution concerns: Post, Tick, timer scheduling, `WaitForWorkOrTimer`, thread binding.

**RPC interfaces move out of Runtime scope:** `IRpcService`, `IRpcContext`, `IRpcMessage` live in Orient.Rpc (today `CRpc.Rpc`), not in Orient.Runtime.

### 4. Handler dispatch uses `server.Services`

**CRpcServerHandler:**

```csharp
// Before
if (server.Loop.TryGetService(serviceId, out var rpcService))

// After
if (server.Services.TryGet(serviceId, out var rpcService))
```

**GateWayServerHandler:** same change for primary and fallback serviceId lookup.

**GreeterHttpHandler:** no change (direct `HelloworldServiceImpl` reference; HTTP demo bypasses registry by design).

### 5. Application migration

| Before | After |
| --- | --- |
| `loop.RegisterService(impl)` | `server.Services.Register(impl)` |
| `loop.TryGetService(id, out s)` | `server.Services.TryGet(id, out s)` |
| `loop.UnregisterService(s)` | `server.Services.Unregister(s)` |
| `loop.ClearRegisteredServices()` | `server.Services.Clear()` |

**Canonical host pattern (replaces lifecycle spec §2):**

```csharp
var loop = new CRpcLoop();
var server = new CRpcServer(loop, options);

CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    server.Services.Register(impl);
    await server.StartAsync(ct);
});

try
{
    CRpcLoopHost.RunUntilCancelled(loop, ct);
}
finally
{
    CRpcLoopRunner.RunUntilComplete(loop, async () => await server.StopAsync());
}
```

Registration still runs on the owner loop thread. Only the object that owns the registry changes.

### 6. Lifecycle interaction

Align with 2026-06-27 server lifecycle spec:

- **`StopAsync`** closes listener and IO groups only; **does not** call `Services.Clear()`.
- **Transport restart:** after `StopAsync`, `StartAsync` may run again; service registrations on `server.Services` persist unless the host explicitly unregisters or clears.
- **`RunAsync` demo helper:** must not clear registry on exit (already required; now applies to `Services.Clear()` instead of `Loop.ClearRegisteredServices()`).

**Business hot-swap:** `server.Services.Unregister` / `Register`, not server restart.

### 7. Multiple endpoints on one loop

| Endpoint | Registry usage |
| --- | --- |
| CRpc binary (`CRpcServerHandler`) | `server.Services.TryGet` |
| GateWay custom handler | same `CRpcServer.Services` |
| HTTP demo (`GreeterHttpHandler`) | direct service impl; no registry |

If two `CRpcServer` instances share one loop in the future, each has its own `Services` registry; hosts register on each server's `Services` separately. Shared registry is explicitly deferred.

---

## Architecture

```text
┌─────────────────────────────────────────────────────────┐
│  Orient.Runtime (future) / CRpc.Async (today)           │
│  CRpcLoop: Post, Tick, Timer, WaitForWorkOrTimer        │
│  CRpcTask, CRpcLoopRunner, CRpcLoopHost                   │
│  (no IRpcService, no registry)                          │
└─────────────────────────────────────────────────────────┘
                          ▲
                          │ owns loop ref
┌─────────────────────────────────────────────────────────┐
│  Orient.Rpc (future) / CRpc.Rpc.* (today)               │
│  RpcServiceRegistry ──► CRpcLoop (EnsureInLoopThread)   │
│  CRpcServer.Services ──► RpcServiceRegistry             │
│  CRpcServerHandler ──► server.Services.TryGet             │
│  IRpcService, IRpcContext, IRpcMessage                    │
└─────────────────────────────────────────────────────────┘
```

**Request dispatch (unchanged flow, different lookup target):**

```text
DotNetty IO thread
  → CRpcServerHandler.ChannelRead
  → server.Loop.Post(...)
  → server.Services.TryGet(serviceId)
  → RpcServiceInvoker.InvokeAsync
  → CRpcTask completes on loop thread
  → response write (fire-and-forget)
```

---

## Error Handling

| Scenario | Behavior |
| --- | --- |
| Register/TryGet/Unregister off loop thread | `InvalidOperationException` |
| Unknown serviceId on dispatch | `CRpcStatusCode.ServiceNotFound` (unchanged) |
| GateWay: neither primary nor fallback registered | existing GateWay error response |
| Register null service | `ArgumentNullException` |

---

## Files to Change

| File | Change |
| --- | --- |
| `CRpc/Async/CRpcLoop.cs` | Remove registry |
| `CRpc/Rpc/CRpc/Server/RpcServiceRegistry.cs` | **New** |
| `CRpc/Rpc/CRpc/Server/CRpcServer.cs` | Add `Services` property |
| `CRpc/Rpc/CRpc/Server/CRpcServerHandler.cs` | `Services.TryGet` |
| `Example/GateWay/GateWay.Core/GateWayServerHandler.cs` | `Services.TryGet` |
| `Example/HelloWorld/Server/Program.cs` | `server.Services.Register` |
| `Example/GateWay/GateWayServer/Program.cs` | `server.Services.Register` |
| `Tests/CRPC.Tests/CRpcLoopRegistryTests.cs` | Rename/migrate → `RpcServiceRegistryTests` |
| `Tests/CRPC.Tests/CRpcServer*.cs`, `GateWayServerHandlerTests.cs`, etc. | Update registration calls |
| `Doc/architecture.md` | Mark ServiceRegistry target as implemented (follow-up doc edit) |

Protobuf plugin and other `TryGetServiceId` references are unrelated (service id from proto options, not loop registry).

---

## Testing

Migrate `CRpcLoopRegistryTests` to `RpcServiceRegistryTests` with equivalent coverage:

1. Register off loop thread throws
2. Register and TryGet on loop thread
3. Unregister removes service
4. Unregister old instance does not remove replacement for same serviceId
5. Post from another thread; register/find on loop thread
6. Clear removes all services

Integration:

- `CRpcServerHandlerTests`, `GateWayServerHandlerTests`, push/error-response tests remain green
- `StopAsync` does not clear `Services`; `TryGet` still succeeds after stop
- Stop → Start → dispatch still works with prior registrations

---

## Invariants

1. RPC service registry state: owner loop thread only (via `RpcServiceRegistry`).
2. `CRpcLoop` has no knowledge of `IRpcService` or service ids.
3. `StopAsync` affects transport only; does not clear `Services`.
4. v1: each `CRpcServer` constructs exactly one `RpcServiceRegistry`; no shared injection.
5. Lifecycle methods on `CRpcServer` remain owner-loop-thread only.

---

## Supersedes

The following from `2026-06-27-crpc-server-lifecycle-design.md` are **replaced** by this spec:

- Goal diagram: "CRpcLoop = business runtime + service registry"
- Decision §1: "Service registry stays on CRpcLoop"
- Decision §7: "`ClearRegisteredServices` stays internal on CRpcLoop"
- Invariants §1 / §3 referencing loop registration APIs

Server lifecycle decisions on `StartAsync` / `StopAsync` / `RunAsync` / transport-only stop **remain in force**.

---

## Verification

- [ ] No remaining references to `loop.RegisterService`, `loop.TryGetService`, `loop.UnregisterService`, `loop.ClearRegisteredServices` in production or test code
- [ ] `RpcServiceRegistryTests` covers migrated scenarios
- [ ] Full `CRPC.Tests` suite passes
- [ ] HelloWorld and GateWay examples build and run
- [ ] `architecture.md` updated to reflect registry on `RpcServiceRegistry` / `CRpcServer.Services`

---

## Open Items (out of scope for A2)

- Orient.Runtime / Orient.Rpc csproj split (separate spec)
- `CRpcLoop` → `OrientLoop` rename
- Optional shared-registry constructor for multi-server same-loop scenarios
- `CRpcServer.RegisterService` sugar delegating to `Services.Register` (optional ergonomics)
