# CRpcClient TcpChannelHost Migration Design

## Purpose

Migrate `CRpcClient` onto the shared `TcpChannelHost` transport foundation created for the unified transport work.

This is a minimum-risk migration. The public `CRpcClient` API and RPC semantics stay unchanged. The change removes duplicated DotNetty bootstrap and IO group ownership from `CRpcClient`, while keeping request sequencing, pending calls, timeouts, and response completion inside `CRpcClient`.

## Current State

`CRpcClient` currently owns transport-level concerns directly:

- `Bootstrap`
- `IEventLoopGroup`
- connected `IChannel`
- client pipeline setup
- connect, close, and IO shutdown

Its pipeline currently uses:

- `LoggingHandler`
- `IdleStateHandler`
- `CRpcMessageDecoder`
- `CRpcMessageEncoder`
- `CRpcClientHandler`

`CRpcClientHandler` converts DotNetty events into `CRpcClient` callbacks:

- inbound `CRpcMessage` -> `OnReceiveResponse`
- channel inactive -> `OnChannelInactive`
- exception -> `OnChannelException`

The shared `TcpChannelHost` now provides the transport lifecycle:

- DotNetty bootstrap and IO group
- connect/write/close/shutdown
- IP-literal connect handling
- owner `CRpcLoop` ingress through `LoopInboundHandler`

## Goals

1. Make `CRpcClient` use `TcpChannelHost` for TCP/DotNetty lifecycle.
2. Keep `CRpcClient` public API unchanged.
3. Keep all RPC semantics in `CRpcClient`.
4. Delete the old `CRpcClientHandler` path after replacing it with `LoopInboundHandler`.
5. Add local tests that verify the migrated response and connection-loss paths without depending on a live CRPC server.

## Non-Goals

- Do not change `IRpcClient`.
- Do not change `CRpcReference*` behavior.
- Do not change `serviceId`, `methodId`, or request sequence semantics.
- Do not redesign pending-call storage.
- Do not introduce new write-failure continuation behavior.
- Do not replace current console logging with a logging abstraction in this phase.
- Do not require a real CRPC live server as a verification gate.

## Design Decisions

### Scope

Use minimum migration scope.

`CRpcClient` stops owning DotNetty bootstrap and IO group, but keeps the same public methods:

- `ConnectAsync(string host, int port) : CRpcTask<IChannel>`
- `CallAsync(ushort serviceId, ushort methodId, byte[] body, int timeout)`
- `CloseAsync()`
- `ShutdownIoAsync()`
- `DisposeAsync()`

### CRPC Client Pipeline

Add `CRpcClientPipelineFactory` near the client implementation.

It implements `IChannelPipelineFactory` and configures the CRPC-specific pipeline inside `TcpChannelHost`:

```text
TcpChannelHost bootstrap
  -> LoggingHandler
  -> CRpcClientPipelineFactory.Configure(...)
       -> IdleStateHandler
       -> CRpcMessageDecoder
       -> CRpcMessageEncoder
       -> LoopInboundHandler
```

`TcpChannelHost` keeps ownership of the outer transport lifecycle. `CRpcClientPipelineFactory` owns only CRPC client protocol handlers.

### Inbound Response Flow

Inbound responses flow through the shared host:

```text
DotNetty socket
  -> CRpcMessageDecoder
  -> LoopInboundHandler
  -> TcpChannelHost.PostInboundMessage
  -> owner CRpcLoop
  -> CRpcClient.CompleteReceiveResponse
```

The `InboundMessageReceived` callback in `TcpChannelHost` should cast or validate the inbound object as `CRpcMessage` before completing the pending call.

### Channel Inactive And Exception Flow

`LoopInboundHandler` should pass `context.Channel` into `TcpChannelHost` when channel inactive or exception events occur.

`TcpChannelHost` should filter stale channel events internally before invoking callbacks:

```text
if event channel != current host channel:
    ignore
else:
    post callback to owner loop
```

This keeps DotNetty `IChannel` identity as a transport-layer detail. `CRpcClient` only observes that its current connection became inactive or faulted.

`CRpcClient` then preserves its current behavior:

- inactive current connection -> `ConnectionClosedException("CRpcClient channel became inactive.")`
- exception on current connection -> `ConnectionClosedException("CRpcClient channel encountered an exception.", cause)`
- pending calls are failed on the owner `CRpcLoop`

### CRpcClientHandler Removal

After `LoopInboundHandler` replaces the three callback responsibilities, delete `CRpcClientHandler.cs`.

Keeping an unused handler would make the client path ambiguous and could hide accidental old-pipeline usage.

### Write Failure Semantics

Keep current `CallAsync` write behavior.

Current behavior:

