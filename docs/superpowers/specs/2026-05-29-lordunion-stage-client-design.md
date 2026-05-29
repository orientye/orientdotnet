# LordUnion Stage Client Design

**Date:** 2026-05-29  
**Status:** Proposed

## Goal

Make `LordUnion.IntegrationTests` read like a real client lifecycle instead of a sequence of low-level protocol operations.

The current scenario code already proves that three accounts can log in, sign up, enter a match, enter a table, and finish one classic Dou Dizhu game against the live server. The next change should reduce the visible protocol noise by introducing stage-oriented APIs:

```text
Login
Signup
MatchStart
EnterMatch
EnterRound
Game
```

The first five stages are common LordUnion lifecycle stages. Game-specific differences should stay in the game stage through `ILordGameVariant`.

## Context

`LordUnion.IntegrationTests` currently has clear lower-level building blocks:

- `AccountSession` records session state, sent messages, received messages, and pending waits.
- `GameServerDotNettyTransport` owns live TCP transport through the shared `TcpChannelHost`.
- `ServerProtocolCodec` builds `TKMobileReqMsg` requests and decodes `TKMobileAckMsg` responses.
- `LoginFlow`, `SignupFlow`, `EnterMatchFlow`, and `GameFlow` orchestrate the current scenario.
- `ILordGameVariant` already adapts game-variant protobuf messages to variant-neutral game events and requests.

The problem is that scenario and flow code still exposes too much send/wait/parse detail:

```text
create request
send request
wait for ProtocolMessageKind
extract typed ack from ProtocolMessage
update AccountSession
build flow result
```

That makes simple lifecycle stages feel verbose and makes future multi-game support harder to reason about.

## Non-Goals

This design does not:

- Change the game server wire protocol.
- Replace `GameServerDotNettyTransport` or `TcpChannelHost`.
- Convert LordUnion traffic into CRPC `serviceId` / `methodId` RPC calls.
- Generate a full LordUnion client stub from protobuf definitions.
- Rewrite `GameFlow` or the bot in the first pass.
- Add a second game variant in this change.
- Add broad hook/plugin abstractions before a second game proves the need.

## Decision

Introduce a stage-oriented `LordUnionSessionClient` for one account.

The client wraps the existing `AccountSession`, `IGameServerTransport`, `ServerProtocolCodec`, and `CRpcLoop` behavior, then exposes typed lifecycle methods:

```csharp
public CRpcTask ConnectAsync(ServerConfig server, TimeSpan timeout);

public CRpcTask<LoginStageResult> LoginAsync(
    AccountConfig account,
    ProtocolConfig protocol,
    TimeSpan timeout);

public CRpcTask<SignupStageResult> SignupAsync(
    LordUnionGameProfile profile,
    TimeSpan timeout);

public CRpcTask<MatchStartStageResult> WaitForMatchStartAsync(
    TimeSpan timeout);

public CRpcTask<EnterMatchStageResult> EnterMatchAsync(
    LordUnionGameProfile profile,
    MatchStartStageResult matchStart,
    TimeSpan timeout);

public CRpcTask<EnterRoundStageResult> EnterRoundAsync(
    LordUnionGameProfile profile,
    TimeSpan timeout);
```

`LordUnionSessionClient` is intentionally per-account. It does not own multi-account coordination and does not own the game bot. `ThreePlayersOneGameScenario` remains responsible for coordinating three clients.

Keep `GameFlow` as the multiplayer game-stage orchestrator in the first implementation. The scenario should call `GameFlow` after all accounts complete `EnterRound`.

## Game Profiles

Most non-game stages share the same protocol across LordUnion games, but some parameters can differ by game. Put those differences in a profile instead of scattering them through flows.

```csharp
public sealed record LordUnionGameProfile
{
    public string ProfileId { get; init; } = "classic";
    public uint GameId { get; init; }
    public uint ProductId { get; init; }
    public uint TourneyId { get; init; }
    public uint MatchPoint { get; init; }
    public required ILordGameVariant Variant { get; init; }
}
```

The first implementation can build a profile from the existing `MatchConfig` and selected `ILordGameVariant`. If `ProductId` and `MatchPoint` represent the same value in the current config path, the implementation should preserve the existing naming at call boundaries and avoid inventing a second source of truth.

Do not add generic profile hooks in the first pass. If a later game needs an extra field for `EnterMatch` or `EnterRound`, add that specific field when the second game's behavior is known.

## Stage Results

Each stage returns a typed result so scenario code does not need to inspect `ProtocolMessage`.

