# CRpc Heartbeat v1 Design — Client-Active Keepalive

**Status:** Draft  
**Date:** 2026-06-21  
**Prerequisite:** Binary codec (`CRpcMessage`), `TcpChannelHost`, `CRpcClientPipelineFactory`, `CRpcServer`

**Related:** `Doc/protocol.md`, `docs/superpowers/specs/2026-05-27-crpc-client-tcp-channel-host-migration-design.md`, `docs/superpowers/specs/2026-06-01-crpc-gateway-design.md`

---

## Goal

Close the half-implemented keepalive gap:

- **Client** sends application-layer `Heartbeat` frames on a fixed interval (default **15s**).
- **Server** does **not** reply (no ack). Any inbound data resets a read-idle timer; **45s** (default, 3× interval) with no inbound → close connection and release registry/session resources.
- Apply consistently across **CRpc.dll**, HelloWorld **UnifiedServer**, and **Gateway** backend outbound links.

Fixes zombie TCP connections (client crash, network partition without FIN/RST) and keeps NAT/firewall paths alive on long-lived links.

---

## Non-Goals (v1)

- Server `HeartbeatAck` / pong response
- Client ack timeout or read-idle “server is dead” detection (TCP half-open to dead server remains slow until `ChannelInactive` or next `CallAsync`)
- Generic `CRpcClient` auto-reconnect (Gateway keeps reconnect at its layer)
- `HeartbeatAck` wire type implementation (reserved for Phase 2 only)
- TCP `SO_KEEPALIVE` tuning
- Metrics / structured logging for heartbeat events

---

## Heartbeat Model (v1)

| Role | Behavior |
|------|----------|
| **Initiator** | Client only |
| **Server response** | None — inbound heartbeat only resets read idle |
| **Send policy** | Fixed interval via **writer idle** (fires even while waiting for RPC response) |
| **Client disconnect discovery** | `ChannelInactive`, `ChannelExceptionCaught`, or next `CallAsync` failure |
| **Client after disconnect** | `FailPendingCalls`; app or Gateway reconnects explicitly |
| **Server disconnect trigger** | Read idle: no inbound bytes for `ReadIdleSeconds` |

Traditional bidirectional probe/ack (client waits for server reply) is **deferred to Phase 2**.

---

## Protocol

### Message type

Extend `CRpcMessageType`:

| Value | Name | v1 use |
|-------|------|--------|
| 0 | Request | unchanged |
| 1 | Response | unchanged |
| 2 | Push | unchanged |
| 3 | **Heartbeat** | client → server |
| 4 | HeartbeatAck | **reserved**, not sent or accepted in v1 |

Update `CRpcMessageHeader.ReadFrom` upper bound from `Push` to allow `Heartbeat`. Reject unknown types above `Heartbeat`.

Update `Doc/protocol.md` message type table and conventions.

### Heartbeat frame fields

| Field | Value |
|-------|-------|
| `version` | 1 |
| `messageType` | 3 (Heartbeat) |
| `flags` | 0 |
| `reserved` | 0 |
| `serviceId` | 0 |
| `methodId` | 0 |
| `reqSeq` | 0 |
| `resultCode` | 0 |
| `body` | empty (`bodyOriginLen = 0`) |

Add `CRpcMessage.CreateHeartbeat()` factory in codec layer.

### Server handling

On decode success:

1. If `MessageType == Heartbeat` → **return immediately** (do not `Post` to `CRpcLoop`, do not dispatch to `IRpcService`).
2. Read idle is already reset when bytes arrive on the socket (before business logic).

Applies to:

- `CRpcServerHandler`
- `GateWayServerHandler`

### Client handling

If a Heartbeat frame is received inbound (should not happen in v1): ignore in `CRpcClient.CompleteReceiveResponse` (no pending completion, no push dispatch).

---

## Configuration

### `CRpcClientOptions`

