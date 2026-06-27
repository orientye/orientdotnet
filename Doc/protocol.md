# CRpc Binary Protocol

Normative wire format for CRpc TCP clients and servers. Full design: `docs/superpowers/specs/2026-06-19-crpc-binary-codec-design.md`

## Frame

```text
magic(4) + payloadLen(4) + header(24) + body(N)
```

- `magic` = `0x43525043` (`'CRPC'`, little-endian)
- `payloadLen` = `24 + body.Length` (header + body only)
- All integers little-endian

## Header (24 bytes)

| Offset | Field |
| --- | --- |
| 0 | `version` = 1 |
| 1 | `messageType` — 0 Request, 1 Response, 2 Push, 3 Heartbeat |
| 2 | `flags` — current release: `0` (`0x01` = compressed, reserved) |
| 3 | `reserved` = 0 |
| 4 | `serviceId` u16 |
| 6 | `methodId` u16 |
| 8 | `reqSeq` u64 |
| 16 | `resultCode` i32 |
| 20 | `bodyOriginLen` u32 |

## Body

Raw protobuf bytes. No application-layer checksum. No compression in current release (`flags = 0`).

## Message conventions

| Type | reqSeq | resultCode |
| --- | --- | --- |
| Request | > 0 | 0 |
| Response | matches request | service result |
| Push | 0 | 0 |
| Heartbeat | 0 | 0 |

## Heartbeat (v1)

- Client sends `Heartbeat` on a fixed writer-idle interval (default 15s).
- Server does not reply. Any inbound frame (RPC or Heartbeat) resets the server read-idle timer (default 45s).
- Server closes the connection when read idle expires.

---

# Multi-Protocol Endpoint & HTTP

How to expose CRpc and HTTP on the same service. **HTTP is application-owned** — routes, JSON shape, and status codes are not defined by `CRpc.dll`.

Reference implementation: `Example/HelloWorld/Server/Http/`  
Architecture decisions: `docs/superpowers/specs/2026-06-20-crpc-http-separation-design.md`

## Deployment modes

| Mode | Start flags | Ports | Description |
| --- | --- | --- | --- |
| CRpc only | (default) | `7999` (`--port` overrides) | Binary CRpc only |
| CRpc + HTTP | `--http` | CRpc: `7999`, HTTP: `8080` (or `crpcPort + 1000`) | Two TCP listeners |
| Unified | `--unified` | Single port (default `7999`) | Port Unification sniffs and branches per connection |

Example:

```bash
dotnet run --project Example/HelloWorld/Server
dotnet run --project Example/HelloWorld/Server -- --http
dotnet run --project Example/HelloWorld/Server -- --unified
dotnet run --project Example/HelloWorld/Server -- --port 9000 --unified
```

## Port Unification (same TCP port)

Optional application-layer pattern: read the first bytes of each new connection, then install the matching DotNetty pipeline.

| First bytes | Branch |
| --- | --- |
| `0x43 0x52 0x50 0x43` (`'CRPC'` LE) | CRpc: `CRpcMessageDecoder` → `CRpcServerHandler` |
| Otherwise (e.g. `GET`, `POST`, `HEAD` HTTP prefixes) | HTTP: `HttpServerCodec` → app router |

Rules:

1. Sniff once per connection; remove the sniff handler after branching.
2. Share one `CRpcLoop`, one service registry, and one `CRpcConnectionRegistry` on a unified server.
3. Not part of `CRpc.dll` — see `PortUnificationHandler.cs` and `UnifiedServer.cs` in the HelloWorld example.

## HelloWorld HTTP reference (not a framework contract)

The routes below are **example only**. Production apps may define their own URLs and response envelopes.

### `POST /api/greeter/say-hello`

| Item | Value |
| --- | --- |
| Method | `POST` |
| Content-Type | `application/json` |
| Request body | `HelloRequest` as protobuf JSON (e.g. `{"name":"world"}`) |
| Success response | `200`, body `{"code":<int>,"body":<HelloReply JSON>}` |

```bash
# Separate HTTP port (--http)
curl -X POST http://127.0.0.1:8080/api/greeter/say-hello \
  -H "Content-Type: application/json" \
  -d '{"name":"world"}'

# Unified port (--unified)
curl -X POST http://127.0.0.1:7999/api/greeter/say-hello \
  -H "Content-Type: application/json" \
  -d '{"name":"world"}'
```

### Example error responses

| Condition | HTTP status | Example body |
| --- | --- | --- |
| Unknown route | 404 | `{"error":"route not found"}` |
| Wrong method | 405 | `{"error":"method not allowed"}` |
| Non-JSON Content-Type | 415 | `{"error":"content type must be application/json"}` |
| Invalid JSON | 400 | `{"error":"invalid json body"}` |
| Connection not ready | 503 | `{"error":"connection not ready"}` |

Application errors are returned in the JSON `code` field (often HTTP `200` with non-zero `code`). CRpc binary errors use `resultCode` in the frame header.

## Result code ranges

| Range | Owner | Notes |
| --- | --- | --- |
| `0` | Success | Response body carries application payload when applicable |
| `1000–1999` | CRpc framework | See table below; error responses use an empty body unless noted |
| `2000–10000` | Unassigned | Do not use |
| `10001+` | Application services | Per-service or codegen-defined error codes |

## Framework result codes

Reserved range **1000–1999** for CRpc framework responses.

| Code | Name | When returned |
| --- | --- | --- |
| 0 | Ok | Success |
| 1001 | ServiceNotFound | Unknown `serviceId` on the server |
| 1002 | MethodNotFound | Known service, unknown `methodId` |
| 1003 | InvalidRequest | Malformed or invalid request (reserved) |
| 1004 | InternalError | Unhandled exception during dispatch |
| 1005 | Unavailable | Connection not ready or server unavailable |
| 1006 | DeadlineExceeded | Server-side deadline exceeded (reserved) |
