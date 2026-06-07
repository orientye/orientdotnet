# CRpc Gateway Design

**Status:** Approved (Phase 1 scope)  
**Date:** 2026-06-01  
**Scope:** Production-oriented CRpc Gateway: client long connections, transparent `(serviceId, methodId)` forwarding, backend push relay, and phased evolution toward multi-replica routing (policy C).

**Prerequisite:** `Example/GateWay` MVP (fallback `serviceId=0`, `GateWayRouter`, `GateWayServerHandler` via `HandlerFactory`).

**Related:** `Doc/architecture-draft.md` (§5 cross-process `CRpcClient`), `Example/HelloWorld`, `docs/superpowers/specs/2026-05-28-crpc-server-push-design.md`.

---

## Summary

| Track | Owner | Responsibility |
|-------|-------|----------------|
| **Inbound** | Gateway `CRpcServer` | Accept client long connections; dispatch unknown `serviceId` to fallback forwarder |
| **Forward** | `GateWayServiceImpl` | Pick backend link; `CallAsync`; return `(resultCode, body)` so inbound `createResponse` preserves client `reqSeq` |
| **Push relay** | `GateWayPushRelay` | Backend `STATE_PUSH` on outbound client → inbound `CRpcConnection.SendPushAsync` |
| **Phase 2+** | `BackendPool`, `StickyStore` | Multi-replica pick; login后 `userId` 粘滞 |

---

## Goals (Phase 1)

1. **Never leave client RPC hanging** — always write a CRpc response (or documented fire-and-forget exception for malformed frames).
2. **Relay server push** — HelloWorld `ServerNotice` (and generic push) reaches the correct inbound client connection.
3. **Per-inbound outbound link** — each client connection gets its own backend `CRpcClient` so push and backend session semantics stay aligned.
4. **Lifecycle** — disconnect backend clients on inbound disconnect and Gateway shutdown; reconnect on transient backend failure (single retry).
5. **Testability** — extract `GateWay.Core` class library; unit tests in `CRPC.Tests`.

## Non-Goals (Phase 1)

- `BackendPool` with multiple replicas per `serviceId` (Phase 2).
- `userId` session stickiness / `SessionRegistry` (Phase 2).
- External config files (JSON/YAML) — hard-coded or `GateWayOptions` in `Program.cs` is fine.
- Merging `GateWayServerHandler` into `CRpcServerHandler` (Phase 3 optional).
- HTTP endpoint on Gateway.

---

## Current State (MVP)

- `GateWayServerHandler`: fallback to `serviceId=0` when `TryGetService(target)` fails.
- `GateWayServiceImpl`: `router.GetBackend(serviceId)` → single shared `CRpcClient` → `CallAsync(..., 5000)`.
- **Gaps:** silent no-response paths; push dropped; shared backend client breaks multi-client push; no reconnect; fixed timeout.

---

## Phase 1 Architecture

```text
[Client] --CRPC--> GateWayServerHandler
                      |
                      v (fallback serviceId=0)
                 GateWayServiceImpl
                      |
                      v
              GateWaySessionTable
         inbound ConnectionId --> GateWayBackendLink
              { CRpcClient, inbound CRpcConnection }
                      |
                      v
              [HelloWorld backend :7999]
```

**Push path:**

```text
Backend SendPush --> Gateway outbound CRpcClient (per inbound link)
  --> GateWayPushRelay --> inbound CRpcConnection.SendPushAsync
```

**reqSeq:** Unchanged from MVP — inbound handler calls `request.createResponse(code, body)` on the **client request** message.

---

## Routing Policy (full target — Phase 2+)

Policy **C (hybrid):** random/round-robin before login; `userId` sticky after login; optional `(serviceId, methodId)` overrides. Documented here for continuity; **not implemented in Phase 1**.

---

## Error Semantics (Phase 1)

| Condition | `resultCode` | Client behavior |
|-----------|--------------|-----------------|
| No backend route for `serviceId` | `-1` | Generated client returns error tuple |
| Backend not connected (after retry) | `-1` | Same |
| No fallback service registered | `-1` | Response written (fixes hang) |
| Inbound connection not registered | `-1` | Response written (fixes hang) |

---

## Phased Delivery

| Phase | Content |
|-------|---------|
| **1** | P0/P1 fixes, per-inbound backend link, push relay, lifecycle, tests |
| **2** | `BackendPool`, health, config file, demo with 2 HelloWorld ports |
| **3** | `SessionRegistry`, `userId` sticky, method overrides |
| **4** | Optional: CRpc handler dedup, `GatewayCore` rename / top-level package |

**Implementation plan:** `docs/superpowers/plans/2026-06-01-crpc-gateway-phase1.md`

---

## Testing (Phase 1)

- Unit: `GateWayServerHandler` error responses; session create/cleanup; push relay with `EmbeddedChannel`.
- Manual: HelloWorld `:7999` + Gateway `:7000` + `GateWayClient` — 5× `SayHello` + console `server push:` lines.

---

## Repository Rule

Do not create commits unless the user explicitly requests them.
