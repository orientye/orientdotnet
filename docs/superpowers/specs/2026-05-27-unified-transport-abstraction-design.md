# Unified Transport Abstraction Design

**Date:** 2026-05-27  
**Status:** Proposed  
**Implementation plan:** `docs/superpowers/plans/2026-05-27-unified-transport-abstraction.md`

## Goal

Introduce a shared DotNetty TCP channel foundation in `orientdotnet` so LordUnion integration tests and future CRPC clients can reuse the same IO and `CRpcLoop` ingress model, while keeping each wire protocol and business semantics independent.

The first delivery migrates LordUnion live transport from `TcpClient` to DotNetty. It does **not** modify `CRpcClient` or unify RPC and game-server message semantics.

## Context

LordUnion integration tests V1 already run end-to-end against the real game server:

- Login, signup, enter match, and one full classic Dou Dizhu game succeed.
- Business logic runs on `CRpcLoop` / `CRpcTask`.
- Live transport today is `GameServerTcpTransport` (`TcpClient` + manual read loop).
- CRPC microservice clients use `CRpcClient` (DotNetty + `CRpcMessageEncoder/Decoder`).

These two paths share the async runtime but not the TCP/channel layer:

| Layer | CRPC (`CRpcClient`) | LordUnion (`GameServerTcpTransport`) |
| --- | --- | --- |
| IO | DotNetty | `TcpClient` |
| Frame codec | `CRpcMessage*` (`0x5F3759DF`) | `ServerPacketFrame` (`0x14801`) |
| Message semantics | `CallAsync(serviceId, methodId)` | Push + `WaitForMessageAsync` |
| Protobuf bodies | RPC payload bytes | `TKMobileReqMsg` / `TKMobileAckMsg` |

The game server does not speak CRPC RPC. Using `CRpcClient` directly against LordUnion would fail at the wire level. Unification must happen at the **transport/channel** layer, not by forcing a single client API.

## Non-Goals

This design explicitly excludes:

- Modifying `CRpcClient`, `CRpcMessageEncoder`, or `CRpcMessageDecoder` in the first delivery.
- Mapping LordUnion `header0`, `gameId`, or message kinds to `serviceId` / `methodId`.
- Adding a permanent user-facing transport selector (CLI flag or config) for TcpClient vs DotNetty.
- Unifying LordUnion flows (`LoginFlow`, `GameFlow`, etc.) with CRPC RPC call semantics.
- Pressure testing, CI pipeline work, or new game variants.
- Creating a separate `CRPC.Transport` NuGet/project; shared code lives under `CRPC/Transport` inside the existing `CRPC.csproj`.

## Decision

Use **option C2: connection + pluggable codec**, delivered in **scope P-A: LordUnion first, `CRpcClient` later**.

```text
CRPC/Transport                 shared DotNetty channel host + loop ingress
LordUnion.Protocol             GameServer frame codec (0x14801)
LordUnion.Sessions             GameServerDotNettyTransport (IGameServerTransport)
CRPC.Rpc.CRpc.Client           unchanged in phase 1
```

**Placement:**

- Shared channel infrastructure: `CRPC/Transport`
- GameServer frame codec: `Tests/LordUnion.IntegrationTests/Protocol`
- LordUnion transport adapter: `Tests/LordUnion.IntegrationTests/Sessions`

**Migration policy for old transport:**

- Keep `GameServerTcpTransport` source during migration for short-term comparison.
- Default live scenario uses `GameServerDotNettyTransport`.
- Do not expose a long-term fallback switch.
- Delete `GameServerTcpTransport` only after unit tests and at least one successful live run on DotNetty.

## Architecture

### Layering

```text
┌─────────────────────────────────────────────────────────────┐
│ Business semantics                                          │
│  CRpcClient.CallAsync          AccountSession / Flows     │
│  (serviceId, methodId)         (WaitForMessage, push)       │
├─────────────────────────────────────────────────────────────┤
│ Protocol codec                                              │
│  CRpcMessageEncoder/Decoder    ServerProtocolCodec          │
│  (0x5F3759DF + RPC header)     (0x14801 + TKMobile*)        │
├─────────────────────────────────────────────────────────────┤
│ Frame codec (DotNetty pipeline handlers)                    │
│  CRpcMessage* handlers         GameServerFrame* handlers    │
├─────────────────────────────────────────────────────────────┤
│ Shared transport (CRPC/Transport)                           │
│  TcpChannelHost, LoopInboundHandler, IChannelPipelineFactory│
├─────────────────────────────────────────────────────────────┤
│ DotNetty TCP / IChannel                                     │
└─────────────────────────────────────────────────────────────┘
```

The shared layer knows:

- How to connect, write, close, and shut down a DotNetty channel.
- How to post inbound messages, inactive events, and exceptions to the owning `CRpcLoop`.

The shared layer does **not** know:

- `serviceId`, `methodId`, `reqSequence`
- `header0`, `TKMobileReqMsg`, login AES rules
- Account session state or scenario orchestration

### Components

#### `TcpChannelHostOptions`

Configuration for the shared host:

