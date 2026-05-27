# LordUnion Integration Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone .NET runner that uses `orientnet` to connect to the real server, logs in three configured accounts, signs them up for the same classic Dou Dizhu match, enters one table, and drives one full game with a minimal legal bot.

**Architecture:** Keep the runner independent from Unity. Generate protocol code from `uframework` `.proto` files, implement a `ServerProtocolCodec` that recreates the real `IConnectService` / `ITCPPacketProcessor` wire format, run each account through an `AccountSession`, and keep classic Dou Dizhu behavior behind `ClassicLordVariant`.

**Tech Stack:** C# / .NET 8, `orientnet` / `CRpcLoop`, protobuf-generated message types, xUnit for non-live tests, opt-in real-server smoke tests.

**Spec reference:** `docs/superpowers/specs/2026-05-25-lordunion-integration-test-design.md`

---

## File Structure

| File / Directory | Responsibility |
| --- | --- |
| `Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj` | Test runner / integration harness project |
| `Tests/LordUnion.IntegrationTests/Config/LordUnionTestConfig.cs` | Server, accounts, match entry, timeout config |
| `Tests/LordUnion.IntegrationTests/Protocol/Generated/` | Generated protobuf C# output |
| `Tests/LordUnion.IntegrationTests/Protocol/ServerProtocolCodec.cs` | Real server frame/header/body codec |
| `Tests/LordUnion.IntegrationTests/Protocol/ProtocolMessage.cs` | Decoded message envelope |
| `Tests/LordUnion.IntegrationTests/Sessions/AccountSession.cs` | One account connection and phase state |
| `Tests/LordUnion.IntegrationTests/Sessions/AccountSessionState.cs` | Session state enum |
| `Tests/LordUnion.IntegrationTests/Flows/LoginFlow.cs` | Connect and login flow |
| `Tests/LordUnion.IntegrationTests/Flows/SignupFlow.cs` | Same-match signup flow |
| `Tests/LordUnion.IntegrationTests/Flows/EnterMatchFlow.cs` | Enter match / enter round flow |
| `Tests/LordUnion.IntegrationTests/Flows/GameFlow.cs` | Variant-neutral game loop |
| `Tests/LordUnion.IntegrationTests/GameVariants/ILordGameVariant.cs` | Variant adapter boundary |
| `Tests/LordUnion.IntegrationTests/GameVariants/ClassicLordVariant.cs` | `TKLord.proto` adapter |
| `Tests/LordUnion.IntegrationTests/Bots/MinimalLandlordBot.cs` | Minimal legal classic Dou Dizhu bot |
| `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs` | Three-account scenario orchestration |
| `Tests/LordUnion.IntegrationTests/Reporting/ScenarioReport.cs` | Result report DTO and formatting |
| `Tests/LordUnion.IntegrationTests/appsettings.example.json` | Safe sample config with no secrets |
| `Tests/CRPC.Tests/LordUnion/` | Unit tests for codec/session/bot pieces that do not require live server |

---

## Task 1: Wire Protocol Discovery Spike

**Files:**
- Read/reference only: `uframework` `IConnectService`, `ITCPPacketProcessor`, generated protocol mapper, or captured packets
- Create notes: `Tests/LordUnion.IntegrationTests/Protocol/WireProtocolNotes.md`
- Create spike tests: `Tests/CRPC.Tests/LordUnion/ServerProtocolCodecSpikeTests.cs`

- [ ] **Step 1: Locate the real connect/packet implementation**

Search `uframework` for these symbols and record findings:

```text
IConnectService
ConnectService
CreateMsg<T>
SendMessage
ITCPPacketProcessor
PacketHeaderLength
DeserializePacketHeader
MessageToPacketStream
PacketStreamToMessage
```

If the implementation is not visible in source, inspect loaded/generated assemblies or use captured traffic from the Unity client.

- [ ] **Step 2: Document the wire format**

Create `Tests/LordUnion.IntegrationTests/Protocol/WireProtocolNotes.md` with:

- Header length.
- Header field order and endian-ness.
- Body root type for lobby/login messages.
- Body root type for LordUnion game messages.
- Message id / descriptor id / module id mapping.
- Compression, encryption, hash, or checksum rules.
- Whether login and post-login packets use the same codec.

- [ ] **Step 3: Add codec spike tests from known bytes**

Create `ServerProtocolCodecSpikeTests` using captured or synthetic packet bytes that represent at least one login response.

Tests should prove:

