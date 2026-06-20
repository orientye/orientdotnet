# Multi-Endpoint CRpc + HttpServer Design

> **Superseded (HTTP-in-core):** `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md` — `HttpServer` and `IRpcHttpJsonCodec` removed from core.

**Date:** 2026-05-19  
**Status:** Approved (brainstorming session)

## Goal

Support minimal multi-protocol server hosting on one `CRpcLoop`:

- **7999** — CRpc binary (`CRpcServer`)
- **8080** — HTTP JSON (`HttpServer`) for inter-service simple calls

One business thread, one `ServiceRegistry`, no duplicate service state.

## Non-Goals (v1)

- Third port (e.g. 7001 internal CRpc)
- `application/octet-stream` HTTP body
- Kestrel / ASP.NET Core
- HTTP vs CRpc request/notification distinction (all unified RPC semantics)
- Per-port service filtering
- `IRpcEndpoint` abstraction layer

## Architecture

```text
CRpcLoopHost.RunUntilCancelled(loop, ct)
  └── CRpcLoop.Tick() on bound thread

CRpcLoop
  ├── ServiceRegistry (ushort → IRpcService)
  ├── CRpcServer :7999  → CRpcServerHandler → RpcServiceInvoker
  └── HttpServer  :8080 → HttpServerHandler  → JSON ↔ protobuf → RpcServiceInvoker
```

### Invariants

1. Business state and registry: **loop thread only**.
2. IO threads: decode, `loop.Post`, `WriteAndFlushAsync` without awaiting (unless result must affect business state → Post back).
3. Async business code: `CRpcTask` only; `TrySetResult` on loop thread.
4. HTTP does not use CRpc frame encryption/compression on the wire.

## HTTP Contract

```http
POST /{serviceId}/{methodId}
Content-Type: application/json
Accept: application/json

{ ... protobuf JSON mapping for request message ... }
```

**Response** (HTTP 200 for handled calls; business errors use JSON `code`):

```json
{"code":0,"body":{ ... reply message as JSON ... }}
```

| Condition | HTTP |
|-----------|------|
| Wrong method / path | 405 / 404 |
| Not `application/json` | 415 |
| Invalid JSON / proto JSON mismatch | 400 |
| Unknown serviceId | 404 |
| Unknown methodId or service lacks HTTP codec | 404 |
| Unhandled exception on loop | 500 |

`serviceId` / `methodId` match `CRpcMessage` header `module` / `command` (see `getServiceId()` / `getMethodId()`).

## JSON ↔ Protobuf

Generated services parse binary in `OnMessageAsync`. `HttpServer` converts at the boundary:

1. Resolve `IRpcHttpJsonCodec.TryGetHttpMethodParsers(methodId, …)`.
2. `JsonParser.Default.Parse(json, requestParser)` → `ToByteArray()` → synthetic `CRpcMessage` (same header pattern as `CRpcClient.__Send`, `NONE_ENCRYPT`).
3. `OnMessageAsync` → `(code, bytes)`.
4. `responseParser.ParseFrom(bytes)` → `JsonFormatter.Default.Format(message)` → response envelope.

Codegen (`crpc-protobuf-plugin`) generates `TryGetHttpMethodParsers` on each `{Service}Base` and implements `IRpcHttpJsonCodec`.

## Types

| Type | Responsibility |
|------|----------------|
| `CRpcLoop` | `RegisterService`, `TryGetService`, `UnregisterService` |
| `CRpcLoopHost` | `RunUntilCancelled` (alias/evolution of `CRpcServerLoop`) |
| `CRpcServerOptions` | Address, port, frame limits |
| `CRpcServer` | Bind TCP CRpc pipeline; `StartAsync` / `StopAsync`; registry forwards to loop |
| `HttpServerOptions` | Address, port (8080), max body bytes |
| `HttpServer` | Bind DotNetty HTTP pipeline |
| `HttpServerHandler` | Parse HTTP, Post to loop, write JSON response |
| `RpcServiceInvoker` | Shared loop-thread dispatch + CRpc response framing |
| `IRpcHttpJsonCodec` | Optional; method-level `MessageParser` pair |

## Lifecycle

```csharp
var loop = new CRpcLoop();
loop.RegisterService(new HelloworldServiceImpl());

var crpc = new CRpcServer(loop, new CRpcServerOptions { Port = 7999 });
var http = new HttpServer(loop, new HttpServerOptions { Port = 8080 });

await crpc.StartAsync(ct);
await http.StartAsync(ct);
CRpcLoopHost.RunUntilCancelled(loop, ct);
await http.StopAsync();
await crpc.StopAsync();
```

`CRpcServer.RunAsync` may remain as convenience (bind default port + run host) but must delegate to `StartAsync` + `CRpcLoopHost`.

## Dependencies

- Add `DotNetty.Codecs.Http` 0.7.6 to `CRpc/CRPC.csproj`.
- `Google.Protobuf` JsonParser / JsonFormatter (already referenced).

## Documentation

- Update `Doc/gateway.md` with HTTP contract.
- Add note in `Doc/architecture-draft.md` §4.2 that `HttpServer` replaces draft `HttpGatewayServer` naming.

## Verification

- Unit tests: loop registry, `RpcServiceInvoker`, `HttpServerHandler` via `EmbeddedChannel`.
- HelloWorld server: 7999 + 8080; manual or test `HttpClient` POST `/1000/1` with JSON body.
