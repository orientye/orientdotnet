# CRpc Binary Protocol (v2)

Full design: `docs/superpowers/specs/2026-06-19-crpc-v2-codec-design.md`

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
| 1 | `messageType` — 0 Request, 1 Response, 2 Push |
| 2 | `flags` — 0 in v2.0 (`0x01` = compressed, reserved) |
| 3 | `reserved` = 0 |
| 4 | `serviceId` u16 |
| 6 | `methodId` u16 |
| 8 | `reqSeq` u64 |
| 16 | `resultCode` i32 |
| 20 | `bodyOriginLen` u32 |

## Body

Raw protobuf bytes. No application-layer checksum in v2.0.

## Message conventions

| Type | reqSeq | resultCode |
| --- | --- | --- |
| Request | > 0 | 0 |
| Response | matches request | service result |
| Push | 0 | 0 |
