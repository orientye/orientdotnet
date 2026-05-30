# LordUnion EnterMatchFlow Internalize — Design

**Status:** Implemented  
**Verification:** `dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter FullyQualifiedName~LordUnion` — 186 passed  
**Date:** 2026-05-29  
**Scope:** Option 4 from stage-client follow-up (not Game → Client / option 3)

## Problem

`LordUnionSessionClient` already exposes `WaitForMatchStartAsync`, `EnterMatchAsync`, and `EnterRoundAsync`, but `EnterMatchFlow` remains a **public** type with legacy `RunAsync` / `EnterTableAsync` / `EnterMatchFlowResult`. Tests and the scenario still reference the flow type directly, which creates two entry paths and duplicate coverage.

## Goal

- **Single public entry** for enter-table behavior: `LordUnionSessionClient` stage APIs.
- **`EnterMatchFlow` becomes `internal`** — implementation detail of the integration assembly, injectable in tests via existing `InternalsVisibleTo("CRPC.Tests")`.
- **Remove legacy APIs:** `RunAsync`, `EnterTableAsync`, `EnterMatchFlowResult`, `CreateSuccessResult`, and dead `Fail` helper.
- **Scenario** no longer carries an `EnterMatchFlow` field; only the client owns the collaborator.

## Non-Goals

- `Client.PlayGameAsync` / `GameFlow` relocation (option 3).
- Report/DTO renames (`GameFlowWinSeat`, etc.).
- Protocol decode expansion or live CI gates.

## Approach (recommended)

**Internal collaborator + client pipeline tests**

1. Mark `EnterMatchFlow` `internal sealed`.
2. Delete `EnterMatchFlowResult.cs` and legacy pipeline methods on the flow.
3. Keep granular `internal` methods (`WaitForMatchStartAsync`, `EnterMatchOnlyAsync`, `EnterRoundOnlyAsync`, `InstallMatchProgressCapture`, `CreateEnterTableStageResult`) for unit tests that target edge cases without full client setup.
4. Migrate former `RunAsync_*` integration tests to `LordUnionSessionClientTests` as `EnterTablePipeline_*` (Connect → WaitForMatchStart → EnterMatch → EnterRound → `ToEnterTableStageResult`).
5. Extract shared wire fixtures to `LordUnionEnterMatchWireFixtures` in `CRPC.Tests` to avoid duplicating message builders.

## Testing

- All existing `EnterMatchFlowTests` except `RunAsync_*` remain (flow-level edge cases).
- New/ported `LordUnionSessionClientTests` cover full enter-table pipeline and timeouts.
- `dotnet test` on `CRPC.Tests` and `LordUnion.IntegrationTests`.

## Risks

- Accidentally narrowing enter-match tolerance when moving tests — pipeline tests must preserve skip-`EnterMatchAck` and progress-message paths covered by former `RunAsync` cases.
