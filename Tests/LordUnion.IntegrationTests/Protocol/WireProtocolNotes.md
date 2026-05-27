# LordUnion Wire Protocol Notes (Task 1 Spike)

**Date:** 2026-05-25  
**Sources:** `jj.TKGameService.Runtime.Connect.NetworkPacketProcessor`, `ConnectService`, `LoginMsgSender`, `LoginMsgHandler`, `lordunion` `TcpProxyServer` mock proxy.

## Summary

Production client traffic uses an **8-byte little-endian TCP frame header** plus a **protobuf body**. Lobby/login and LordUnion game traffic share the same frame format. The production implementation lives in `Assets/JJService/tkgameservice/Runtime/Script/Connect/NetworkPacketProcessor.cs` (not in `JJCore` mock samples alone).

## Frame Header (8 bytes)

| Offset | Size | Field | Endian | Client send | Server receive (client decode) |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `header0` | little-endian uint32 | **Fixed `0x14801`** (`83969`) | **Contract / route id** (`MagicNum`) used by `IProtocolService.GetContractById(id)` |
| 4 | 4 | `bodyLength` | little-endian int32 | protobuf body length | protobuf body length |

Reference:

```114:161:c:/TKLobby/uengine/uframework/Assets/JJService/tkgameservice/Runtime/Script/Connect/NetworkPacketProcessor.cs
public bool MessageToPacketStream(IMessage message, MemoryStream stream)
{
    // ...
    stream.Position = 8;
    // ... encode protobuf body ...
    int magicNum = 0x14801;
    int packetLength = (int)stream.Position - 8;
    // write magicNum + packetLength to bytes 0..7
}
```

**Important:** `lordunion` editor `TcpProxyServer` writes **`gameId`** into the first 4 bytes for mock server replies. That matches the **server->client** routing id, not the fixed **`0x14801`** used by the real client on **client->server** sends.

## Body Root Messages

| Direction | Root protobuf type | Notes |
| --- | --- | --- |
| Client -> server | `TKMobileReqMsg` | Lobby/login via `lobby_req_msg`; match via `match_req_msg`; LordUnion via game-specific req wrapper |
| Server -> client | `TKMobileAckMsg` | Same layering with `lobby_ack_msg`, `match_ack_msg`, etc. |

Proto definitions (codegen source of truth):

- Lobby/match/mobile core: `Assets/JJService/tkgameservice/Editor/Resource/Proto/` — `TKMobile.proto`, `TKLobby.proto`, `TKMatch.proto`, `TKPartnerRoom.proto`, `Partnerroom/TKDefine.proto`
- LordUnion game: `Assets/JJGame/lordunion/Editor/Resource/Proto/` — `TKMobileLordUnion.proto`, `TKLord.proto`, and variant protos

Do **not** use `Assets/JJCore/core/Runtime/Script/MockServer/proto/` for integration-test codegen; that copy can lag runtime (e.g. missing `AnonymousBrowseAck.u64servertime = 18`). Generated output: `Protocol/Generated/LordUnionProtocol.g.cs` via `Protocol/GenerateLordUnionProto.ps1`.

V1 classic game messages route through `TKMobileLordUnionReqMsg` / `TKMobileLordUnionAckMsg` with `lord_req_msg` / `lord_ack_msg`.

## Compression / Encryption / Checksum

- **Frame level:** no extra compression/encryption/checksum beyond the 8-byte header + raw protobuf body.
- **Login JSON payload:** `CommonLoginReq.jsondata` uses **AES-128-ECB + PKCS7**, output as **uppercase hex ASCII**. Default key `"1kHL@65J"` until replaced by server `AnonymousBrowseAck.param`.
- **Algorithm port:** `Assets/JJCore/core/Runtime/Script/Crypt/CryptoUtil.cs` (`Aes128.GetInnerKey`, `Encrypt`, `Decrypt`).

## Login Sequence (required before password login)

1. TCP connect to lobby server (`115.182.5.66:30301` for integration test).
2. Send `TKMobileReqMsg.lobby_req_msg.anonymous_req_msg` (`AnonymousBrowseReq`).
   - Production sets `serialid` from client id (`LoginMsgSender.SendAnonymouseBrowseReq`).
3. Receive `TKMobileAckMsg.lobby_ack_msg.anonymous_ack_msg`.
   - **`AnonymousBrowseAck.param`** becomes the dynamic AES key (`LoginMsgHandler.OnAnonymousAckHandle`).
4. Send `CommonLoginReq` inside `TKMobileReqMsg.lobby_req_msg`.
   - `msgtype = 3588` (`MSG_TYPE_GENERAL_PASSWORD_LOGIN` / `LoginMessageType.GeneralPasswordLogin`).
   - Outer JSON wrapper (`CommonLoginContent`): `method=1003`, `version`, `timestamp`, `app_id`, `content`.
   - Inner password JSON (`GeneralPasswordLoginContent`): `login_type`, `login_name`, `password`, `password_type`, slider fields.
   - `jsondata` = AES hex of standardized outer JSON.
5. Receive `TKMobileAckMsg.lobby_ack_msg.commonlogin_ack_msg`.
   - **`TKMobileAckMsg.param`** carries login **error code** (`LoginMsgHandler.OnCommonloginAckHandle`).
   - Success populates `CommonLoginAck.userinfo.userid`.

## Message Id / Contract Mapping

Full production routing uses `IProtocolService` + generated `ProtocolMapperTKGameService`. For the V1 spike:

- **Encode (client send):** always header `0x14801` + protobuf body.
- **Decode (client receive):** read header route id, then decode body as `TKMobileAckMsg` directly (spike shortcut). Production also selects contract/decode root by route id.

Game traffic after login uses the **game id** in the **server->client** header (e.g. `1001` classic). Client->server game sends still use `0x14801` in production `NetworkPacketProcessor`.

## Same Codec for Login and Post-Login

Yes. `ConnectService` creates one TCP channel with one `NetworkPacketProcessor(8, ProtocolService)` for all lobby/match/game messages.

## Open Items for Live Spike

| Item | Status |
| --- | --- |
- Outer JSON `timestamp` uses **server millis** (`TimeUtil.CurrentServerMillis`): prefer `AnonymousBrowseAck.u64servertime` when present, else `servertime * 1000` plus elapsed local ms — not seconds and not raw local UTC.
| Exact `login_type` int for `LoginType.Password` | Use `1` initially; confirm against generated enum if login fails |
| `AnonymousBrowseReq.serialid` format | Use stable test serial; production uses PC client id |
| Full `IProtocolService` route-id table | Deferred to Task 3/4 proto pipeline; spike decodes `TKMobileAckMsg` directly |

## References

| Symbol | Location |
| --- | --- |
| `NetworkPacketProcessor` | `Assets/JJService/tkgameservice/Runtime/Script/Connect/NetworkPacketProcessor.cs` |
| `ConnectService` | `Assets/JJService/tkgameservice/Runtime/Script/Connect/ConnectService.cs` |
| `LoginMsgSender.SendCommonLoginReq` | `Assets/JJService/tkgameservice/Runtime/Script/Login/LoginMsgSender.cs` |
| `LoginMsgHandler` | `Assets/JJService/tkgameservice/Runtime/Script/Login/LoginMsgHandler.cs` |
| Mock proxy header/body split | `Assets/JJGame/lordunion/Editor/Script/MockServer/TcpProxyServer.cs` |