```csharp
public sealed record LoginStageResult(
    int Result,
    uint UserId,
    ulong? SessionId,
    string? Nickname,
    string AesKey);

public sealed record SignupStageResult(
    int Result,
    uint TourneyId,
    uint MatchPoint,
    uint GameId);

public sealed record MatchStartStageResult(
    uint MatchId,
    string? ServerIp,
    uint? ServerPort);

public sealed record EnterMatchStageResult(
    uint MatchId,
    uint? TableId);

public sealed record EnterRoundStageResult(
    uint MatchId,
    uint TableId,
    uint Seat);
```

The stage client still updates `AccountSession` as the source of live session state. Result records are the typed public summary of each stage, not a replacement for session state.

## Stage Data Flow

### Connect

```text
LordUnionSessionClient.ConnectAsync
  -> transport.BindIncomingHandler(session, codec)
  -> transport.ConnectAsync(server, timeout, session.Loop)
  -> session.SetState(Connected)
```

After connect, the stage client should not repeatedly bind the incoming handler for every stage.

### Login

`LoginAsync` contains the current two-step login sequence:

```text
AnonymousBrowseReq
  -> AnonymousBrowseAck
  -> resolve AES key, route id, and login timestamp

CommonLoginReq
  -> CommonLoginAck
  -> decrypt login JSON
  -> set session.UserId, SessionId, Nickname, LoginRouteId
  -> return LoginStageResult
```

The implementation may use a private helper for request/response style calls:

```csharp
private async CRpcTask<(int Result, TAck Ack, ProtocolMessage Message)> CallAsync<TAck>(
    TKMobileReqMsg request,
    ProtocolMessageKind expectedKind,
    Func<ProtocolMessage, TAck?> getAck,
    int timeoutMs)
```

That helper must stay private to the stage client. Public APIs should remain stage-based rather than exposing `ProtocolMessageKind` selectors to scenario code.

### Signup

```text
TourneySignupReq(profile.TourneyId, profile.GameId, profile.MatchPoint)
  -> TourneySignupAck
  -> update session.TourneyId and session.MatchPoint
  -> return SignupStageResult
```

The request must use the profile values so future game profiles can vary match parameters without copying the signup flow.

### MatchStart

`WaitForMatchStartAsync` is a push-driven stage. It must not use the private request/response helper.

```text
wait for StartClientExAck / StartGameClientAck / match progress candidates
  -> capture match id, server ip, and server port when present
  -> update session.MatchId
  -> return MatchStartStageResult
```

This stage should reuse the existing match-start capture behavior from `PostSignupDiagnosticMonitor` and `EnterMatchFlowSessionState` where practical, but the public result must remain typed and compact.

### EnterMatch

```text
EnterMatchReq(matchStart.MatchId, profile parameters if needed)
  -> EnterMatchAck and related match messages
  -> update session.MatchId and session.TableId when known
  -> return EnterMatchStageResult
```

If the current `EnterMatchFlow` waits for multiple candidate messages, preserve that behavior inside the stage client or a focused helper. The migration should not narrow accepted live-server responses.

### EnterRound

```text
EnterRoundReq(session.MatchId / session.TableId)
  -> EnterRoundAck / InitGameTableAck / seat messages
  -> update session.TableId and session.SeatOrder
  -> return EnterRoundStageResult
```

This is the end of the common stage pipeline. After this point, the scenario enters `GameFlow` with the selected `ILordGameVariant`.

### Game

`GameFlow` remains the multiplayer game-stage implementation in the first pass.

`ILordGameVariant` continues to own game-specific behavior:

- Determine whether an ack belongs to the variant.
- Decode game acks into variant-neutral `GameEvent` values.
- Build ready, bid, play-cards, pass, and force-declare requests.

Adding another game should primarily require a new `ILordGameVariant` implementation, a matching bot policy if needed, and a profile that selects those pieces.

## Scenario Shape

The scenario should become stage-oriented:

```csharp
var profile = LordUnionGameProfiles.FromConfig(match, variant);

foreach (var client in clients)
{
    await client.ConnectAsync(server, config.Timeouts.Login);
    await client.LoginAsync(account, protocol, config.Timeouts.Login);
    await client.SignupAsync(profile, config.Timeouts.Signup);
}

var starts = await WaitAllMatchStartAsync(clients, config.Timeouts.EnterMatch);

foreach (var client in clients)
{
    await client.EnterMatchAsync(profile, starts[client.Alias], config.Timeouts.EnterMatch);
    await client.EnterRoundAsync(profile, config.Timeouts.EnterMatch);
}

var game = await gameFlow.RunAsync(
    sessions,
    profile.Variant,
    botPolicy,
    config.Timeouts.Game);
```

`WaitAllMatchStartAsync` can stay in `ThreePlayersOneGameScenario` initially because it coordinates multiple accounts. If it becomes reusable, move it into a separate coordinator later.

