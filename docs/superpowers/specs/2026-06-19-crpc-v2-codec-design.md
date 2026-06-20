# CRpc v2 Codec Design

**Date:** 2026-06-19  
**Status:** Proposed  
**Scope:** Replace the v1 binary wire codec with a simpler, fixed-header frame format. Breaking change — all .NET endpoints (server, client, gateway) migrate together. No v1 compatibility.

**Related:** `Doc/protocol.md`, `Doc/architecture-draft.md`, `docs/superpowers/specs/2026-05-28-crpc-server-push-design.md`, `docs/superpowers/specs/2026-06-01-crpc-gateway-design.md`.

---

## Goal

Redesign the CRpc TCP binary protocol codec to eliminate v1 pain points:

- Variable-length header driven by bit flags (`state`, ext header, compress metadata).
- Transport concerns (compress, encrypt) mixed with message type (request, response, push, ping).
- Weak tail checksum (`BPHashPartly`) and confusing `length` semantics (included magic bytes).
- Legacy naming (`module` / `command`) diverging from `serviceId` / `methodId` used everywhere else.
- Half-implemented compress/encrypt paths (`Debug.Assert(false)`, commented Java port code).

v2 keeps the internal dispatch contract unchanged:

```text
serviceId + methodId + reqSeq + body + IRpcContext
```

HTTP/JSON endpoints are unaffected — they never use the binary frame.

---

## Non-Goals (v2.0)

- Backwards compatibility with v1 wire format or other-language v1 clients.
- Application-layer checksum (integrity delegated to TCP / TLS).
- Compression implementation in v2.0 (wire fields reserved; `flags` always 0).
- Encryption, gateway ext header, Ping/Pong, session-close control frames.
- Dual-protocol negotiation or version downgrade on the same port.
- Changes to protobuf code generation options (`crpc.service_id`, `crpc.method_id`).

---

## Decisions

### 1. Breaking change — single protocol version

All binary endpoints switch to v2 together:

- `CRpcMessageEncoder` / `CRpcMessageDecoder`
- `CRpcClient`, `CRpcServerHandler`, `RpcServiceInvoker`
- `CRpcConnection.SendPushAsync`
- Gateway push relay (`GateWayPushRelay`)
- Codec unit and integration tests

No v1 fallback listener. Old magic `0x5F3759DF` is retired.

### 2. Fixed 24-byte message header

Every frame uses the same header size. No conditional ext-header or compress-size fields. Decoder reads exactly 24 bytes after the frame prefix, then `body.Length = payloadLen - 24`.

### 3. Explicit `messageType` byte — not a state bitmask

Replace `CRpcMessageState` bit flags for routing with a single `messageType` enum byte:

| Value | Name | Direction |
| --- | --- | --- |
| 0 | Request | Client → Server |
| 1 | Response | Server → Client |
| 2 | Push | Server → Client |

Client routing:

```text
messageType == Response -> complete pending CallAsync by reqSeq
messageType == Push     -> dispatch to generated push handler
otherwise on client     -> log / ignore (protocol error path)
```

Server routing:

```text
messageType == Request -> invoke IRpcService via RpcServiceInvoker
otherwise on server    -> log / ignore
```

Push semantics match `2026-05-28-crpc-server-push-design.md`: `reqSeq = 0`, `resultCode = 0`, fire-and-forget from server, no client response.

### 4. Rename wire fields to `serviceId` / `methodId`

Drop `module` / `command` naming in codec types, logs, and tests. Align wire layout with proto options and `IRpcMessage` surface.

### 5. No application-layer checksum

Remove `ChecksumsUtil`, tail 4-byte hash, and checksum validation in the decoder. Framing relies on `magic` + `payloadLen`. Integrity relies on TCP; production deployments should use TLS when needed.

### 6. Compression — format reserved, implementation deferred (Strategy 1)

Header includes `flags` (bit 0 = `COMPRESSED`) and `bodyOriginLen` (uncompressed protobuf size for zstd). v2.0 always sets `flags = 0` and `bodyOriginLen = body.Length`. Future work adds zstd + `compressThreshold` in encoder/decoder without wire changes.

