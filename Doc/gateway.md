# HTTP Gateway (HttpServer)

`HttpServer` exposes the same `IRpcService` instances registered on a `CRpcLoop` over HTTP JSON.

## Ports (HelloWorld default)

| Port | Component | Protocol |
|------|-----------|----------|
| 7999 | `CRpcServer` | CRpc binary |
| 8080 | `HttpServer` | HTTP JSON |

## Request

```http
POST /{serviceId}/{methodId}
Content-Type: application/json
Accept: application/json

{ ... protobuf JSON for the request message ... }
```

Example:

```http
POST /1000/1
Content-Type: application/json

{"name":"world"}
```

## Response

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8

{"code":0,"body":{"message":"Hello world"}}
```

- `code` — business result from `OnMessageAsync` (same as CRpc tuple item 1).
- `body` — reply message as JSON (protobuf JSON mapping).

## Requirements

- Service implementation must implement `IRpcHttpJsonCodec` (generated on `{Service}Base` by `crpc-protobuf-plugin`).
- IO threads only decode HTTP and `loop.Post` business work; responses use `WriteAndFlushAsync` without awaiting.

## Startup pattern

```csharp
var loop = new CRpcLoop();
loop.Post(() => loop.RegisterService(new MyServiceImpl()));
loop.Tick();

await new CRpcServer(loop, new CRpcServerOptions { Port = 7999 }).StartAsync(ct);
await new HttpServer(loop, new HttpServerOptions { Port = 8080 }).StartAsync(ct);
CRpcLoopHost.RunUntilCancelled(loop, cancellationToken);
```

See `docs/superpowers/specs/2026-05-19-multi-endpoint-crpc-http-design.md` for full design.