- Header decode returns the expected body length and routing fields.
- Body decode creates the expected generated root message.
- Re-encoding a simple request creates bytes with the expected header and protobuf body length.

- [ ] **Step 4: Prove live login encode/decode**

Add an opt-in live spike path guarded by config, environment variable, or explicit command-line flag. It must:

1. Connect to the configured test server (default: **`115.182.5.66:30301`**).
2. Encode and send one login request.
3. Decode one login response.
4. Print the decoded user id or server error.

Do not proceed to signup/game-flow implementation until live login returns `userid` or equivalent captured-packet proof is available. As of Task 1 spike, TCP + `AnonymousBrowse` + `CommonLogin` encode/decode against `115.182.5.66:30301` is proven; login business params (`app_id`, possibly `login_type`) still need tuning.

---

## Task 2: Test Runner Project and Configuration

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj`
- Create: `Tests/LordUnion.IntegrationTests/Program.cs`
- Create: `Tests/LordUnion.IntegrationTests/Config/LordUnionTestConfig.cs`
- Create: `Tests/LordUnion.IntegrationTests/appsettings.example.json`

- [ ] **Step 1: Create standalone runner project**

Add a .NET console project under `Tests/LordUnion.IntegrationTests`. Reference the local `CRpc` project and protobuf packages used by the generated code.

- [ ] **Step 2: Add config model**

Define:

- Server host and port (defaults: `115.182.5.66`, `30301`).
- Protocol options discovered in Task 1.
- Three account aliases and credentials (defaults):

  | Alias | Username | Password |
  | --- | --- | --- |
  | player1 | `TJJ006628` | `3YXRQW` |
  | player2 | `TJJ006629` | `3YRQ83` |
  | player3 | `TJJ006630` | `Q5EDHU` |

- Match target (defaults: 经典斗 自由桌 — `gameId` `1001`, `productId` `2008280`, `tourneyId` `159740`).

  Other presets on the same test server:

  | Variant | `gameId` | `productId` (mpid) | `tourneyId` |
  | --- | --- | --- | --- |
  | 赖斗 | `1010` | `2008390` | `159830` |
  | 欢斗 | `1019` | `2008391` | `159829` |
  | 闪斗 | `1054` | `2008392` | `159831` |

- Per-phase timeouts.
- Output directory.

- [ ] **Step 3: Add local sample config**

Create `appsettings.local.json.example` (or document the shape in README) and add `appsettings.local.json` to `.gitignore`. Default V1 values:

```json
{
  "Server": {
    "Host": "115.182.5.66",
    "Port": 30301
  },
  "Match": {
    "GameId": 1001,
    "ProductId": 2008280,
    "TourneyId": 159740
  },
  "Accounts": [
    { "Alias": "player1", "Username": "TJJ006628", "Password": "3YXRQW" },
    { "Alias": "player2", "Username": "TJJ006629", "Password": "3YRQ83" },
    { "Alias": "player3", "Username": "TJJ006630", "Password": "Q5EDHU" }
  ]
}
```

All four variants on this test server are **自由桌**. V1 smoke uses the `Match` block above (经典斗). Switch `GameId` / `ProductId` / `TourneyId` to another row when testing 赖斗 / 欢斗 / 闪斗 later.

Do not commit session tokens or production credentials. The three accounts above are dedicated integration-test accounts documented in the spec.

- [ ] **Step 4: Add command-line guard for live tests**

The runner should refuse live server execution unless the user explicitly passes a flag such as `--live` and a real local config path.

---

## Task 3: Protocol Generation Pipeline

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Protocol/Generated/`
- Create: `Tests/LordUnion.IntegrationTests/Protocol/GenerateLordUnionProto.ps1` or equivalent script
- Reference source (lobby/match core): `Assets/JJService/tkgameservice/Editor/Resource/Proto/` — `TKMobile.proto`, `TKLobby.proto`, `TKMatch.proto`, `TKPartnerRoom.proto`, `Partnerroom/TKDefine.proto`
- Reference source (LordUnion game): `Assets/JJGame/lordunion/Editor/Resource/Proto/` — `TKMobileLordUnion.proto`, `TKLord.proto`, and variant protos imported by `TKMobileLordUnion.proto`

Do **not** use `Assets/JJCore/core/Runtime/Script/MockServer/proto/`; it is a mock-server copy and can miss runtime fields (e.g. `u64servertime`).

- [x] **Step 1: Generate V1 protocol types**

