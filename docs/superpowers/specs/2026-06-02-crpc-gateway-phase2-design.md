# CRpc Gateway Phase 2 Design — BackendPool

**Status:** Implemented  
**Date:** 2026-06-02  
**Prerequisite:** Phase 1 (`GateWay.Core`, per-inbound `GateWayBackendLink`, push relay)

**Related:** `docs/superpowers/specs/2026-06-01-crpc-gateway-design.md`

---

## Goal

Add **multi-replica routing** for the same `serviceId`: `BackendPool` with round-robin pick across **new inbound connections**, connection-level stickiness to one endpoint, passive health (mark unhealthy on connect/RPC failure), static JSON config, and a two-HelloWorld-port demo.

## Non-Goals (Phase 2)

- Nacos / dynamic service discovery
- `userId` sticky / `SessionRegistry` (Phase 3)
- Per-RPC round-robin on one connection (breaks push)
- Active health probes (TCP/CRPC ping) — deferred to Phase 2.1
- `(serviceId, methodId)` route overrides

---

## Pick Semantics

| When | Behavior |
|------|----------|
| First RPC on inbound connection | `BackendPool.Pick()` → create `GateWayBackendLink` to that endpoint |
| Subsequent RPC on same connection | Reuse same link / same endpoint (sticky) |
| New inbound connection | New pick (round-robin among healthy endpoints) |
| Connect or `CallAsync` failure | `MarkUnhealthy(endpoint)`; reconnect retry on same link may fail; new connections skip dead endpoint |

**Constraint:** Stateless backends only (HelloWorld). Stateful traffic requires Phase 3 stickiness.

---

## Components

| Component | Responsibility |
|-----------|----------------|
| `BackendEndpoint` | `host`, `port`, optional `weight`, runtime `IsHealthy` |
| `BackendPool` | Endpoints for one `serviceId`; `Pick`, `MarkUnhealthy`, `MarkHealthy` (on reconnect success) |
| `BackendPoolRegistry` | `serviceId → BackendPool` |
| `RoundRobinPicker` | Next healthy endpoint |
| `GateWayConfig` + `GateWayConfigLoader` | JSON file; replaces single `BackendHost/Port` |
| `GateWaySessionTable` | Pick endpoint when creating link |
| `GateWayServiceImpl` | On forward failure, mark link endpoint unhealthy |

---

## Config (JSON)

```json
{
  "listenPort": 7000,
  "defaultTimeoutMs": 5000,
  "fallbackServiceId": 0,
  "pools": [
    {
      "serviceId": 1000,
      "pickPolicy": "roundRobin",
      "endpoints": [
        { "host": "127.0.0.1", "port": 7999 },
        { "host": "127.0.0.1", "port": 8001 }
      ]
    }
  ]
}
```

Demo fallback: in-memory two-endpoint pool when config file missing.

---

## Demo

- HelloWorld server accepts `--port` (default 7999)
- Run instances on 7999 and 8001
- Gateway loads `gateway.json`
- Two client connections or reconnects → logs show traffic to both ports

---

## Testing

- Unit: `RoundRobinPicker` skips unhealthy; alternates across picks
- Unit: session table uses picked endpoint on connect
- Manual: dual HelloWorld + Gateway

**Plan:** `docs/superpowers/plans/2026-06-02-crpc-gateway-phase2.md`