---

## Wire Format

All multi-byte integers are **little-endian**.

### Frame layout

```text
┌──────────┬─────────────┬────────────────────┬──────────────┐
│ magic    │ payloadLen  │ fixed header (24B) │ protobuf body│
│ 4 bytes  │ 4 bytes     │                    │ N bytes      │
└──────────┴─────────────┴────────────────────┴──────────────┘
```

| Field | Size | Value / rule |
| --- | --- | --- |
| `magic` | 4 | `0x43525043` — ASCII `'CRPC'` (bytes on wire: `43 52 50 43`) |
| `payloadLen` | 4 | `24 + body.Length`. Does **not** include `magic` or `payloadLen`. |
| header | 24 | Fixed layout below |
| body | N | Raw protobuf in v2.0 |

Minimum frame size: `4 + 4 + 24 = 32` bytes (empty body).

### Fixed header (24 bytes)

| Offset | Size | Field | v2.0 rule |
| --- | --- | --- | --- |
| 0 | 1 | `version` | `1` |
| 1 | 1 | `messageType` | `0` Request, `1` Response, `2` Push |
| 2 | 1 | `flags` | `0` (bit 0 `COMPRESSED = 0x01` reserved) |
| 3 | 1 | `reserved` | `0` |
| 4 | 2 | `serviceId` | Proto `crpc.service_id` |
| 6 | 2 | `methodId` | Proto `crpc.method_id` |
| 8 | 8 | `reqSeq` | Request/Response: client-assigned sequence; Push: `0` |
| 16 | 4 | `resultCode` | Response: business/error code; Request/Push: `0` |
| 20 | 4 | `bodyOriginLen` | v2.0: equals `body.Length`; future: uncompressed size when `COMPRESSED` |

### Per-message field conventions

| messageType | reqSeq | resultCode | body |
| --- | --- | --- | --- |
| Request | `> 0` | `0` | Request protobuf |
| Response | Same as matching request | Set by service / framework | Response protobuf (may be empty on error) |
| Push | `0` | `0` | Push protobuf |

### v1 → v2 comparison

| Aspect | v1 | v2 |
| --- | --- | --- |
| Magic | `0x5F3759DF` (easter egg) | `0x43525043` (`'CRPC'`) |
| Length field | Included magic in packet size | `payloadLen` = header + body only |
| Message kind | `state` bitmask | `messageType` single byte |
| Service routing | `module` / `command` | `serviceId` / `methodId` |
| Tail checksum | `BPHashPartly` 4 bytes | None |
| Ext header | Optional, variable | None |
| Compress metadata | Variable (+4 when compressed) | Fixed `bodyOriginLen`; `flags` bit |
| Header size | 18 + optional ext + optional compress | Fixed 24 |

---

## Encoder / Decoder

### DotNetty framing

Use `LengthFieldBasedFrameDecoder` with:

| Parameter | Value |
| --- | --- |
| `lengthFieldOffset` | 4 |
| `lengthFieldLength` | 4 |
| `lengthAdjustment` | 0 |
| `initialBytesToStrip` | 8 (strip `magic` + `payloadLen`) |

After strip: read 24-byte header, then `payloadLen - 24` body bytes.

### Encoder steps

1. Build header from `messageType`, `serviceId`, `methodId`, `reqSeq`, `resultCode`.
2. Set `flags = 0`, `bodyOriginLen = body.Length`, `version = 1`, `reserved = 0`.
3. v2.0: body is raw protobuf (no compress).
4. Write `magic`, `payloadLen`, header, body to `IByteBuffer`.
5. Do **not** call v1 `encryptAndCompress`.

### Decoder steps

1. Validate `magic == 0x43525043`.
2. Validate `version == 1`.
3. Validate `messageType` in `0..2`.
4. Validate `payloadLen >= 24` and `<= maxFrameLength`.
5. Parse fixed header; read body.
6. v2.0: if `flags != 0`, treat as protocol error (future: decompress when `COMPRESSED`).
7. Construct `CRpcMessage` (or renamed type) for handler pipeline.