Generate C# for:

- `TKLobby.proto`
- `TKMatch.proto`
- `TKMobileLordUnion.proto`
- `TKLord.proto`
- `TKRoomInfo.proto` only if the server entry path requires room messages

- [x] **Step 2: Keep classic-only runtime references**

V1 code may generate all imports needed by `TKMobileLordUnion.proto`, but runtime flow should only route `lord_req_msg` and `lord_ack_msg`.

- [x] **Step 3: Verify generated code compiles outside Unity**

Run the runner project build. If generated code depends on Unity-only helpers, adjust the generator/script to emit plain .NET-compatible protocol types.

---

## Task 4: ServerProtocolCodec

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Protocol/ServerProtocolCodec.cs`
- Create: `Tests/LordUnion.IntegrationTests/Protocol/ProtocolMessage.cs`
- Test: `Tests/CRPC.Tests/LordUnion/ServerProtocolCodecTests.cs`

- [ ] **Step 1: Implement header encode/decode**

Use the Task 1 wire-format findings. Tests should cover valid packet, incomplete header, invalid body length, and unknown routing fields.

- [ ] **Step 2: Implement protobuf body encode/decode**

Support the minimum messages needed for:

- Login request/response.
- Signup request/response.
- Enter match / enter round.
- `TKMobileLordUnionReqMsg` / `TKMobileLordUnionAckMsg` classic Lord messages.

- [ ] **Step 3: Add error reporting**

Decode failures should include account alias, phase, header fields, body length, and message id where available.

- [ ] **Step 4: Re-run spike proof**

Confirm the production codec still passes the live login or captured-packet proof from Task 1.

---

## Task 5: AccountSession and Message Routing

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Sessions/AccountSession.cs`
- Create: `Tests/LordUnion.IntegrationTests/Sessions/AccountSessionState.cs`
- Create: `Tests/LordUnion.IntegrationTests/Sessions/SessionMessageRouter.cs`
- Test: `Tests/CRPC.Tests/LordUnion/AccountSessionTests.cs`

- [x] **Step 1: Define session state**

States:

```text
Disconnected
Connecting
Connected
LoggedIn
SignedUp
WaitingForMatch
EnteringMatch
InGame
Finished
Failed
```

- [x] **Step 2: Add send/receive logging hooks**

Every sent and received message records account alias, phase, message name, routing fields, key ids, and timestamp.

- [x] **Step 3: Add phase waits with timeouts**

Each flow waits for specific decoded messages or fails with a named timeout.

- [x] **Step 4: Route decoded messages**

Route lobby, match, and LordUnion game messages to the current flow or variant adapter.

---

## Task 6: Login and Signup Flows

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Flows/LoginFlow.cs`
- Create: `Tests/LordUnion.IntegrationTests/Flows/SignupFlow.cs`
- Test: `Tests/CRPC.Tests/LordUnion/LoginFlowTests.cs`
- Test: `Tests/CRPC.Tests/LordUnion/SignupFlowTests.cs`

- [x] **Step 1: Implement login flow**

Connect, send the selected login request type, wait for success/failure, and store `userid`, `nickname`, tokens, and any server route information needed by later flows.

- [x] **Step 2: Implement signup flow**

Send signup for the configured `gameId`, `productId` (mpid), and `tourneyId` (自由桌). Wait for signup acknowledgement.

- [x] **Step 3: Add fake-message tests**

Use fake decoded messages to verify success, server failure, wrong account, and timeout behavior.

---

## Task 7: Enter Match / Enter Round Flow

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Flows/EnterMatchFlow.cs`
- Test: `Tests/CRPC.Tests/LordUnion/EnterMatchFlowTests.cs`

- [x] **Step 1: Wait for match start/table information**

Capture `matchid`, `ticket`, `tourneyid`, `matchpoint`, `tableid`, seat mapping, and any required server start data.

- [x] **Step 2: Send enter-match and enter-round requests**

Use the correct `TKMatch.proto` messages and validate acknowledgements.

- [x] **Step 3: Verify same-table condition**

All three sessions must agree on table id and seat/user mapping. If not, fail with a clear scenario error.

---

