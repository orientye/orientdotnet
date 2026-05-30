# LordUnion Game Stage on Client — Design

**Status:** Implemented  
**Verification:** `dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter FullyQualifiedName~LordUnion` — 186 passed  
**Date:** 2026-05-29  
**Scope:** Option 3 — `PlayGameAsync` on `LordUnionSessionClient`

## Goal

- Per-account game loop via `LordUnionSessionClient.PlayGameAsync`.
- `GameFlow` / `GameFlowResult` become `internal` implementation details (same pattern as EnterMatchFlow).
- `ThreePlayersOneGameScenario` orchestrates three clients; no scenario-owned `GameFlow`.

## API

```text
PlayGameAsync(profile, policy, scheduler, gameOverTimeout) -> GameStageResult
```

- `LordUnionGameProfile.Variant` selects `ILordGameVariant`.
- Bot policy and scheduler remain injected by the scenario (from config + `ScenarioRunOptions`).
- `ScenarioRunOptions.PlayGameOverride` replaces `GameFlowOverride` for test stubs.

## Non-Goals

- Rename report fields (`GameFlowWinSeat`).
- Change lord protocol end signals or bot logic.
- Multi-account game coordination inside the client.

## Testing

- Existing `GameFlowTests` (internal flow).
- Scenario tests updated to `PlayGameOverride`.
- Full `FullyQualifiedName~LordUnion` filter.
