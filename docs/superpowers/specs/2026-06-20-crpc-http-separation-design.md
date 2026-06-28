# CRpc / HTTP Separation Design

**Date:** 2026-06-20  
**Status:** Approved  
**Supersedes:** `docs/superpowers/specs/2026-05-19-multi-endpoint-crpc-http-design.md` (HTTP-in-core portions)  
**Related:** `Doc/protocol.md`, `docs/superpowers/specs/2026-06-19-crpc-binary-codec-design.md`, `docs/superpowers/specs/2026-05-28-crpc-server-push-design.md`

---

## Goal

Separate **CRpc binary runtime** from **HTTP concerns**:

1. **`CRpc.dll`** — `CRpcLoop`, `IRpcService`, `CRpcServer`, binary wire codec only. No HTTP server, no JSON routing, no HTTP codec interfaces.
2. **`crpc-protobuf-plugin`** — generates `{Service}ServiceBase` and `{Service}ClientBase` for CRpc only. No `IRpcHttpJsonCodec`, no `TryGetHttpMethodParsers`.
3. **HTTP** — application-owned (or a future optional package). Custom URL routes, JSON handling, optional Port Unification on the same TCP port as CRpc.

CRpc and HTTP may share **one `CRpcLoop`**, **one service registry**, and **one business thread**. HTTP invokes business via **typed service methods** on the loop thread; it does **not** need to call `OnMessageAsync`.

---

## Non-Goals (this change)

- Official `CRpc.Http` NuGet package (may follow later; not required for this spec).
- HTTP code generator or `POST /{serviceId}/{methodId}` contract in core.
- Kestrel / ASP.NET Core integration.
- TLS / ALPN negotiation.
- Changing CRpc binary wire format or `crpc.service_id` / `crpc.method_id` proto options.
- Obsolete period for removed HTTP APIs — **delete directly** from core.

---

## Background / Problem

The current design couples HTTP to CRpc core and codegen:

| Location | HTTP coupling |
| --- | --- |
| `IRpcHttpJsonCodec` | Optional interface on `IRpcService` |
| `GreeterServiceBase` | Implements `IRpcHttpJsonCodec`; codegen emits `TryGetHttpMethodParsers` |
| `HttpServer` / `HttpServerHandler` | Fixed route `POST /{serviceId}/{methodId}`, JSON ↔ protobuf, `RpcServiceInvoker` → `OnMessageAsync` |
| `Example/HelloWorld/Server/Program.cs` | Starts `CRpcServer` + `HttpServer` on two ports |

This mixes transport responsibilities onto `ServiceBase`, duplicates method metadata (dispatch in `OnMessageAsync` and parsers in `TryGetHttpMethodParsers`), and constrains HTTP to numeric routes. New endpoints (WebSocket, gRPC gateway) would invite more interfaces on `IRpcService`.

---

## Decisions

### 1. CRpc framework scope

**In `CRpc.dll`:**

| Keep | Remove |
| --- | --- |
| `CRpcLoop`, `RegisterService`, `TryGetService` | `HttpServer` |
| `IRpcService`, `OnMessageAsync` | `HttpServerHandler` |
| `CRpcServer`, `CRpcServerHandler`, codec types | `HttpServerOptions` |
| `CRpcConnectionRegistry`, `CRpcContext` | `IRpcHttpJsonCodec` |
| `RpcServiceInvoker` (used by CRpc handler; may stay `internal`) | Any `POST /{serviceId}/{methodId}` contract |

Remove `DotNetty.Codecs.Http` from `CRpc/CRPC.csproj` if no remaining core code references it.

**`OnMessageAsync` is the CRpc binary entry only.** TCP clients send binary frames → `CRpcServerHandler` → `loop.Post` → `OnMessageAsync`.

### 2. Codegen scope

`CRpcGen.GenerateServiceForServer` emits:

```csharp
public abstract class GreeterServiceBase : IRpcService
```

Only:

- `GetServiceId()`
- `OnMessageAsync` + per-method `__OnMessageXxxAsync` dispatch
- `protected abstract` typed methods (e.g. `SayHelloAsync`)
- Push helpers for `server_push` methods

Do **not** emit `IRpcHttpJsonCodec` or `TryGetHttpMethodParsers`.

### 3. HTTP at application layer

HTTP is **not** a framework feature. The application (e.g. `Example/HelloWorld/Server/`) owns:

- DotNetty HTTP pipeline (`HttpServerCodec`, `HttpObjectAggregator`, etc.)
- URL routing (REST, proto method names, health checks — app choice)
- JSON ↔ protobuf (`Google.Protobuf` `JsonParser` / `JsonFormatter`)
- Optional **Port Unification** (see §4)

**Invoke pattern (recommended):**

```text
IO thread: decode HTTP, parse JSON → HelloRequest
    → loop.Post(() => {
         var (code, reply) = await impl.SayHelloAsync(ctx, request);
         // marshal result back to IO thread for HTTP write
       })
```

- Business runs on **`CRpcLoop` thread** (`loop.Post` or already in loop).
- Use **`CRpcTask`**; `TrySetResult` only on loop thread.
- HTTP does **not** call `OnMessageAsync` unless the app explicitly chooses a generic gateway style (not recommended for HelloWorld).

**Accessing typed methods:** Generated `SayHelloAsync` is `protected abstract` on `ServiceBase`. Application options:

1. HTTP handler lives in the same assembly as `HelloworldServiceImpl` and calls through a small public façade on the impl, or
2. Impl exposes thin `public` methods that forward to `protected` overrides (hand-written in app code).

The framework does not add a new public typed interface in this change.

### 4. Port Unification (application layer, optional)

Applications may listen on **one TCP port** and sniff the first bytes per connection:

| First bytes | Branch |
| --- | --- |
| `0x43 0x52 0x50 0x43` (`'CRPC'` LE) | CRpc binary pipeline (`CRpcMessageDecoder` → `CRpcServerHandler`) |
| HTTP methods (`GET`, `POST`, `HEAD`, …) | App HTTP pipeline |

Pattern (Netty Port Unification):

1. `ByteToMessageDecoder` reads ≥2–4 bytes, `markReaderIndex` / `resetReaderIndex`.
2. Add protocol-specific handlers to pipeline.
3. `remove(this)` — one sniff per connection.

**Shared runtime:**

- Same `CRpcLoop` instance passed to both branches.
- Same `RegisterService` table.
- **One `CRpcConnectionRegistry` per unified server** (not separate registries per protocol). Register channel on `ChannelActive` via `loop.Post`.

Port Unification lives in **Example** (or future `CRpc.Http`), not in `CRpc.dll`.

### 5. Delete HTTP from core (no Obsolete)

Remove files and references in one breaking change:

- Delete `CRpc/Rpc/IRpcHttpJsonCodec.cs`
- Delete `CRpc/Rpc/CRpc/Server/HttpServer.cs`
- Delete `CRpc/Rpc/CRpc/Server/HttpServerHandler.cs`
- Delete `CRpc/Rpc/CRpc/Server/HttpServerOptions.cs`
- Delete `Tests/CRPC.Tests/HttpServerHandlerTests.cs`
- Remove HTTP tests from `Tests/CRPC.Tests/CRpcServerTests.cs`
- Regenerate / update `Example/HelloWorld/Server/HelloworldService.cs` (remove `IRpcHttpJsonCodec`)
- Update `Example/HelloWorld/Server/Program.cs` (CRpc only, or app-layer HTTP demo)
- Update `Tool/crpc-protobuf-plugin/CRpcProtobufPlugin/CRpcGen.cs`
- Update generator tests in `Tests/CRPC.Tests/CRpcGeneratorTests.cs`

---

## Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│ Application (Example/HelloWorld/Server)                      │
│  · Optional UnifiedServer + PortUnificationHandler           │
│  · Optional GreeterHttpRoutes (URL → JSON → typed call)      │
│  · Program.cs: loop.RegisterService(impl)                    │
└───────────────────────────┬─────────────────────────────────┘
                            │
         ┌──────────────────┴──────────────────┐
         ▼                                      ▼
┌─────────────────────┐              ┌─────────────────────┐
│ CRpc branch         │              │ HTTP branch (app)   │
│ CRpcMessageDecoder  │              │ HttpCodec + Router  │
│ CRpcServerHandler   │              │ loop.Post → typed   │
└──────────┬──────────┘              └──────────┬──────────┘
           │                                    │
           │ OnMessageAsync                     │ SayHelloAsync (etc.)
           └────────────────┬───────────────────┘
                            ▼
                 ┌─────────────────────┐
                 │ CRpcLoop (1 thread)   │
                 │ ServiceRegistry       │
                 │ Timers / CRpcTask     │
                 └─────────────────────┘
