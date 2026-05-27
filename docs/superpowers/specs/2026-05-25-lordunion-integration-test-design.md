# LordUnion Integration Test Design

**Date:** 2026-05-25  
**Status:** Proposed

## Goal

Build a standalone .NET integration test runner for Dou Dizhu (LordUnion) that uses `orientnet` to communicate with the real game server protocol.

The first version does not do pressure testing. It proves the full real-server flow with three accounts:

1. Connect to the configured test server (default: `115.182.5.66:30301`).
2. Log in with three configured accounts.
3. Sign all three accounts up for the same match entry.
4. Wait for the server to match them into the same table.
5. Enter the Dou Dizhu game flow.
6. Drive the game with a minimal legal bot until one full game completes.
7. Print a structured result report with timings and the first failure, if any.

## Non-Goals

V1 deliberately avoids these features:

- Large-scale pressure testing.
- Distributed load generation.
- Unity runtime or asset dependencies.
- Full client parity with the production LordUnion UI.
- Smart card strategy. The bot only needs to make legal progress.
- Broad protocol coverage beyond the messages required for the three-account one-game scenario.

## Decision

Use option A: a standalone .NET runner with generated protocol code and an explicit server protocol adapter.

The runner lives in the `orientdotnet` repository because the test harness is built around `orientnet` and should run as a normal .NET process. The Unity `uframework` repository remains the source for protocol definitions and LordUnion behavior references, but V1 should not depend on Unity runtime services.

Protocol message classes are generated from `.proto` files into a dedicated test protocol assembly. The test code should not hand-write protobuf message models and should not directly reference Unity runtime-generated protocol code.

## Architecture

The system is split into small units with clear responsibilities:

```text
LordUnion.IntegrationTests
  Protocol.Generated
  ServerProtocolCodec
  AccountSession
  MessageRouters
  GameVariants
    ILordGameVariant
    ClassicLordVariant
  Flows
    LoginFlow
    SignupFlow
    EnterMatchFlow
    GameFlow
  Bots
    MinimalLandlordBot
  Scenarios
    ThreePlayersOneGameScenario
```

### `Protocol.Generated`

Contains C# code generated from the required `.proto` files, starting with:

- `TKLobby.proto`
- `TKMatch.proto`
- `TKMobileLordUnion.proto`
- `TKLord.proto`
- `TKRoomInfo.proto`, if room-entry messages are required by the selected test server path

V1 only supports classic Dou Dizhu through `TKLord.proto`: outbound game messages use `TKMobileReqMsg.lord_req_msg`, and inbound game messages use `TKMobileAckMsg.lord_ack_msg`.

The protocol generation and project layout must still leave room for later variants imported by `TKMobileLordUnion.proto`, such as `TKLZLord.proto`, `TKHLLord.proto`, `TKSDLord.proto`, `TKHZLord.proto`, `TKDDLord.proto`, `TKDBLord.proto`, and `TKDJLord.proto`.

**Proto source roots in `uframework`:**

- Lobby/match/mobile core: `Assets/JJService/tkgameservice/Editor/Resource/Proto/` (Unity client/runtime definitions)
- LordUnion game: `Assets/JJGame/lordunion/Editor/Resource/Proto/`
- Avoid `Assets/JJCore/core/Runtime/Script/MockServer/proto/` for codegen; it is for local mock servers and may not match live-server wire types.

This assembly is data-only. It should not know about sockets, sessions, logging, test orchestration, or bot decisions.

### `ServerProtocolCodec`

Owns the real server wire protocol:

- Frame boundaries.
- Header fields.
- Message identifiers.
- Mapping between message identifiers and generated protobuf bodies.
- Compression, encryption, checksum, or hash handling if required by the server protocol.
- Decode failures with enough context to identify the message and account.

Business flows must not parse raw bytes directly. They receive decoded messages and send typed requests through this codec.

### `AccountSession`

Represents one account's connection and state. Each of the three accounts has one session.

It owns:

- The `orientnet` client connection.
- Current session state: disconnected, connected, logged in, signed up, waiting for match, entering game, in game, finished, failed.
- User and match identifiers: `userid`, `nickname`, `tourneyid`, `matchid`, `matchpoint`, `tableid`, seat mapping, and ticket data.
- Request/response waits and phase timeouts.
- Per-account structured logs.

All server messages are routed through the session before reaching a flow or bot.

### `MessageRouters`

Routes decoded messages by protocol area:

