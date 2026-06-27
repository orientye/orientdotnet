# CRpc Config Validation and Options Convergence Design

**Status:** Implemented  
**Date:** 2026-06-27  
**Related:** `Doc/TODO.txt` P0 item 1, `docs/superpowers/specs/2026-06-27-crpc-server-lifecycle-design.md`, `docs/superpowers/specs/2026-06-21-crpc-heartbeat-v1-design.md`, `Doc/architecture-draft.md`

---

## Goal

Make invalid transport configuration fail fast with clear `ArgumentOutOfRangeException` / `ArgumentException` messages, and remove the unused dual-track config stubs so hosts have a single framework-level options surface.

After this change:

```text
Host/App config (GateWayConfig, appsettings, CLI)  →  maps at startup
CRpcServerOptions / CRpcClientOptions                →  sole CRpc transport config types
TcpChannelHostOptions                              →  transport-layer subset for TcpChannelHost only
```

Delete empty `CRpc.Config.ServerConfig` and `CRpc.Config.ServiceConfig`. Do not introduce replacement DTOs in `CRpc.Config` in this work item.

---

## Non-Goals

- Appsettings / JSON binding layer (`HostCrpcSettings`, `IOptions<T>`, etc.)
- New public `ServerConfig` with `Host` + `Port` for discovery
- Wiring `CallTimeoutMilliseconds` into pending-call timeout logic (validate only; consumption is a follow-up)
- `CRpcLoopOptions` validation
- Injecting shared `IEventLoopGroup` via options (architecture-draft follow-up)
- Changing default values on `CRpcServerOptions` / `CRpcClientOptions`

---

## Decisions

### 1. Single framework config surface

| Layer | Type | Responsibility |
| --- | --- | --- |
| Application | Host-owned types (`GateWayConfig`, CLI args, future appsettings) | Business topology, listen ports, backend pools |
| CRpc RPC | `CRpcServerOptions`, `CRpcClientOptions` | DotNetty listener/client transport: ports, threads, frame limits, heartbeat, RPC timeouts |
| CRpc Transport | `TcpChannelHostOptions` | Tcp connect/thread settings inside `TcpChannelHost` |

Hosts continue constructing `CRpcServer` / `CRpcClient` with `*Options` directly, as in `Example/HelloWorld/Server/Program.cs` and `Example/GateWay/GateWayServer/Program.cs`.

### 2. Delete `CRpc.Config` stubs

Remove:

- `CRpc/Config/ServerConfig.cs`
- `CRpc/Config/ServiceConfig.cs`
- `CRpc/Config/` directory (empty after deletion)

No in-repo production references exist. Historical plan references in `docs/superpowers/plans/2026-05-27-unified-transport-abstraction.md` are archival only; do not resurrect these types.

### 3. Validation rules

Add named constants on the options types where a bound is shared (e.g. `MaxPort`, `MaxMaxFrameLength`). Use `ArgumentOutOfRangeException` for numeric range violations; use `ArgumentException` for `IPAddress` null (if ever exposed as nullable in future — not required today).

#### `CRpcServerOptions`

| Property | Rule | Message intent |
| --- | --- | --- |
| `Port` | `1` .. `65535` | Valid TCP port |
| `MaxFrameLength` | `>= CRpcMessage.MinFrameLength` (32) and `<= MaxMaxFrameLength` | Decoder can accept at least one minimal frame; cap prevents runaway allocations |
| `BossThreadCount` | `> 0` | DotNetty boss group requires at least one thread |
| `WorkerThreadCount` | `> 0` | DotNetty worker group requires at least one thread |
| `SoBacklog` | `> 0` | `ChannelOption.SoBacklog` must be positive |
| `ReadIdleSeconds` | See heartbeat section | Existing heartbeat v1 rules |

`MaxMaxFrameLength` default: **16 MiB** (`16 * 1024 * 1024`). Matches integration test usage (`1024 * 1024`) with headroom for larger payloads.

`Address` is non-nullable with default `IPAddress.Any`; no extra validation in v1.

#### `CRpcClientOptions`

| Property | Rule | Message intent |
| --- | --- | --- |
| `IoThreadCount` | `> 0` | DotNetty client event loop group |
| `ConnectTimeoutSeconds` | `> 0` | Tcp connect must have positive timeout |
| `MaxFrameLength` | Same as server | Shared decoder constraint |
| `CallTimeoutMilliseconds` | `> 0` | Fail-fast before pending-call timeout is wired |
| `HeartbeatIntervalSeconds` | See heartbeat section | Existing heartbeat v1 rules |

#### Heartbeat cross-field rules (unchanged semantics, clarified scope)

Apply only when the relevant heartbeat feature is enabled:

| Type | When | Rule |
| --- | --- | --- |
| `CRpcClientOptions` | `HeartbeatEnabled == true` | `HeartbeatIntervalSeconds > 0` |
| `CRpcClientOptions` | `HeartbeatEnabled == false` | Skip `HeartbeatIntervalSeconds` coupling checks |
| `CRpcServerOptions` | `HeartbeatEnabled == true` | `ReadIdleSeconds > 0` and `ReadIdleSeconds >= clientHeartbeatIntervalSeconds * 2` |
| `CRpcServerOptions` | `HeartbeatEnabled == false` | Skip read-idle / client-interval coupling checks |

`CRpcServerOptions.Validate(int clientHeartbeatIntervalSeconds = CRpcClientOptions.DefaultHeartbeatIntervalSeconds)` keeps the existing parameter so hosts running non-default client intervals can pass the actual client value at start time.

