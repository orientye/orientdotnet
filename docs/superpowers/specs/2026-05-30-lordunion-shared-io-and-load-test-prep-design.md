# LordUnion Shared IO & Load-Test Prep — Design

**Status:** Implemented  
**Date:** 2026-05-30  
**Scope:** Shared DotNetty `IEventLoopGroup` for multi-account LordUnion live/load runs; explicit transport teardown; configurable `Live.IoThreadCount`.

**Prerequisite work:** `TcpChannelHost`, `GameServerDotNettyTransport`, single `CRpcLoop` per scenario, client stages 0–3c.

**Motivation:** Each live account previously created `new MultithreadEventLoopGroup(1)` → N accounts ≈ N IO threads. Unsuitable for planned load testing.

**Related:** `2026-05-27-unified-transport-abstraction-design.md`, `2026-05-29-lordunion-cleanup-phases-0-3c-design.md`

---

## Goals

1. N TCP connections share **one** `IEventLoopGroup`; `IoThreadCount` is configuration, not account count.
2. Channels close per account; **group shutdown once** per scenario on `CRpcLoop` with loop pump.
3. Default `TcpChannelHost` unchanged for `CRpcClient` (owns group when no injection).
4. `Live.IoThreadCount` in `LordUnionTestConfig`.

## Implementation summary

| Component | Change |
|-----------|--------|
| `TcpChannelHost` | Optional `sharedEventLoopGroup`; `ownsEventLoopGroup`; borrowed hosts skip group shutdown on dispose |
| `LordUnionSharedIo` | Creates group from `config.Live.IoThreadCount`; `DisposeAsync(CRpcLoop)` shuts down once |
| `GameServerDotNettyTransport` | Optional shared group; per-alias `LoggingName` |
| `LiveScenarioTransportFactory` | Requires shared `IEventLoopGroup` |
| `ThreePlayersOneGameScenario` | `try/finally`: dispose transports → `sharedIo.DisposeAsync(loop)` |

## Teardown order (live)

1. `DisposeAsync` on each `GameServerDotNettyTransport` (closes channel only when borrowing group).
2. `LordUnionSharedIo.DisposeAsync(loop)` → single `ShutdownGracefullyAsync`.

## Config

```json
"live": { "ioThreadCount": 1 }
```

Default: `TcpChannelHostOptions.DefaultIoThreadCount` (1). Validated `> 0`.

## Verification

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~TcpChannelHost|FullyQualifiedName~LordUnion"
```

Live: `Tests/LordUnion.IntegrationTests/scripts/run-live-back-to-back.ps1 -Runs 2`

## Non-Goals (follow-up)

- Generic load harness (N accounts, ramp, metrics).
- Multi-`CRpcLoop` sharding.
- CI nightly live.

## Load-test follow-up

- Batch connects; process-level isolation for very large N.
- Metrics: connect latency, signup success rate.