```

### Invariants (unchanged)

1. Business state and `ServiceRegistry`: **loop thread only**.
2. DotNetty IO threads: decode, `loop.Post`, `WriteAndFlushAsync` without awaiting (unless write outcome affects business state → Post back).
3. Async business code: **`CRpcTask` only** on CRpc paths.
4. CRpc responses: submit write without awaiting unless marshaling result to loop first.

---

## HelloWorld Example (target shape)

**Minimum (P1):** CRpc-only server — `Program.cs` starts `CRpcServer` only; no HTTP.

**Recommended demo (P2):** Add application HTTP under `Example/HelloWorld/Server/Http/`:

| File | Responsibility |
| --- | --- |
| `GreeterHttpHandler.cs` | Route e.g. `POST /api/greeter/say-hello`, JSON body, `loop.Post` → `SayHelloAsync` |
| `UnifiedServer.cs` (optional) | Single port + `PortUnificationHandler` |

Example registration:

```csharp
var loop = new CRpcLoop();
var impl = new HelloworldServiceImpl();
loop.RegisterService(impl);

// CRpc-only: CRpcServer(loop, options)
// Or unified: new UnifiedServer(loop, impl, options)  // app code
```

---

## Error Handling (application HTTP)

Framework does not define HTTP status codes. Application handlers should follow usual practice:

| Condition | Suggested HTTP |
| --- | --- |
| Unknown route | 404 |
| Wrong method | 405 |
| Invalid JSON | 400 |
| Application error (`code != 0`) | 200 with app envelope, or 4xx/5xx per app policy |

CRpc binary errors remain in `CRpcMessage` `resultCode` / frame semantics per `Doc/protocol.md`.

---

## Testing

| Area | Action |
| --- | --- |
| `HttpServerHandlerTests` | **Delete** |
| `CRpcServerTests` HTTP cases | **Remove** |
| `CRpcGeneratorTests` | Assert no `IRpcHttpJsonCodec` in generated server file |
| CRpc integration | Existing CRpc server/client tests must pass |
| HelloWorld HTTP (optional) | App-level test or manual `curl` against example route |

---

## Documentation Updates

| Doc | Change |
| --- | --- |
| `Doc/architecture.md` | Mark `HttpServer` as removed from core; HTTP is app concern |
| `Doc/gateway.md` (if HTTP section exists) | Remove core `POST /{serviceId}/{methodId}` as framework contract |
| `docs/superpowers/specs/2026-05-19-multi-endpoint-crpc-http-design.md` | Add header note: superseded by this spec for HTTP-in-core |
| `docs/superpowers/specs/2026-06-19-crpc-binary-codec-design.md` | Clarify HTTP is outside core (no change to binary wire) |

---

## Implementation Phases

| Phase | Scope | Delivers |
| --- | --- | --- |
| **P1** | Codegen + regenerate HelloWorld service | `ServiceBase : IRpcService` only |
| **P2** | Delete HTTP from `CRpc.dll`, tests, csproj | Core builds without HTTP |
| **P3** | HelloWorld `Program.cs` CRpc-only | Runnable example |
| **P4** | App-layer HTTP demo (optional) | `GreeterHttpHandler` + routes |
| **P5** | App-layer Port Unification (optional) | Single-port demo |
| **P6** | Doc updates | Architecture docs aligned |

P1–P3 are required for a complete breaking change. P4–P5 demonstrate the new model; P6 should land with or immediately after P2–P3.

---

## Future (out of scope)

- **`CRpc.Http` package** — reusable Port Unification + JSON helpers; still not part of core.
- **HTTP codegen plugin** — separate from `crpc-protobuf-plugin`.
- **Public typed service interface** — if apps want HTTP handlers without concrete impl types.

---

## Spec Self-Review

- [x] No TBD / placeholder sections.
- [x] Consistent with CRpc binary codec (binary path unchanged).
- [x] Scope fits one implementation plan (phases P1–P6).
- [x] Explicit: HTTP does not call `OnMessageAsync` by default; CRpc does.
- [x] Explicit: delete HTTP from core, no Obsolete period.
- [x] Connection registry: unified server uses one registry (ambiguous point resolved).