#### `TcpChannelHostOptions`

No rule changes. Keep existing validation for `IoThreadCount`, `ConnectTimeoutSeconds`, and `LoggingName`. `CRpcClientOptions.Validate()` covers the RPC-layer fields before values are copied into `TcpChannelHostOptions` in `CRpcClient.CreateHost`.

### 4. When `Validate()` runs

Primary entry points (fail before IO setup):

| Call site | When |
| --- | --- |
| `CRpcServer.StartInternalAsync` | Before creating `MultithreadEventLoopGroup` and calling `BindAsync` |
| `CRpcClient` constructor (`createHost: true`) | Before `TcpChannelHost` construction |
| `CRpcClient` internal constructor with injected host | Before storing options (options must be valid even if host pre-exists) |

Defense in depth (keep existing calls):

| Call site | When |
| --- | --- |
| `CRpcServerPipelineFactory.Configure` | Per accepted child channel |
| `CRpcClientPipelineFactory.Configure` | Per connect pipeline setup |
| `TcpChannelHost` constructor | Transport layer |

`Validate()` must be idempotent and cheap (integer comparisons only) so duplicate calls are acceptable.

`CRpcServer.RunAsync` flows through `StartInternalAsync` and therefore picks up start-time validation automatically.

### 5. Error shape

Follow `TcpChannelHostOptions.Validate()` style:

```csharp
throw new ArgumentOutOfRangeException(
    nameof(Port),
    Port,
    "CRpcServerOptions.Port must be between 1 and 65535.");
```

Include the type name prefix in messages (`CRpcServerOptions.*`, `CRpcClientOptions.*`) for easier log filtering.

---

## Implementation sketch

### `CRpcServerOptions.Validate`

Extend the existing method with port, frame, thread, and backlog checks before heartbeat logic. Gate heartbeat checks on `HeartbeatEnabled`.

### `CRpcClientOptions.Validate`

Add `IoThreadCount`, `ConnectTimeoutSeconds`, `MaxFrameLength`, and `CallTimeoutMilliseconds` checks. Gate `HeartbeatIntervalSeconds` on `HeartbeatEnabled`.

Extract shared `MaxFrameLength` validation into a private static helper on one type, or duplicate the two-line check in both options classes — prefer minimal duplication without a new shared public utility type for v1.

### `CRpcServer.StartInternalAsync`

```csharp
startOptions.Validate();
// then create event loop groups and bind
```

### `CRpcClient` constructors

Call `options.Validate()` at the start of the constructor chain before `CreateHost`.

### File deletions

Delete `CRpc/Config/ServerConfig.cs` and `CRpc/Config/ServiceConfig.cs`.

---

## Invariants

1. Invalid `*Options` never reach DotNetty `BindAsync`, `MultithreadEventLoopGroup`, or `LengthFieldBasedFrameDecoder` construction.
2. Framework transport configuration lives only on `CRpcServerOptions` / `CRpcClientOptions`; application hosts map their own config at startup.
3. Heartbeat v1 timing relationship (server read idle ≥ 2× client interval) is preserved when heartbeat is enabled.
4. `TcpChannelHostOptions` remains the transport-internal type; `CRpcClientOptions` is the public client configuration source for Io/connect settings.

---

## Verification

### Unit tests (`Tests/CRPC.Tests/CRpcTransportOptionsTests.cs`)

Extend with parameterized or individual tests:

**Server — reject:**

- `Port = 0`, `Port = 65536`
- `MaxFrameLength = 0`, `MaxFrameLength = 31`, `MaxFrameLength = MaxMaxFrameLength + 1`
- `BossThreadCount = 0`, `WorkerThreadCount = 0`, `SoBacklog = 0`
- Existing heartbeat cases remain

**Server — accept:**

- Defaults pass `Validate()`
- `MaxFrameLength = CRpcMessage.MinFrameLength` and `MaxFrameLength = MaxMaxFrameLength`

**Client — reject:**

- `IoThreadCount = 0`, `ConnectTimeoutSeconds = 0`, `CallTimeoutMilliseconds = 0`
- Same `MaxFrameLength` bounds as server
- Existing `HeartbeatIntervalSeconds = 0` when heartbeat enabled

**Client — accept:**

- Defaults pass `Validate()`

### Integration / behavior tests

- `CRpcServer.StartAsync` with `Port = 0` throws before bind (unit test with loop thread, no need for real socket)
- `CRpcClient` construction with `IoThreadCount = 0` throws before connect

### Regression

- All existing `CRpcServerTests`, `CRpcClientTests`, pipeline factory tests, and HelloWorld/GateWay paths remain green.
- No references to `CRpc.Config.ServerConfig` or `ServiceConfig` after deletion.

### Docs

- Archive or check off `Doc/TODO.txt` P0 item 1 when implementation completes.

---

## Follow-ups (out of scope)

- Appsettings binding spec when a host needs file-driven CRpc transport config
- Consume `CallTimeoutMilliseconds` in `CRpcClient` pending-call timer
- `RunAsync` bound-options copy omits heartbeat fields — separate bugfix if demos need heartbeat overrides via `RunAsync`

---

## Amendments

### Port 0 allowed for ephemeral bind

Implementation allows `CRpcServerOptions.Port = 0` for OS-assigned ephemeral bind. Validation accepts `0 .. 65535` (not `1 .. 65535` as originally drafted). `MinPort = 0`; error message documents ephemeral semantics.