- `IoThreadCount` (default `1`)
- `ConnectTimeoutSeconds` (default `10`)
- `TcpNoDelay` (default `true`)
- `LoggingName` (default `"tcp-channel"`)

Validates positive thread count and connect timeout.

#### `IChannelPipelineFactory`

Protocol-specific hook that configures a DotNetty pipeline on a connected channel. Each consumer supplies its own encoder/decoder handlers plus the shared `LoopInboundHandler`.

#### `LoopInboundHandler`

DotNetty `ChannelHandlerAdapter` that forwards:

- `ChannelRead` → `TcpChannelHost.PostInboundMessage`
- `ChannelInactive` → `TcpChannelHost.PostChannelInactive`
- `ExceptionCaught` → `TcpChannelHost.PostChannelException`, then close channel

All callbacks are marshaled with `ownerLoop.Post(...)`. Business state must only be touched on the owner loop thread.

Decoded messages passed to `LoopInboundHandler` must be owned objects (for example `GameServerFrame`), not reference-counted buffers requiring release on the IO thread.

#### `TcpChannelHost`

Shared DotNetty lifecycle wrapper:

- `ConnectAsync(host, port)` → `CRpcTask<IChannel>`
- `WriteAndFlushAsync(object message)` → `CRpcTask`
- `CloseAsync()` → `CRpcTask`
- `ShutdownIoAsync()` → `CRpcTask`
- `DisposeAsync()` → close then shutdown IO on owner loop

Public callback properties:

- `InboundMessageReceived`
- `ChannelBecameInactive`
- `ChannelExceptionCaught`

All public mutating methods require the owner `CRpcLoop` thread, matching existing `CRpcClient` conventions.

External DotNetty/BCL `Task` results are bridged with `CRpcTask.FromTask(..., ownerLoop)`.

#### `GameServerFrame`

LordUnion frame envelope:

```csharp
public readonly record struct GameServerFrame(uint Header0, byte[] Body);
```

#### `GameServerFrameDecoder` / `GameServerFrameEncoder`

DotNetty handlers implementing the existing 8-byte little-endian frame format documented in `WireProtocolNotes.md`:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | `header0` (client send: `0x14801`) |
| 4 | 4 | `bodyLength` |
| 8 | N | protobuf body |

Encoder output must be byte-identical to `ServerPacketFrame.EncodeFrame(...)`.

Decoder responsibilities:

- Handle partial frames (wait for header, then body).
- Reject negative `bodyLength`.
- Enforce configurable `maxBodyLength`.

Protobuf decode remains in `ServerProtocolCodec`, not in the frame decoder.

#### `GameServerPipelineFactory`

Implements `IChannelPipelineFactory` for LordUnion:

```text
game-server-decoder → GameServerFrameDecoder
game-server-encoder → GameServerFrameEncoder
loop-ingress        → LoopInboundHandler
```

Logging is already added by `TcpChannelHost` before factory configuration.

#### `GameServerDotNettyTransport`

Implements existing `IGameServerTransport`:

- `ConnectAsync` creates `TcpChannelHost` with `GameServerPipelineFactory`.
- `SendAsync(byte[] packet)` converts packet bytes to `GameServerFrame`, then writes through the host.
- `BindIncomingHandler` stores `AccountSession`; inbound frames decode through `ServerProtocolCodec` and call `DeliverIncomingMessage`.
- `DisconnectAsync` closes the host channel.

`AccountSession`, flows, and `ServerProtocolCodec` remain unchanged. Only the live transport implementation swaps.

#### Unchanged consumers (phase 1)

- `CRpcClient` and `CRpcClientHandler`
- `CRpcMessageEncoder` / `CRpcMessageDecoder`
- `IRpcClient.CallAsync(ushort serviceId, ushort methodId, ...)`
- LordUnion business flows and bot logic

## Data Flow

### LordUnion outbound (client → server)

```text
Flow calls session.SendRequestAsync(TKMobileReqMsg)
  → ServerProtocolCodec.EncodeClientRequest
  → byte[] packet (0x14801 + protobuf)
  → GameServerDotNettyTransport.SendAsync(packet)
  → BuildOutboundFrame(packet)
  → TcpChannelHost.WriteAndFlushAsync(GameServerFrame)
  → GameServerFrameEncoder
  → TCP
```

### LordUnion inbound (server → client)

```text
TCP
  → GameServerFrameDecoder
  → LoopInboundHandler
  → ownerLoop.Post
  → GameServerDotNettyTransport.HandleInboundMessage
  → ServerProtocolCodec.DecodePacket
  → AccountSession.DeliverIncomingMessage
  → Flow wait / push handler
```

### CRPC RPC (unchanged in phase 1)

```text
CRpcClient.CallAsync(serviceId, methodId, body)
  → CRpcMessage + CRpcMessageHeader
  → CRpcMessageEncoder
  → DotNetty channel (owned by CRpcClient today)
  → CRpcMessageDecoder
  → CRpcClientHandler → pending call completion
```

Future phase 5 may replace `CRpcClient`'s private Bootstrap with `TcpChannelHost` + a CRPC pipeline factory. `serviceId` / `methodId` stay in `CRpcClient`.

## Error Handling

