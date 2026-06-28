# CRpc Server Lifecycle Design

**Status:** Approved  
**Date:** 2026-06-27  
**Superseded (registry):** Service registry decisions replaced by `docs/superpowers/specs/2026-06-28-a2-rpc-service-registry-design.md`. `StartAsync` / `StopAsync` / transport-only stop remain in force.  
**Related:** `Doc/architecture.md` §4.1 / §8.4, `Doc/TODO.txt` P0.2, `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md`

---

## Goal

Clarify server endpoint lifecycle and align `CRpcServer` with the three-layer ownership model:

```text
CRpcLoop   = business runtime + service registry (single source of truth)
CRpcServer = transport endpoint (listener, pipeline, Connections)
Host/App   = composition: new loop → RegisterService → Start endpoints → RunUntilCancelled → Stop
```

Remove misleading APIs (`Open` / `Close` / `IRpcServer`) and stop clearing the loop service registry when an endpoint stops.

---

## Non-Goals

- Config validation (P0.3)
- Write backpressure (P0.4)
- `UnifiedServer` lifecycle refactor (application layer; document boundary only)
- `CRpcServer.RegisterService` sugar (optional follow-up)

---

## Decisions

### 1. Service registry stays on `CRpcLoop`

`RegisterService` / `TryGetService` / `UnregisterService` remain loop-only. `CRpcServer` does not own or forward a registry.

Multiple endpoints (CRpc, HTTP, Unified) sharing one loop use the same registry without duplicate registration.

### 2. Canonical host pattern

Production hosts use:

```csharp
var loop = new CRpcLoop();
var server = new CRpcServer(loop, options);

CRpcLoopRunner.RunUntilComplete(loop, async () =>
{
    loop.RegisterService(impl);
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

Reference: `Example/HelloWorld/Server/Program.cs`, `Example/GateWay/GateWayServer/Program.cs`.

### 3. Endpoint lifecycle API

| API | Role |
| --- | --- |
| `StartAsync(CancellationToken)` | Bind listener; must run on owner loop thread |
| `StopAsync()` | Close listener and IO groups; **does not** clear service registry |
| `IsRunning` | `true` after successful bind until `StopInternalAsync` completes |

`StartAsync` throws `InvalidOperationException` if already started.

### 4. Transport restart allowed

After `StopAsync`, `bootstrapChannel` and event loop groups are cleared. A subsequent `StartAsync` is supported. Service registrations on the loop persist across stop/start unless the host explicitly unregisters them.

### 5. Remove `IRpcServer`, `Open()`, `Close()`

No in-repo callers. `Close()` only posted cancel to `runCancellation`, overlapping `StopAsync`. Delete the interface and methods.

### 6. `RunAsync` — demo helper only

`RunAsync` remains for quick console demos: bind, embed `CRpcLoopHost.RunUntilCancelled`, then `StopInternalAsync`.

Changes:

- **Remove** `Loop.ClearRegisteredServices()` on exit.
- Document that hosts needing registry teardown must call `UnregisterService` or host-specific dispose explicitly.
- Do not register console cancel when `registerConsoleCancelHandler: false` (existing behavior).

`RunAsync` must be invoked from a thread that will drive `CRpcLoopHost.RunUntilCancelled` (typically the loop-bound main thread). Do not call it from inside `CRpcLoopRunner.RunUntilComplete` on the same loop (deadlock).

### 7. `ClearRegisteredServices`

Stays `internal` on `CRpcLoop` for tests or explicit host teardown. **Never** called from `CRpcServer` stop paths.

### 8. UnifiedServer boundary

`UnifiedServer` may bind TCP without calling `CRpcServer.StartAsync`. `CRpcServer` still supplies `Loop`, `Options`, `Connections`, and `CRpcServerHandler` context. `CRpcServer.IsRunning` reflects only the CRpc-owned listener, not Unified's bind.

---

## Invariants

1. Service registry and business state: owner loop thread only.
2. `StopAsync` affects transport only.
3. Business hot-swap: `loop.UnregisterService` / `RegisterService`, not server restart.
4. Lifecycle methods (`StartAsync`, `StopAsync`, `RunAsync`): owner loop thread only.

---

## Verification

- Unit: `StopAsync` preserves `TryGetService`; `Stop` → `Start` succeeds; duplicate `StartAsync` throws.
- Existing integration tests (HelloWorld, GateWay, error response) remain green.
- Docs: `Doc/TODO.txt` P0.2 archived; `architecture.md` §8.4 aligned.