- Lobby messages: login, signup, match entry, table assignment, and server errors.
- Match/game messages: enter match, enter round, table init, hand start, landlord selection, card actions, hand/game over.
- LordUnion game messages: route `TKMobileAckMsg` through the selected game variant adapter. V1 selects `ClassicLordVariant`.

The router should be explicit. Unknown required messages fail the scenario with the raw message id and current account phase. Unknown optional push messages can be logged and ignored only when the flow can still make progress.

### `GameVariants`

The game layer is variant-aware even though V1 implements only classic Dou Dizhu.

`ILordGameVariant` defines the boundary between game-specific protobuf messages and the shared `GameFlow`:

```text
ILordGameVariant
  VariantId
  CanHandle(TKMobileAckMsg ack)
  DecodeGameEvent(TKMobileAckMsg ack)
  BuildReadyReq(...)
  BuildBidReq(...)
  BuildPlayCardsReq(...)
  BuildPassReq(...)
```

`ClassicLordVariant` is the only V1 implementation. It converts classic `LordAckMsg` messages into shared game events and builds classic `LordReqMsg` requests.

Later variants should add new adapters without rewriting `GameFlow`. Test-server **自由桌** presets:

| Variant | Game id | Product id (mpid) | Tourney id | Adapter (future) |
| --- | --- | --- | --- | --- |
| 经典斗 | `1001` | `2008280` | `159740` | `ClassicLordVariant` (V1) |
| 赖斗 | `1010` | `2008390` | `159830` | `LzLordVariant` |
| 欢斗 | `1019` | `2008391` | `159829` | `HlLordVariant` |
| 闪斗 | `1054` | `2008392` | `159831` | TBD |

```text
LzLordVariant       // TKLZLord.proto, lzlord_ack_msg / lzlord_req_msg
HlLordVariant       // TKHLLord.proto, hllord_ack_msg / hllord_req_msg
SdLordVariant       // TKSDLord.proto (or other proto for 闪斗, confirm during implementation)
```

The shared `GameFlow` should work with variant-neutral events such as:

```text
GameStarted
ReadyRequested
CardsDealt
BidRequested
LandlordDeclared
TurnStarted
CardsPlayed
PassPlayed
GameFinished
```

## Scenario Flow

`ThreePlayersOneGameScenario` is the only required V1 scenario.

```text
Load config
  -> create three AccountSession instances
  -> connect all sessions
  -> login all sessions
  -> signup all sessions for the same tourney (自由桌: use configured `tourneyId` + `productId`)
  -> wait until all sessions receive match start / table information
  -> enter match / enter round as required by the server protocol
  -> verify all three accounts are at the same table
  -> run GameFlow with MinimalLandlordBot for each session
  -> stop after one completed game
  -> write scenario report
```

The scenario succeeds only when all three accounts finish the same game.

If one account fails, the scenario fails as a whole and reports:

- Failed account.
- Current phase.
- Last sent message.
- Last received message.
- Server error code or local exception.
- Timeout name, if the failure is a timeout.

## Game Bot

`MinimalLandlordBot` is intentionally simple.

It maintains only the state needed to send legal responses:

- Current hand cards.
- Seat order and landlord identity.
- Current turn owner.
- Last played cards and whether the bot is allowed to pass.
- Current phase, such as bidding, landlord selection, playing, or game over.

V1 behavior:

- During bidding or landlord selection, use a deterministic fixed policy.
- When leading, play the smallest legal card or smallest legal card group supported by V1.
- When following, play the smallest legal response that beats the previous play.
- Pass when no legal response exists and passing is allowed.
- Do not optimize for winning.

The server remains authoritative. If local state conflicts with server messages, the session fails fast with a state-sync error rather than guessing.

Future versions can replace this bot with a strategy that reuses LordUnion card prompt logic, but V1 should keep the bot independent and minimal.

## Configuration

The runner is configured by a local config file or command-line arguments. V1 requires:

- Server host and port. The default integration-test server endpoint is:
  - **Host:** `115.182.5.66`
  - **Port:** `30301`
- Protocol options required by the server codec.
- Three account credentials. The default V1 integration-test accounts are:

  | Alias | Username | Password |
  | --- | --- | --- |
  | player1 | `TJJ006628` | `3YXRQW` |
  | player2 | `TJJ006629` | `3YRQ83` |
  | player3 | `TJJ006630` | `Q5EDHU` |

