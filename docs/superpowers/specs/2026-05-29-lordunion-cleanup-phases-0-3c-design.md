# LordUnion Cleanup Phases 0–3c — Design

**Status:** Implemented  
**Verification:** `dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter FullyQualifiedName~LordUnion` — 186 passed  
**Date:** 2026-05-30  
**Scope:** Repository hygiene, naming/DTO alignment, cleanup flow internalize, scenario slimming through unified client stage APIs (phases 0–3c only; not 4–6).

**Prerequisite work (already done):** Stage client, cleanup symmetry, EnterMatchFlow internalize, `PlayGameAsync`, enter-match tolerance, protocol decode expansion, live back-to-back ×2 (2026-05-30).

**User decision:** JSON report field renames are **allowed** (no dual-key compatibility period required).

---

## Goals

1. **Repository:** Stop tracking live logs and scenario JSON output artifacts.
2. **Naming:** Stage results and report JSON use consistent names (`WinSeat`, `CleanupStageResult`).
3. **Architecture:** `AccountCleanupFlow` is `internal`; public surface is `LordUnionSessionClient` only (same pattern as EnterMatch/Game).
4. **Scenario thinness:** `ThreePlayersOneGameScenario` reads as multi-account orchestration; per-account lifecycle is `Client.*` only.
5. **Client APIs:** `EnterTableAsync`, `PlayGameAsync(config)` reduce duplicated phase wrappers.

## Non-Goals (this spec)

- Test deduplication (phase 4): merge `EnterMatchFlowTests` / `GameFlowTests`.
- Split `EnterMatchFlow` into multiple files (phase 5a).
- Diagnostic verbosity / `Unknown` protocol decode (phase 5b–5c).
- CI workflow for live runs (phase 6).
- `ThreePlayersScenarioCoordinator` extract (phase 3d).
- Git commit (human partner reviews diff first).

---

## Phase 0 — Repository and documentation

### 0a `.gitignore`

Add under `orientdotnet` repo root or `Tests/LordUnion.IntegrationTests/`:

```gitignore
# LordUnion integration harness artifacts
Tests/LordUnion.IntegrationTests/live-run*.log
Tests/LordUnion.IntegrationTests/lordunion-test-output/
```

Do not delete local log files on disk; only prevent new commits.

### 0b Spec status updates

Mark **Implemented** and cross-link in:

- `2026-05-29-lordunion-stage-client-design.md` — full pipeline including `PlayGameAsync`; Step 3 prune list updated.
- `2026-05-29-lordunion-client-cleanup-symmetry-design.md` — Deferred items completed or superseded by this spec.
- `2026-05-29-lordunion-enter-match-flow-internalize-design.md`
- `2026-05-29-lordunion-game-stage-client-design.md`

Add **Live verification** footnote: back-to-back 2026-05-30, matchId `475051269`, ~221s / ~172s, `LordResultAck`, cleanup completed.

### 0c Plans

Add banner at top of `docs/superpowers/plans/2026-05-29-lordunion-stage-client.md`:

> **Historical plan.** Implementation status: see specs dated 2026-05-29 and this document (2026-05-30). Do not treat `EnterMatchFlowResult` / `GameFlow.RunAsync` sections as current.

---

## Phase 1 — Naming and report JSON

### 1a `CleanupStageResult`

Add to `Scenarios/StageResults.cs`:

```csharp
public sealed record CleanupStageResult(
    bool UnsignupSent,
    bool UnsignupAckReceived,
    uint? UnsignupParam,
    IReadOnlyList<uint> DiscoveredMatchIds,
    IReadOnlyList<uint> ExitGameAttemptedMatchIds,
    IReadOnlyList<uint> ExitMatchAttemptedMatchIds);
```

`AccountCleanupSummary.FromResult` accepts `CleanupStageResult?` instead of `AccountCleanupFlowResult?`.

### 1b Game end naming

