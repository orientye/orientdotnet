# CRpcClient Pending Call Lifecycle Design

**Date:** 2026-05-24  
**Status:** Implemented

## Problem

`CRpcClient` keeps in-flight RPC calls in a loop-owned `results` dictionary (`seq → PendingCall`). Before this fix:

1. **`CloseAsync` / `DisposeAsync` did not fail pending calls** — entries stayed in `results` until response or timeout.
2. **Remote disconnect had no cleanup path** — `CRpcClientHandler` did not handle `ChannelInactive`.
3. **`timeout == 0` meant infinite wait** — no timer was registered; if the connection closed, the call could hang forever.

This is a correctness bug, not just a resource leak: callers could block indefinitely, and upper layers could not retry or fail over promptly.

## Decision

### 1. Pending calls complete when the connection ends

When the client channel closes — actively via `CloseAsync` or passively via `ChannelInactive` — all pending calls must fail immediately with `ConnectionClosedException`.

Pending call lifecycle is bound to connection lifecycle:

| Event | Where it runs | Action |
| --- | --- | --- |
| Response received | owner loop (`CompleteReceiveResponse`) | `Remove(seq)` → cancel timer → `TrySetResult` |
| Timeout | owner loop (timer callback) | `Remove(seq)` → `TrySetException(TimeoutException)` |
| Active close (`CloseAsync`) | owner loop | `channel = null` → `FailPendingCalls(ConnectionClosedException)` → DotNetty close |
| Passive close (`ChannelInactive`) | IO thread → `ownerLoop.Post` | clear `channel` if same instance → `FailPendingCalls(ConnectionClosedException)` |

`FailPendingCalls`:

1. Snapshot `results.Values`, then `Clear()` the dictionary.
2. For each pending call: cancel `TimeoutTimer`, `TrySetException(exception)`.
3. Must run only on the owner `CRpcLoop` thread.

### 2. `timeout` must be a positive integer

`CRpcClient.CallAsync(..., timeout)` rejects `timeout <= 0` with `ArgumentOutOfRangeException`.

Rationale:

- Network RPC should not support implicit infinite wait at the transport layer.
- Default timeout belongs in Reference / generated proxy APIs (e.g. `SayHelloAsync(..., timeOut = 5000)`), not as `0` passed to `CallAsync`.
- Every in-flight call always has a timer, so there is always a bounded completion path even if connection-close handling regresses.

### 3. Thread model

- `results`, `PendingCall`, and `CRpcTaskCompletionSource` are owner-loop-owned.
- DotNetty `ChannelInactive` fires on IO threads — handler calls `client.OnChannelInactive(channel)`, which `ownerLoop.Post`s cleanup.
- Do not touch `results` or call `TrySetException` from IO threads.

## Components

```text
CRpcClient
  ├── results: Dictionary<long, PendingCall>   (owner loop only)
  ├── CallAsync(...)                           (timeout > 0 required)
  ├── CloseAsync()                             → FailPendingCalls + DotNetty close
  ├── OnChannelInactive(IChannel)              → ownerLoop.Post(FailPendingCalls)
  └── FailPendingCalls(Exception)

CRpcClientHandler
  ├── ChannelRead  → OnReceiveResponse → ownerLoop.Post
  └── ChannelInactive → OnChannelInactive → ownerLoop.Post

ConnectionClosedException
  └── thrown when pending call fails due to channel close / inactive
```

## Completion paths (summary)

A pending `CallAsync` completes in exactly one of three ways:

1. **Success** — matching response arrives, `TrySetResult`.
2. **Timeout** — timer fires, `TrySetException(TimeoutException)`.
3. **Connection closed** — active or passive close, `TrySetException(ConnectionClosedException)`.

Late responses after timeout or connection close are ignored (`results.Remove` fails).

## API layering

| Layer | Timeout responsibility |
| --- | --- |
| `CRpcClient.CallAsync` | Caller must pass explicit positive `timeout` (milliseconds). |
| Generated proxy / Reference | Provide default (e.g. `5000`); do not forward `0` to `CallAsync`. |

## Tests

`Tests/CRPC.Tests/CRpcClientTests.cs`:

- `CallAsyncThrowsWhenTimeoutIsNotPositive` — `timeout: 0` rejected
- `CloseAsyncFailsPendingCalls` — active close fails pending call
- `ChannelInactiveFailsPendingCalls` — passive close fails pending call

## Related docs

- [architecture.md §6.3 / §9.5.7 / §9.5.8](../../../Doc/architecture.md) — architecture overview and invariants