- Match target for signup. All listed tables are **自由桌** (free/casual tables). V1 defaults to **经典斗**:

  | Field | V1 default | Notes |
  | --- | --- | --- |
  | `gameId` | `1001` | 经典斗 |
  | `productId` (mpid) | `2008280` | |
  | `tourneyId` | `159740` | Same tourney for all three accounts |

  Other test-server presets (for later variant work):

  | Variant | `gameId` | `productId` (mpid) | `tourneyId` |
  | --- | --- | --- | --- |
  | 赖斗 | `1010` | `2008390` | `159830` |
  | 欢斗 | `1019` | `2008391` | `159829` |
  | 闪斗 | `1054` | `2008392` | `159831` |

- Per-phase timeout values.
- Output directory for logs and reports.

Secrets such as session tokens must not be committed. Account passwords for the dedicated test accounts above may be referenced in local runner config (`appsettings.local.json`, gitignored); do not copy them into application source code.

## Timeouts

Every phase has a bounded timeout:

- `ConnectTimeout`
- `LoginTimeout`
- `SignupTimeout`
- `MatchStartTimeout`
- `EnterMatchTimeout`
- `EnterRoundTimeout`
- `GameActionTimeout`
- `GameOverTimeout`

Timeouts fail the scenario instead of leaving a pending wait. The failure report must identify which timeout fired and which account was blocked.

## Observability

The runner emits structured logs for each account and one scenario summary.

Per-account logs include:

- Account alias and `userid` when known.
- Session phase.
- Sent message name, message id, and key fields.
- Received message name, message id, and key fields.
- State transitions and reasons.
- Server error codes.
- Decode errors.
- Timeout failures.
- Disconnects and reconnect decisions.

The scenario report includes:

- Scenario name.
- Start and end time.
- Overall result.
- Per-account connect, login, signup, enter, and game durations.
- Table id and seat mapping.
- Game result if available.
- First failure details if failed.

## Testing Strategy

V1 testing is layered:

1. Codec tests for frame decode/encode and message id mapping using captured or synthetic packets.
2. Session tests with fake decoded messages to verify state transitions and timeout behavior.
3. Bot tests for minimal legal decisions with deterministic hands.
4. One real-server smoke test: three configured accounts complete one game.

The real-server smoke test should be opt-in because it depends on server availability, credentials, and match configuration.

## Implementation Order

1. Create the test runner project and config model.
2. Generate protocol code from the required `.proto` files.
3. Run a `ServerProtocolCodec` spike that proves real-server wire compatibility.
4. Implement the server protocol codec for the minimum login/signup/match/game messages.
5. Implement `AccountSession` and message routing.
6. Implement login and signup flows.
7. Implement enter-match / enter-round flow.
8. Implement `ILordGameVariant` and `ClassicLordVariant`.
9. Implement minimal game state tracking over variant-neutral events.
10. Implement `MinimalLandlordBot`.
11. Implement `ThreePlayersOneGameScenario`.
12. Add logs, report output, and opt-in real-server smoke execution.

The codec spike must complete before game-flow implementation starts. It must identify or recreate the real `IConnectService` / `ITCPPacketProcessor` wire format from `uframework` source, generated code, DLL inspection, or captured traffic. At minimum, the spike must prove that a login request can be encoded, sent to the test server, and decoded from the server response.

## Acceptance Criteria

V1 is complete when:

- One command starts the runner.
- The runner uses three configured accounts.
- All accounts connect and log in successfully.
- All accounts sign up for the same match entry.
- All accounts enter the same table.
- The classic `TKLord.proto` variant drives one full Dou Dizhu game to completion.
- A success report includes timings and table/seat information.
- A failure report identifies the failed account, phase, last message, and error or timeout.
- The code structure can later expand to other LordUnion variants, multi-game loops, and pressure testing without rewriting the protocol/session layers.

## Open Constraints

The design assumes the test server is reachable at **`115.182.5.66:30301`**, with the three default test accounts signing up for the **经典斗** 自由桌 preset (`gameId` `1001`, `productId` `2008280`, `tourneyId` `159740`).

The exact server frame header, message ids, and encryption/compression details must be filled in from the real protocol implementation or captured traffic during implementation. This does not change the architecture; it only fills in `ServerProtocolCodec`.

The first implementation task is therefore a wire-protocol spike. No signup, enter-match, or game-bot work should proceed until login request/response encode-decode is proven against the real server or an equivalent captured packet set.

## Follow-Up

After this spec is reviewed, the next step is an implementation plan that breaks the work into small tasks and identifies the first protocol messages needed for the three-account one-game path.