| Event | Behavior |
| --- | --- |
| Connect failure | `ConnectAsync` completes with exception via `CRpcTask` |
| Write failure | `SendAsync` / `WriteAndFlushAsync` completes with exception |
| Invalid frame (`bodyLength < 0`, over max) | Decoder throws; channel exception posted to loop; session marked failed |
| Protobuf decode failure | `ServerProtocolCodec` error path unchanged; session/flow handles failure |
| Channel inactive | Post to loop; `GameServerDotNettyTransport` sets session state to failed |
| Decoder receives wrong object type | Treat as channel exception; log and fail session |

Write completion for normal LordUnion responses does not need to block business state unless a future requirement says otherwise. Follow existing CRPC guidance: observe write failures through returned `CRpcTask`, do not await writes on IO threads for business updates.

## Testing

### Unit tests (no live server)

- `TcpChannelHostOptions` defaults and validation
- `LoopInboundHandler` posts to owner loop (embedded channel)
- `TcpChannelHost` owner-loop guards and disconnected write behavior
- `GameServerFrameCodecTests`: byte parity with `ServerPacketFrame`, partial/sticky frames, negative length rejection
- `GameServerPipelineFactoryTests`: pipeline delivers `GameServerFrame` to host callback
- `GameServerDotNettyTransportTests`: frame delivery reaches `AccountSession.ReceivedMessages`

Use `[assembly: InternalsVisibleTo("CRPC.Tests")]` on the LordUnion integration test assembly for focused transport test helpers.

### Integration / live verification

- All existing LordUnion xUnit tests pass (fake transport unchanged via `ScenarioRunOptions.TransportFactory`).
- At least one live three-account scenario report with `"success": true` on DotNetty transport.
- Compare against baseline report `scenario-report-20260527T105628Z.json`: login, signup, enter match, and game all complete.

### Failure classification before code changes

If live fails after migration:

- Connect failure → inspect `TcpChannelHost` / pipeline setup
- Decode failure → inspect frame codec byte parity
- Send failure → inspect packet-to-frame conversion
- Login/signup/enter/game timeout → inspect received messages and flows before reverting transport layer

## Phased Delivery

| Phase | Deliverable | Exit criteria |
| --- | --- | --- |
| 0 | Baseline | LordUnion unit tests green; latest live success recorded |
| 1 | `CRPC/Transport` foundation | Transport unit tests green |
| 2 | GameServer frame codec | Codec tests match `ServerPacketFrame` bytes |
| 3 | `GameServerDotNettyTransport` + default live switch | Transport tests green; scenario unit tests green |
| 4 | Live verification | New live report `success: true` |
| 5 | Cleanup | Delete `GameServerTcpTransport`; no remaining references |
| Future | `CRpcClient` on `TcpChannelHost` | Separate plan after phase 4 stable |

## Alternatives Considered

### C1: IO channel only, no pluggable codec abstraction

Rejected as the sole target because DotNetty's value is the pipeline. Without `IChannelPipelineFactory`, CRPC and LordUnion would duplicate pipeline wiring next to the shared host.

### C3: Unified `IConnection` session API

Rejected for phase 1 because CRPC RPC and LordUnion push models diverge at the semantic layer. A byte-level connection interface adds indirection without removing duplicate upper-layer clients.

### C4: Single unified client API for RPC and game server

Rejected. Forcing `serviceId`/`methodId` or a generic message bus over LordUnion push traffic would distort the game-server protocol and increase failure risk.

### Permanent TcpClient fallback switch

Rejected. Short-term source retention is enough for migration debugging. A permanent selector increases maintenance and hides the default path.

## Success Criteria

Phase 1 delivery is complete when:

1. `CRPC/Transport` provides a reusable DotNetty channel host with `CRpcLoop` ingress.
2. LordUnion live runner defaults to `GameServerDotNettyTransport`.
3. Existing LordUnion flows and `ServerProtocolCodec` require no semantic changes.
4. `CRpcClient` behavior and API are unchanged.
5. Unit tests cover shared transport and GameServer codec parity.
6. At least one live scenario succeeds on the new transport.
7. `GameServerTcpTransport` is removed in a follow-up cleanup task after verification.

## References

| Symbol | Location |
| --- | --- |
| `GameServerTcpTransport` | `Tests/LordUnion.IntegrationTests/Sessions/GameServerTcpTransport.cs` |
| `IGameServerTransport` | `Tests/LordUnion.IntegrationTests/Sessions/IGameServerTransport.cs` |
| `ServerPacketFrame` | `Tests/LordUnion.IntegrationTests/Protocol/ServerPacketFrame.cs` |
| `WireProtocolNotes.md` | `Tests/LordUnion.IntegrationTests/Protocol/WireProtocolNotes.md` |
| `CRpcClient` | `CRPC/Rpc/CRpc/Client/CRpcClient.cs` |
| `CRpcLoop` / `CRpcTask` | `CRPC/Async/` |
| LordUnion integration test design | `docs/superpowers/specs/2026-05-25-lordunion-integration-test-design.md` |
| Implementation plan | `docs/superpowers/plans/2026-05-27-unified-transport-abstraction.md` |
