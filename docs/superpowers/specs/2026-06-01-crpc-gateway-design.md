# CRpc Gateway Design

**Status:** Phase 1 implemented (2026-06-01); full production routing in Phase 2+  
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

## Naming: Session Table vs Session Registry

Do not confuse these two concepts:

| Name | Phase | Maps | Purpose |
|------|-------|------|---------|
| **`GateWaySessionTable`** | 1 | `inbound ConnectionId → GateWayBackendLink` | One outbound `CRpcClient` per client TCP connection; push relay target |
| **`SessionRegistry`** | 3 | `userId → inbound CRpcConnection(s)` | Business session; push and sticky routing by user identity |

Phase 1 **does not** implement `SessionRegistry` or `userId` stickiness.

---

## Goals (Phase 1)

1. **Never leave client RPC hanging** — always write a CRpc response on recognized request frames. Malformed / undecodable frames may be dropped at the decoder (same as `CRpcServerHandler` today).
2. **Relay server push** — HelloWorld `ServerNotice` (`serviceId=1000`, `methodId=2`) reaches the correct inbound client connection.
3. **Per-inbound outbound link** — each client connection gets its own backend `CRpcClient` so push and backend session semantics stay aligned.
4. **Lifecycle** — disconnect backend clients on inbound disconnect and Gateway shutdown; reconnect on transient backend failure (single retry).
5. **Testability** — extract `GateWay.Core` class library under `Example/GateWay/GateWay.Core/`; unit tests in `CRPC.Tests`.

## Non-Goals (Phase 1)

- `BackendPool` with multiple replicas per `serviceId` (Phase 2).
- `userId` stickiness / `SessionRegistry` (Phase 3).
- External config files (JSON/YAML) — `GateWayOptions` in `Program.cs` is fine.
- Merging `GateWayServerHandler` into `CRpcServerHandler` (Phase 4 optional).
- HTTP endpoint on Gateway.
- Forwarding request **ext header** or encryption flags — body-only transparent forward in Phase 1.
- Routing arbitrary `serviceId` values — Phase 1 routes **only configured entries** (HelloWorld demo: `1000`).

## Reserved `serviceId`

**`serviceId=0` is reserved** for the internal Gateway fallback forwarder (`GateWayServiceImpl`). Real business services must not register `0` on the Gateway loop. Phase 4 may move fallback to a dedicated reserved id (e.g. `ushort.MaxValue`) if needed.

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
| Backend `ConnectAsync` failed on first use | `-1` | Same |
| Backend not connected (after single retry) | `-1` | Same |
| Backend `CallAsync` timeout / exception | `-1` | Same |
| No fallback service registered | `-1` | Response written (fixes hang) |
| Inbound connection not registered | `-1` | Response written (fixes hang) |

---

## Phased Delivery

| Phase | Content |
|-------|---------|
| **1** | P0/P1 fixes, per-inbound backend link, push relay, lifecycle, tests |
| **2** | `BackendPool`, health, config file, **stateless** demo with 2 HelloWorld ports (round-robin). **Implemented** — see `2026-06-02-crpc-gateway-phase2-design.md`. **Not** for stateful game traffic without Phase 3 stickiness. |
| **3** | `SessionRegistry`, `userId` sticky, `(serviceId, methodId)` overrides (policy C) |
| **4** | Optional: CRpc handler dedup; promote `GateWay.Core` to top-level package if needed |

**Implementation plan:** `docs/superpowers/plans/2026-06-01-crpc-gateway-phase1.md`

---

## Testing (Phase 1)

- Unit: `GateWayServerHandler` error responses (`CrpcTestBase` + `EmbeddedChannel`); session create/cleanup with injectable backend client factory.
- Optional unit: `GateWayPushRelay` with test double inbound connection.
- Manual: HelloWorld `:7999` + Gateway `:7000` + `GateWayClient` — 5× `SayHello` + console `server push:` lines.

---

## Repository Rule

Do not create commits unless the user explicitly requests them.