## Error Handling

Each stage should fail with account and stage context:

```text
[player1] Login failed: CommonLoginAck param=31, missing session id.
[player2] WaitForMatchStart timed out after 30s.
[player3] EnterRound failed: InitGameTableAck missing seat.
```

The scenario report remains the structured failure record. Stage exceptions should improve the human-readable failure message without replacing `ScenarioFailureDetail`.

Timeouts remain explicit per stage and continue to run on the owning `CRpcLoop`.

## Logging

Successful live runs should be compact by default:

```text
SUCCESS ThreePlayersOneGame duration=155.54s matchId=475051269 tableId=475051269 winSeat=1
player1 login=416ms signup=74ms enter=3.44s game=146.30s
player2 login=415ms signup=74ms enter=3.44s game=146.30s
player3 login=415ms signup=74ms enter=3.44s game=146.30s
JSON report: Tests/LordUnion.IntegrationTests/lordunion-test-output/scenario-report-20260529T032000Z.json
```

Detailed diagnostics should print on failure. Signup diagnostics and post-signup message samples are useful for debugging, but they should not dominate successful logs.

A later CLI flag such as `--verbose` can force full diagnostic output. The first implementation may simply use compact output on success and expanded output on failure.

## Migration Strategy

### Step 1: Add the stage client

Add the new stage client, profile, and stage result records without deleting existing flows.

Expected files:

```text
Tests/LordUnion.IntegrationTests/Sessions/LordUnionSessionClient.cs
Tests/LordUnion.IntegrationTests/Scenarios/LordUnionGameProfile.cs
Tests/LordUnion.IntegrationTests/Scenarios/LordUnionGameProfiles.cs
Tests/LordUnion.IntegrationTests/Scenarios/*StageResult.cs
```

The stage client should reuse current session, codec, and transport behavior.

### Step 2: Move the scenario to stage language

Change `ThreePlayersOneGameScenario` to coordinate `LordUnionSessionClient` instances:

```text
ConnectAsync
LoginAsync
SignupAsync
WaitForMatchStartAsync
EnterMatchAsync
EnterRoundAsync
GameFlow.RunAsync
```

This step should make the scenario easier to read while preserving live behavior.

### Step 3: Prune duplicates after verification

After local tests and one live run pass, decide whether to:

- Delete `LoginFlow` and `SignupFlow`, or make them thin wrappers over the stage client.
- Split `EnterMatchFlow` into focused private helpers for `MatchStart`, `EnterMatch`, and `EnterRound`.
- Keep diagnostic helpers but only print their full output on failure or verbose runs.

Do not delete large flows before the stage client has passed regression and live verification.

## Testing

Add focused tests for the new stage client:

- `LoginAsync_CompletesBrowseAndLogin`: verifies browse/login messages, session state, user id, AES key, and result values.
- `SignupAsync_SendsProfileParameters`: verifies signup request values come from `LordUnionGameProfile`.
- `WaitForMatchStartAsync_CompletesFromStartClientExAck`: verifies push-driven match start completion.
- `EnterMatchAsync_PreservesCurrentAcceptedMessages`: verifies the same message forms accepted by current `EnterMatchFlow` still complete the stage.
- `EnterRoundAsync_ReturnsTableAndSeat`: verifies table and seat extraction.
- `SuccessfulConsoleSummary_IsCompact`: optional guard against noisy success output.

Run:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj
dotnet test Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj
```

Live verification:

- Run the three-account live scenario once.
- Confirm the JSON report has `success: true`.
- Confirm success console output is compact.

## Risks

- `EnterMatchFlow` currently accepts multiple message paths. A naive stage rewrite could accidentally narrow live behavior.
- Match-start push capture is coupled with diagnostics. The migration must keep the data capture behavior while reducing success log noise.
- `BotDecision` currently references `ClassicLordVariant` in at least one path. Adding a second game will likely require a game-specific bot policy in addition to a new variant.
- If profile fields duplicate `MatchConfig` fields without a clear source of truth, tests could pass with one config path but live runs could use a different value.

## Success Criteria

This phase is complete when:

1. `ThreePlayersOneGameScenario` is expressed in stage-level calls for connect, login, signup, match start, enter match, and enter round.
2. Scenario code no longer directly sends requests, waits on `ProtocolMessageKind`, or extracts typed acks for the common lifecycle stages.
3. Game-specific behavior remains behind `ILordGameVariant` and `GameFlow`.
4. Different game parameters are represented by `LordUnionGameProfile`.
5. Successful console output is compact by default, while failure output still includes useful diagnostics.
6. Local regression tests pass.
7. One live three-account scenario still succeeds.