| Property | Default | Description |
|----------|---------|-------------|
| `HeartbeatEnabled` | `true` | When false, omit idle + heartbeat handlers from client pipeline |
| `HeartbeatIntervalSeconds` | **15** | Writer-idle interval; replaces `HeartbeatIdleSeconds` |

Remove `HeartbeatIdleSeconds` (rename only; no long-term alias unless tests require migration shim).

### `CRpcServerOptions`

| Property | Default | Description |
|----------|---------|-------------|
| `HeartbeatEnabled` | `true` | When false, omit server read-idle handlers |
| `ReadIdleSeconds` | **45** | Reader-idle close threshold (= 3 × default client interval) |

### Validation

On options construction or first use:

```text
HeartbeatIntervalSeconds > 0
ReadIdleSeconds > 0
ReadIdleSeconds >= HeartbeatIntervalSeconds * 2
```

Deployments may use looser ratios (e.g. 15 / 90) as long as validation passes.

---

## Pipeline Architecture

### Client (`CRpcClientPipelineFactory`)

When `HeartbeatEnabled`:

```text
IdleStateHandler(0, HeartbeatIntervalSeconds, 0)   // writer idle
→ CRpcClientHeartbeatHandler
→ CRpcMessageDecoder
→ CRpcMessageEncoder
→ LoopInboundHandler
```

Replace current `IdleStateHandler(0, 0, HeartbeatIdleSeconds)` (all-idle, unused).

**`CRpcClientHeartbeatHandler`:**

- `UserEventTriggered`: on `IdleState.WriterIdle`, `WriteAndFlush` `CRpcMessage.CreateHeartbeat()` (fire-and-forget on IO thread).
- Do not `Post` to owner loop for send.

When `HeartbeatEnabled == false`: decoder → encoder → `LoopInboundHandler` only.

### Server (`CRpcServerPipelineFactory`)

New factory, used by `CRpcServer` and HelloWorld `UnifiedServer` CRpc branch.

When `HeartbeatEnabled`:

```text
IdleStateHandler(ReadIdleSeconds, 0, 0)   // reader idle
→ CRpcServerReadIdleHandler
→ CRpcMessageDecoder
→ CRpcMessageEncoder
→ app handler (CRpcServerHandler / GateWayServerHandler / …)
```

**`CRpcServerReadIdleHandler`:**

- `UserEventTriggered`: on `IdleState.ReaderIdle`, `ctx.CloseAsync()` (IO thread).
- `ChannelInactive` on app handler performs `Unregister` / Gateway session cleanup.

When `HeartbeatEnabled == false`: decoder → encoder → app handler only.

### `CRpcServer.cs`

Replace inline `ChildHandler` pipeline wiring with `CRpcServerPipelineFactory.Configure(pipeline, options, handler)`.

### `UnifiedServer.cs`

CRpc branch after port unification must call the same `CRpcServerPipelineFactory` (not duplicate decoder/encoder/handler list).

---

## Disconnect Detection and Handling

### `CRpcClient` (all clients, including Gateway backend)

| Event | Action |
|-------|--------|
| `ChannelInactive` | `FailPendingCalls(ConnectionClosedException)`; raise **`ConnectionLost`** |
| `ChannelExceptionCaught` | Same with wrapped exception |
| `CallAsync` write failure | Fail that call / pending set per existing rules |

**`ConnectionLost`:** new public event on `CRpcClient`, invoked on owner loop when current channel becomes inactive (thin wrapper over `TcpChannelHost.ChannelBecameInactive`).

**No** built-in reconnect in `CRpcClient`.

### Gateway inbound (client → Gateway)

Uses `CRpcServer` pipeline → read idle closes silent clients.

Existing `GateWayServerHandler.ChannelInactive`:

- `Connections.Unregister`
- `sessions.RemoveAsync` → `GateWayBackendLink.DisposeAsync`

### Gateway outbound (Gateway → backend)

Backend `CRpcClient` uses standard client pipeline (heartbeat enabled by default).

On backend **`ConnectionLost`** (in `GateWaySessionTable` when link is created):

