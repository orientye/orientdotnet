# LordUnion Client Cleanup Symmetry Design

**Date:** 2026-05-29  
**Status:** Proposed

## Goal

Finish the stage-client lifecycle boundary for LordUnion integration tests by moving cleanup behind `LordUnionSessionClient`.

The scenario should read as a per-account client lifecycle:

```text
Connect
Login
Cleanup(pre-signup)
Signup
MatchStart
EnterMatch
EnterRound
Game
Cleanup(post-game)
```

`ThreePlayersOneGameScenario` should coordinate accounts and game flow, but it should not directly own the details of cleanup request construction, state validation, transport sending, or known-match exit handling.

## Context

The stage client migration already moved the main account lifecycle behind `LordUnionSessionClient`:

- `ConnectAsync`
- `LoginAsync`
- `SignupAsync`
- `WaitForMatchStartAsync`
- `EnterMatchAsync`
- `EnterRoundAsync`

Cleanup is now the remaining asymmetric lifecycle operation. The scenario still holds an `AccountCleanupFlow` field and calls:

```csharp
accountCleanupFlow.RunAsync(bundle.Session, config.Match, bundle.Transport, ...);
```

Recent live testing showed why cleanup is part of the normal lifecycle rather than a side detail:

- Without reliable cleanup, repeated live runs can leave accounts in a state where signup returns `mobile.param=6` and no match start acknowledgement arrives.
- With post-game cleanup plus signup early-failure behavior, four back-to-back live runs completed successfully.

The cleanup behavior is important enough to expose through the same per-account client surface as the rest of the lifecycle.

## Decision

Add cleanup to `LordUnionSessionClient`:

```csharp
public CRpcTask<AccountCleanupFlowResult> CleanupAsync(
    MatchConfig match,
    AccountCleanupRunOptions? options = null);
```

`LordUnionSessionClient` will own an `AccountCleanupFlow` collaborator in the same style that it owns `EnterMatchFlow`.

```csharp
private readonly AccountCleanupFlow cleanupFlow;
```

The cleanup flow remains a separate engine. The client is the public per-account facade; the flow remains the focused, unit-tested implementation for cleanup mechanics.

## Scope

This change includes:

- Add `CleanupAsync` to `LordUnionSessionClient`.
- Inject `AccountCleanupFlow` into `LordUnionSessionClient` for tests, defaulting to `new AccountCleanupFlow(codec)`.
- Replace scenario direct cleanup calls with `bundle.Client.CleanupAsync(...)`.
- Remove `AccountCleanupFlow` ownership from `ThreePlayersOneGameScenario`.
- Remove the scenario's stored `EnterMatchFlow` field if it is only a pass-through to client construction; keep the constructor injection path if tests need it.
- Keep post-game cleanup best-effort.
- Make post-game cleanup observable in the report, without turning post-cleanup failure into scenario failure.

## Non-Goals

This change does not:

- Change the game server wire protocol.
- Replace `GameServerDotNettyTransport`, `TcpChannelHost`, or `CRpcLoop`.
- Convert LordUnion traffic into CRPC `serviceId` / `methodId` calls.
- Inline `EnterMatchFlow` into `LordUnionSessionClient`.
- Inline `GameFlow` or the bot into `LordUnionSessionClient`.
- Delete `EnterMatchFlow.RunAsync` or `EnterMatchFlow.EnterTableAsync`.
- Rewrite scenario phase plumbing to use only `*StageResult` types.
- Rename report and diagnostic DTOs such as `LoginFlowResult`, `SignupFlowResult`, or `EnterMatchFlowResult`.
- Fail an otherwise successful scenario because post-game cleanup failed.
- Commit changes automatically.

## Deferred Work

These are valid follow-ups, but they should not be bundled into this cleanup symmetry change:

- Migrate `EnterMatchFlowTests` to stage-client APIs and then delete or internalize `EnterMatchFlow` legacy APIs.
- Convert scenario phase result handling from `*FlowResult` to `*StageResult`.
- Align report and diagnostic DTO names with stage naming.
- Add a `CleanupStageResult` wrapper or alias if stage result naming becomes important at the report boundary.
- Wrap `GameFlow` behind a future `Client.PlayGameAsync` only after bot and variant ownership are redesigned.

## Cleanup API Semantics

`CleanupAsync(match)` uses pre-signup defaults:

- Requires a logged-in account.
- Drains briefly to capture pushed match ids.
- Sends tourney unsignup.
- Exits discovered match ids with `ExitGame` and `ExitMatch`.

`CleanupAsync(match, AccountCleanupRunOptions.PostGameCleanup(matchIds))` uses post-game defaults:

- Allows `Finished`, `InGame`, and `SignedUp` states.
- Does not need a drain window when known match ids are supplied.
- Exits known match ids and sends tourney unsignup.
- Returns the cleanup result if available.

The client should not hide cleanup failure for the normal `CleanupAsync` API. Best-effort handling belongs at the scenario post-game call site unless a separate `CleanupBestEffortAsync` is introduced later.

## Reporting

Post-game cleanup remains best-effort, but its result should be visible in `ScenarioReport`.

Add a compact post-cleanup summary with per-account fields such as:

- account alias
- whether cleanup completed without throwing
- unsignup sent
- unsignup ack received
- unsignup param
- discovered match ids
- exit-game attempted match ids
- exit-match attempted match ids
- error message if the best-effort cleanup threw

The successful console output should remain compact. It may add one short `cleanup` line only if that line is concise. The JSON report should include the full cleanup summary for investigation.

## Desired Scenario Boundary

After this change, `ThreePlayersOneGameScenario` should still own multi-account sequencing:

```text
login all accounts
cleanup all accounts
signup all accounts
wait for match start
enter match
enter round
verify same table
play game
post cleanup all accounts
build report
```

But account-level operations should be invoked through `bundle.Client`.

## Success Criteria

- `ThreePlayersOneGameScenario` no longer has an `AccountCleanupFlow` field.
- Scenario cleanup calls go through `bundle.Client.CleanupAsync`.
- Post-game cleanup continues to run after successful games and remains best-effort.
- Post-game cleanup outcome is present in JSON reports.
- Existing `AccountCleanupFlowTests` continue to cover cleanup engine mechanics.
- `LordUnionSessionClientTests` cover the client cleanup facade.
- `dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter FullyQualifiedName~LordUnion` passes.
- At least one live run passes; two back-to-back live runs are preferred for validating cleanup behavior.

## Risks

The primary risk is accidentally changing cleanup failure behavior. Pre-signup cleanup may surface failures normally, but post-game cleanup must not turn a successful scenario into a failed one.

The second risk is over-expanding the refactor. `EnterMatchFlow`, `GameFlow`, and phase result naming should stay outside this change so the review remains focused on lifecycle cleanup symmetry.
