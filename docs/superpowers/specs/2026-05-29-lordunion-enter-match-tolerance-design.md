# LordUnion EnterMatch Tolerance Design

**Date:** 2026-05-29  
**Status:** Implemented

## Problem

Live runs sometimes receive `EnterRoundAck` and in-game `LordAck` messages (for example `LordWaitClientReadyAck`, `LordInitCardAck`) **without** a preceding `EnterMatchAck`. The strict client waited only for `EnterMatchAck` and timed out after 60 seconds even though the server had already placed the account in the match.

## Decision

`EnterMatchFlow.EnterMatchOnlyAsync` accepts either:

1. `EnterMatchAck` with a matching `matchid`, or
2. In-match progress that indicates the account is already at the table: `EnterRoundAck`, or same-match `InitGameTableAck`, `AddGamePlayerInfoAck`, or `LordAck`.

Captured messages on `EnterMatchFlowSessionState` (via `LordUnionSessionClient.enterMatchState`) are consulted before and during the wait. If `LastEnterRoundAck` or `InitGameTableAck` is already present, enter-match completes without waiting for `EnterMatchAck`.

## Non-Goals

- Changing server protocol or requiring `EnterMatchAck` on re-entry.
- Skipping `EnterMatchReq` when already in match (request is still sent).
- Failing the scenario when only tolerance path succeeds (still proceeds to `EnterRound`).

## Success Criteria

- Live EnterMatch phase completes in seconds (not 60s timeout) when server pushes round/game without `EnterMatchAck`.
- `RunAsync_SucceedsWhenEnterRoundAckArrivesInsteadOfEnterMatchAck` passes.
- Normal fake/live paths that return `EnterMatchAck` remain unchanged.

## Risks

- False-positive completion if an unrelated `EnterRoundAck` arrives without match context. Mitigated by session `MatchId` being set from match-start before enter-match.