1. `pool.MarkUnhealthy(link.Endpoint)`
2. `links.Remove(inboundConnectionId)` (immediate remove, not stale flag)

Retain existing `GateWayServiceImpl` RPC-failure path: `ReconnectAsync` on “not connected” `InvalidOperationException`.

Next RPC on that inbound calls `GetOrCreateAsync` → new pick + connect.

---

## Components (new / modified)

| Component | Location | Change |
|-----------|----------|--------|
| `CRpcMessageType` | Codec | Add `Heartbeat = 3`; document `HeartbeatAck = 4` reserved |
| `CRpcMessage` | Codec | `CreateHeartbeat()` |
| `CRpcMessageHeader` | Codec | Allow type 3 in validation |
| `CRpcClientOptions` | Client | `HeartbeatEnabled`, `HeartbeatIntervalSeconds` (default 15) |
| `CRpcServerOptions` | Server | `HeartbeatEnabled`, `ReadIdleSeconds` (default 45) |
| `CRpcClientHeartbeatHandler` | Client | Writer-idle send |
| `CRpcServerReadIdleHandler` | Server | Reader-idle close |
| `CRpcServerPipelineFactory` | Server | Shared server pipeline |
| `CRpcClientPipelineFactory` | Client | Fix idle mode + wire heartbeat handler |
| `CRpcClient` | Client | `ConnectionLost` event; ignore inbound Heartbeat |
| `CRpcServer` | Server | Use `CRpcServerPipelineFactory` |
| `CRpcServerHandler` | Server | Short-circuit Heartbeat |
| `GateWayServerHandler` | GateWay | Short-circuit Heartbeat |
| `GateWaySessionTable` | GateWay | Subscribe backend `ConnectionLost` |
| `UnifiedServer` | Example | Use `CRpcServerPipelineFactory` |
| `Doc/protocol.md` | Doc | Heartbeat type + conventions |

---

## Error Handling

| Situation | Behavior |
|-----------|----------|
| Heartbeat write fails synchronously | Log; await `ChannelInactive` or next RPC |
| Server read idle fires | `CloseAsync`; normal cleanup chain |
| Malformed Heartbeat frame | Existing decode error → close connection |
| Invalid options (interval / read idle) | Throw at startup |
| `HeartbeatEnabled = false` | No idle handlers; connections rely on TCP/app behavior only |

---

## Testing

### Unit

- `CRpcMessage.CreateHeartbeat()` round-trip encode/decode
- Header validation accepts `Heartbeat`, rejects unknown types
- `CRpcClientPipelineFactory`: writer idle handler + heartbeat handler present when enabled; absent when disabled
- `CRpcServerPipelineFactory`: reader idle handler present when enabled
- Options validation: `ReadIdleSeconds >= 2 × HeartbeatIntervalSeconds`

### Integration

- Connected client, no RPC: heartbeats keep connection alive beyond 45s
- Stop heartbeats (disable or mock handler): server closes within ~45s; client `ConnectionLost` / pending fail
- Pending `CallAsync` with slow server: heartbeats during wait prevent server read idle
- Gateway: backend `ConnectionLost` removes link and marks endpoint unhealthy

### Regression

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

---

## Phase 2 (explicitly out of scope)

- `HeartbeatAck` on server receive
- Client ack timeout → `CloseAsync`
- Client read idle (no inbound Response/Push for N seconds)
- Optional `CRpcClient` auto-reconnect (opt-in API)

---

## Success Criteria

1. Client sends Heartbeat every 15s (configurable) on all `CRpcClient` connections.
2. Server closes connections with 45s (configurable) of no inbound data.
3. Server never sends ack for Heartbeat in v1.
4. `CRpcConnectionRegistry` and Gateway sessions release on read-idle close.
5. Gateway backend links react to `ConnectionLost` without generic client reconnect.
6. UnifiedServer CRpc path uses shared server pipeline factory.
7. Full `CRPC.Tests` suite passes.

**Plan:** (to be created via writing-plans skill)