- create pending call
- write the request
- if the write task is already completed, observe synchronous failure immediately
- if the write task fails later asynchronously, do not add a new continuation in this migration
- response, channel exception/inactive, or timeout completes the pending call

The migrated client should preserve this timing. Any improvement that immediately fails pending calls on asynchronous write failure belongs in a later, separate change.

### Connect Return Type

Keep `ConnectAsync` returning `CRpcTask<IChannel>`.

Although this still exposes DotNetty, preserving the public API keeps this phase focused and avoids breaking existing callers.

## Component Changes

### `CRpcClient`

Replace direct transport fields:

- remove direct `IEventLoopGroup`
- remove direct `Bootstrap`
- replace direct connected `IChannel?` ownership with `TcpChannelHost`

Keep client-owned state:

- `Dictionary<long, PendingCall> results`
- `CRpcClientOptions`
- request sequence
- owner `CRpcLoop`

Responsibilities after migration:

- create `TcpChannelHost` with `CRpcClientPipelineFactory`
- route `InboundMessageReceived` to response completion
- route `ChannelBecameInactive` and `ChannelExceptionCaught` to pending-call failure
- build and write `CRpcMessage` requests
- manage pending-call timeout timers

### `TcpChannelHost`

Extend internal event posting so inactive and exception events include the source `IChannel`.

Before invoking public callbacks, check that the event source matches the current channel. Stale events should be ignored.

This behavior benefits both current CRPC migration and any future host users that reconnect or receive delayed channel events.

### `LoopInboundHandler`

Pass `context.Channel` into the new `TcpChannelHost` inactive/exception methods.

Inbound message posting remains unchanged.

### `CRpcClientPipelineFactory`

New factory with dependencies from `CRpcClientOptions`:

- `HeartbeatIdleSeconds`
- `MaxFrameLength`
- `HashLength`
- `CompressThreshold`

It should add:

- `IdleStateHandler`
- `CRpcMessageDecoder`
- `CRpcMessageEncoder`
- `LoopInboundHandler`

## Error Handling

| Event | Behavior |
| --- | --- |
| Connect failure | `TcpChannelHost.ConnectAsync` completes with exception on owner loop |
| Response decode failure | Pipeline exception reaches host exception callback; pending calls fail |
| Current channel inactive | Pending calls fail with `ConnectionClosedException` |
| Current channel exception | Pending calls fail with `ConnectionClosedException` wrapping the cause |
| Stale channel inactive/exception | Ignored by `TcpChannelHost` |
| Synchronous write failure | `CallAsync` removes the pending call and throws |
| Asynchronous write failure | No new behavior in this migration |
| Call timeout | Existing timer behavior fails only that pending call |

## Testing

### Unit Tests

Add or update tests for:

- `CRpcClientPipelineFactory` configures the expected CRPC client handlers.
- `CRpcClient` completes a pending call from a `CRpcMessage` delivered through `TcpChannelHost`.
- channel inactive fails pending calls with `ConnectionClosedException`.
- channel exception fails pending calls with `ConnectionClosedException` wrapping the original exception.
- stale channel inactive/exception does not invoke host callbacks.
- `ConnectAsync` remains API-compatible and returns the host-connected `IChannel`.
- `CloseAsync` fails pending calls and closes the host channel.
- `ShutdownIoAsync` delegates to the host IO shutdown.

### Regression

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
```

Expected result: full suite passes.

### Live Testing

No real CRPC server live test is required for this phase.

The migration should rely on focused local tests plus the existing CRPC test suite. If a stable CRPC server endpoint is later available, live verification can be added as a separate task.

## Rollout

1. Add tests around `TcpChannelHost` stale channel filtering.
2. Add `CRpcClientPipelineFactory` tests.
3. Migrate `CRpcClient` to use `TcpChannelHost`.
4. Delete `CRpcClientHandler.cs`.
5. Run full `CRPC.Tests`.

## Risks

- `CallAsync` write timing could accidentally change if implementation awaits host writes. The implementation must preserve fire-and-forget behavior except for already-completed write failures.
- Channel inactive/exception events could double-fail pending calls if stale filtering is missed. Host-level channel identity filtering prevents that.
- `DisposeAsync` could block differently if host shutdown behavior differs from the previous direct IO group shutdown. Keep the same close-then-shutdown structure.
- Pipeline ordering must preserve current decoder/encoder behavior.

## Success Criteria

This phase is complete when:

1. `CRpcClient` no longer owns DotNetty `Bootstrap` or `IEventLoopGroup` directly.
2. `CRpcClient` uses `TcpChannelHost` for connect/write/close/shutdown.
3. `CRpcClientHandler.cs` is removed.
4. Public `CRpcClient` API remains unchanged.
5. Existing CRPC call, timeout, inactive, exception, and close behavior is covered by tests.
6. `dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj` passes.
