# CRpcLoop Lock-Free Wakeup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `wakeupGate` lock in `Post`/`WaitForWorkOrTimer` with `Interlocked pendingWakeup + ManualResetEventSlim`, keeping anti-lost-wakeup semantics while removing lock contention on the `Post` hot path.

**Architecture:** `Post` enqueues to `ConcurrentQueue` then atomically sets `pendingWakeup = 1`; only the first transition `0→1` calls `wakeup.Set()`. `WaitForWorkOrTimer` clears `pendingWakeup`, resets the event, re-checks queue/timer/flag, then waits outside any lock.

**Tech Stack:** C# / .NET 8, xUnit, existing `CRpcLoop` / `CRpcLoopWakeupTests`.

**Spec reference:** `Doc/architecture-draft.md` §9.5.2.

---

## File Structure

| File | Responsibility |
| --- | --- |
| `CRpc/Async/CRpcLoop.cs` | Remove `wakeupGate`; add `pendingWakeup`; refactor `Post` + `WaitForWorkOrTimer` |
| `Tests/CRPC.Tests/CRpcLoopWakeupTests.cs` | Add coalesced-wakeup + race tests |
| `Doc/architecture-draft.md` §9.5.2 | Document lock-free wakeup invariant |

---

### Task 1: Add Tests for Lock-Free Wakeup Semantics

**Files:**
- Modify: `Tests/CRPC.Tests/CRpcLoopWakeupTests.cs`

See plan body in agent session for full test code.

---

### Task 2: Replace Lock with pendingWakeup

**Files:**
- Modify: `CRpc/Async/CRpcLoop.cs`

---

### Task 3: Update Architecture Doc

**Files:**
- Modify: `Doc/architecture-draft.md` §9.5.2