## Task 8: ClassicLordVariant and Variant-Neutral Game Events

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/GameVariants/ILordGameVariant.cs`
- Create: `Tests/LordUnion.IntegrationTests/GameVariants/GameEvent.cs`
- Create: `Tests/LordUnion.IntegrationTests/GameVariants/ClassicLordVariant.cs`
- Test: `Tests/CRPC.Tests/LordUnion/ClassicLordVariantTests.cs`

- [x] **Step 1: Define variant-neutral events**

Events:

```text
ReadyRequested
GameStarted
CardsDealt
BidRequested
LandlordDeclared
TurnStarted
CardsPlayed
PassPlayed
GameFinished
```

- [x] **Step 2: Decode classic Lord ACKs**

Map `LordWaitClientReadyAck`, `LordGameStartAck`, `LordInitCardAck`, `LordCallScoreAck`, `LordInitBottomCardAck`, `LordOperateStartAck`, `LordTakeoutCardAck`, and `LordResultAck`.

- [x] **Step 3: Build classic Lord REQs**

Build `LordClientReadyReq`, `LordCallScoreReq`, `LordForceDeclareLoadReq` if required, and `LordTakeoutCardReq`.

- [x] **Step 4: Add unit tests**

Each supported ACK should produce a deterministic neutral event. Each bot decision should become the expected classic request wrapper.

---

## Task 9: MinimalLandlordBot

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Bots/MinimalLandlordBot.cs`
- Create: `Tests/LordUnion.IntegrationTests/Bots/CardCodec.cs`
- Test: `Tests/CRPC.Tests/LordUnion/MinimalLandlordBotTests.cs`
- Test: `Tests/CRPC.Tests/LordUnion/CardCodecTests.cs`

- [x] **Step 1: Implement card byte codec**

Map server `bytes cards` to a simple internal card model. Confirm with `uframework` `Card.Byte` / `CardsMgr` behavior or captured packets.

- [x] **Step 2: Implement deterministic bid policy**

Use a fixed policy that is legal and predictable.

- [x] **Step 3: Implement minimal play policy**

When leading, play the smallest supported legal card/group. When following, play the smallest supported legal response; otherwise pass when legal.

- [x] **Step 4: Add deterministic tests**

Cover single card, pair, simple pass, and end-of-hand update cases.

---

## Task 10: GameFlow and ThreePlayersOneGameScenario

**Files:**
- Create: `Tests/LordUnion.IntegrationTests/Flows/GameFlow.cs`
- Create: `Tests/LordUnion.IntegrationTests/Scenarios/ThreePlayersOneGameScenario.cs`
- Create: `Tests/LordUnion.IntegrationTests/Reporting/ScenarioReport.cs`
- Test: `Tests/CRPC.Tests/LordUnion/GameFlowTests.cs`
- Test: `Tests/CRPC.Tests/LordUnion/ThreePlayersOneGameScenarioTests.cs`

- [x] **Step 1: Implement GameFlow**

Consume variant-neutral events, update per-session game state, ask the bot for decisions, and send variant requests.

- [x] **Step 2: Implement scenario orchestration**

Run:

```text
connect all
login all
signup all
enter match / round all
verify same table
drive game until GameFinished
write report
```

- [x] **Step 3: Add failure aggregation**

When one session fails, cancel the scenario and report the first failure with account, phase, last sent message, last received message, and timeout/error.

- [x] **Step 4: Add fake-server tests**

Use fake decoded messages to verify scenario success and failure paths without a live server.

---

## Task 11: Live Smoke Test and Reporting

**Files:**
- Modify: `Tests/LordUnion.IntegrationTests/Program.cs`
- Create: `Tests/LordUnion.IntegrationTests/Reporting/ReportWriter.cs`

- [ ] **Step 1: Add report output**

Write console summary and optional JSON report with:

- Scenario result.
- Account timings.
- Table id and seat mapping.
- Game result.
- First failure detail.

- [ ] **Step 2: Add live smoke command**

Support:

```bash
dotnet run --project Tests/LordUnion.IntegrationTests -- --live --config path/to/local.json
```

- [ ] **Step 3: Run against test server**

Default endpoint: `115.182.5.66:30301`. Expected: three configured accounts complete one classic Dou Dizhu game, or the report identifies the exact blocking phase.

---

## Validation Checklist

- [ ] `dotnet build` passes for the runner and tests.
- [ ] Codec tests pass with captured/synthetic packets.
- [ ] Live login spike passes before signup/game tasks begin.
- [ ] Session and flow tests pass without live server.
- [ ] Bot tests pass for deterministic hands.
- [ ] Opt-in live smoke either completes one game or prints an actionable failure report.
- [ ] No session tokens or production credentials are committed (`appsettings.local.json` remains gitignored).