| Layer | Old | New |
|-------|-----|-----|
| `AccountGameEndSummary` | `GameFlowWinSeat` | `WinSeat` |
| `AccountGameEndSummaryJson` | `GameFlowWinSeat` | `WinSeat` |
| Console `gameEnd` line | `seat=` from `WinSeat` | unchanged format |
| `ThreePlayersOneGameScenario` | maps `GameFlowWinSeat` | maps `WinSeat` |

### 1c JSON report schema (camelCase via default serializer)

`gameEndSummaries[].winSeat` (was `gameFlowWinSeat`). No other game-end keys change.

Update `ReportWriterTests` and any scenario tests asserting JSON/console.

---

## Phase 2 — Cleanup flow internalize

- `AccountCleanupFlow` → `internal sealed class`
- `AccountCleanupFlowResult` → `internal sealed class` (or removed; map inline to `CleanupStageResult` in client)
- `LordUnionSessionClient.CleanupAsync` returns `CRpcTask<CleanupStageResult>`
- Public ctor unchanged; `internal` ctor may inject `AccountCleanupFlow` for tests (`InternalsVisibleTo("CRPC.Tests")` already exists)
- `AccountCleanupFlowTests` continue using `internal` flow or client `CleanupAsync`

---

## Phase 3 — Scenario slimming and client APIs

### 3a Remove thin scenario wrappers

Replace private `RunLoginAsync` / `RunSignupAsync` with:

- Static helpers: `ScenarioStageMapping.FromLoginException`, `FromSignupException` (or inline minimal catch in lambdas)
- `RunPhaseConcurrentOnLoopAsync(b => Connect + LoginAsync(...))` etc.

Keep exception→`LoginStageResult` / `SignupStageResult` behavior identical to today.

### 3b `EnterTableAsync`

```csharp
public async CRpcTask<EnterTableStageResult> EnterTableAsync(
    LordUnionGameProfile profile,
    TimeoutConfig timeouts)
```

Sequence: `WaitForMatchStartAsync` → `EnterMatchAsync` → `EnterRoundAsync` → `ToEnterTableStageResult`.

**Fake tests:** `ScenarioRunOptions.MatchStartAckFactory` remains scenario-only: when set and transport is fake, scenario delivers push then calls `EnterTableAsync` OR keeps explicit three-step call with factory (minimal branch). Prefer: deliver factory message, then `await client.EnterTableAsync(profile, timeouts)` so fake and live share one path after push.

### 3c `PlayGameAsync` convenience overload

```csharp
public CRpcTask<GameStageResult> PlayGameAsync(
    LordUnionGameProfile profile,
    LordUnionTestConfig config,
    ScenarioRunOptions? options = null)
```

Builds `MinimalLandlordBot`, policy (`PolicyOverride` or default), scheduler via `ActionSchedulerFactory`, delegates to existing `PlayGameAsync(profile, policy, scheduler, gameOverTimeout)`.

Scenario `RunGameAsync` becomes:

```csharp
if (options.PlayGameOverride is not null) { ... }
return await bundle.Client.PlayGameAsync(profile, config, options);
```

---

## Testing

After each phase group:

```bash
dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter "FullyQualifiedName~LordUnion"
```

After full 0–3c (user or agent): optional live back-to-back ×2 via `scripts/run-live-back-to-back.ps1`.

---

## Risks

| Risk | Mitigation |
|------|------------|
| JSON consumers relied on `gameFlowWinSeat` | User approved rename; document in this spec |
| Fake `MatchStartAckFactory` regression | Keep scenario branch; run `ThreePlayersOneGameScenarioTests` |
| `CleanupAsync` signature break for external callers | Only test harness uses it today |

---

## Success criteria

1. Phases 0–3c code complete; LordUnion unit tests pass (186+).
2. No public `AccountCleanupFlow` / `AccountCleanupFlowResult`.
3. Scenario has no `RunLoginAsync`/`RunSignupAsync`/`RunEnterMatchAsync` wrappers except fake match-start injection block.
4. `LordUnionSessionClient` exposes `EnterTableAsync` and config-based `PlayGameAsync`.
5. Report JSON uses `winSeat` under `gameEndSummaries`.