### Error handling

| Condition | Action |
| --- | --- |
| Bad magic, bad version, unknown `messageType`, `payloadLen` out of range | Close connection (same severity as v1 decoder) |
| Protobuf parse failure in business layer | Return error `resultCode` on Response; do not tear down TCP for a single bad RPC |

---

## Internal API Changes

### Types

| v1 | v2 |
| --- | --- |
| `CRpcMessageHeader` with `module`, `command`, `state` | `CRpcFrameHeader` (or refactored `CRpcMessageHeader`) with `ServiceId`, `MethodId`, `MessageType`, `Flags`, `BodyOriginLen` |
| `CRpcMessageState` constants | `CRpcMessageType` enum + `CRpcFrameFlags` for reserved compress bit |
| `getModule()` / `getCommand()` | `ServiceId` / `MethodId` only |
| `hasState(STATE_PUSH)` etc. | `MessageType == Push` |
| `ChecksumsUtil` | Remove |
| `encryptAndCompress(...)` | Remove from v2.0 encoder path |

### `IRpcMessage` (conceptual)

Keep the dispatch-facing surface:

```csharp
CRpcMessageType MessageType { get; }
ushort ServiceId { get; }
ushort MethodId { get; }
long ReqSequence { get; }
int ResultCode { get; }
byte[] Body { get; }
```

### Files to update

| Area | Files |
| --- | --- |
| Codec | `CRpcMessage.cs`, `CRpcMessageHeader.cs`, `CRpcMessageEncoder.cs`, `CRpcMessageDecoder.cs`; delete or gut `CRpcMessageState.cs`, `ChecksumsUtil.cs` |
| Server | `CRpcServerHandler.cs`, `RpcServiceInvoker.cs`, `CRpcConnection.cs` |
| Client | `CRpcClient.cs`, `CRpcClientHandler.cs`, `CRpcClientPipelineFactory.cs` |
| Gateway | `GateWayServerHandler.cs`, `GateWayPushRelay` (or equivalent), `GateWayServiceImpl.cs` |
| Tests | `CRpcMessageEncoderTests.cs`, `CRpcConnectionTests.cs`, `CRpcClientTests.cs`, `CRpcServerHandlerTests.cs`, gateway tests, push integration tests |
| Docs | `Doc/protocol.md` — summarize v2 wire format and link here |

HTTP handler (`HttpServerHandler`, `IRpcHttpJsonCodec`) unchanged.

---

## Future: Compression (post v2.0)

When enabled:

```text
Encode: protobuf bytes -> (if len >= threshold) zstd -> set flags|=COMPRESSED, bodyOriginLen=original len
Decode: if COMPRESSED -> zstd decompress to bodyOriginLen -> protobuf parse
```

Algorithm: zstd (consistent with v1 commented intent). Only body is compressed; header stays plaintext. Symmetric on client and server unless a later policy restricts direction.

---

## Testing

1. **Round-trip unit tests** — Request, Response, Push frames with empty and non-empty body; assert byte layout including offsets and `payloadLen`.
2. **Decoder edge cases** — bad magic, bad version, invalid `messageType`, `payloadLen < 22`, truncated body.
3. **Integration** — HelloWorld RPC, server push, gateway push relay with v2 frames.
4. **Regression** — pending call matching by `reqSeq` on Response; Push does not complete pending calls.

---

## Migration Checklist

- [ ] Implement v2 codec types and encoder/decoder
- [ ] Remove v1 checksum and compress/encrypt dead code
- [ ] Update server/client/gateway handlers for `CRpcMessageType`
- [ ] Update all tests and test helpers (`CreateRequest`, etc.)
- [ ] Fill `Doc/protocol.md` with v2 summary
- [ ] Verify HelloWorld + Gateway examples end-to-end

---

## Open Items

None for v2.0 wire format. Compression implementation tracked as follow-up after v2.0 ships with `flags = 0`.
