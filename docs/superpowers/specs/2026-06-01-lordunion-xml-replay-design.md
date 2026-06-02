# LordUnion XML Recording Replay & Server Verification — Design

**Status:** Approved  
**Date:** 2026-06-01  
**Scope:** End-to-end LordUnion integration tests driven by golden XML recordings (client replay, track **A**) and server-side semantic verification of generated recordings vs origin (track **B**). Covers TKLordSvr (`TK_INTEGRATION_TEST` / `TK_INTEGRATION_TEST_USE_CASES`) and `LordUnion.IntegrationTests` client modules under `Games/TKLord/`.

**Prerequisite work:** `ThreePlayersOneGameScenario`, `GameFlow`, `ScenarioRunOptions.PolicyOverride`, `ClassicLordVariant`, `MinimalLandlordBot` / `IBotPolicy`, `ImmediateActionScheduler`, live/fake transport split (`2026-05-25-lordunion-integration-test-design.md`, `2026-05-30-lordunion-shared-io-and-load-test-prep-design.md`).

**Related:** `Tests/LordUnion.IntegrationTests/Doc/lordunion-integration-tests-architecture.md`, `Games/TKLord/Protocol/Raw/TKLord.proto`, TKLordSvr `Define/TestDefine.h`.

---

## Summary

| Track | Owner | Responsibility |
|-------|-------|----------------|
| **A — Client XML replay** | `LordUnion.IntegrationTests` | Server sends `testrecordid` on `LordInitCardAck`; client loads matching fixture XML and replays bid (`id=2`) and play/pass (`id=10`) per seat via `XmlReplayBotPolicy`. Server still owns deck injection only. |
| **B — Server semantic diff** | TKLordSvr (integration-test build) | After game end, compare `Record\TKLord\Cases\Generate\{testrecordid}.xml` vs `Record\TKLord\Cases\Origin\{testrecordid}.xml` (same filename stem). Semantic diff only — not byte equality. |

The client does **not** implement B. The server does **not** replay client bid/play actions.

---

## Goals

1. **Deterministic client actions** when `testrecordid` is present: three seats replay recorded `id=2` (call landlord) and `id=10` (play or pass, `o` empty = pass) from fixture XML, keyed by seat `s`.
2. **Server-controlled deck** in Cases mode: `TK_INTEGRATION_TEST_USE_CASES` reads deck from sequential Origin case selection; bid and play remain client-driven.
3. **Single protocol carrier** for record identity: `LordInitCardAck.testrecordid` (field 5, optional string) = filename stem without `.xml` (e.g. `20260601_7646425803181457480`). Do **not** use `LordSvr2BotRecordAck` for passing record id.
4. **Fail-fast client** when `testrecordid` is non-empty but fixture is missing, or when the three seats disagree on `testrecordid`. No silent fallback to `MinimalLandlordBot`.
5. **Server B oracle** after game: semantic match of player actions and results between Generate and Origin; ignore `rt`, timestamps, match/round ids.

## Non-Goals (MVP)

- Multi-game loop in one scenario run.
- Client-side B verification or XML diff on the client.
- `LordSvr2BotRecordAck` as the record-id channel.
- Byte-level XML diff on the server.
- Server replay of bid/play (server only decides deck).
- Broadening replay to action ids other than 2 and 10 in MVP (optional `id=27` deal gate only; see below).
- Using XML `auto="1"` on `<a id="10">` to filter client replay queues (runtime uses protocol fields instead; see **Auto play / auto PASS** below).

---

## Protocol

### `LordInitCardAck.testrecordid`

| Field | Type | Semantics |
|-------|------|-----------|
| `testrecordid` | `optional string` (field 5) | Filename stem for the active case XML, **without** `.xml`. Example: `20260601_7646425803181457480`. |

**Proto source (client):** `Tests/LordUnion.IntegrationTests/Games/TKLord/Protocol/Raw/TKLord.proto` (field already present).

**Codegen:** Regenerate `Tests/LordUnion.IntegrationTests/Protocol/Generated/LordUnionProtocol.g.cs` via `Tests/LordUnion.IntegrationTests/Protocol/GenerateLordUnionProto.ps1`, pointing `TKLord.proto` at `Games/TKLord/Protocol/Raw` (or keep server/client proto in sync).

**Rejected alternative:** `LordSvr2BotRecordAck` exists in `TKLord.proto` but must **not** be used to pass `testrecordid` to integration-test clients.

### `LordTakeoutCardAck` — auto play / auto PASS hints

Carried on every play/pass broadcast (`lordtakeoutcard_ack_msg`). These flags describe **the next player** (`nextplayer`), not the seat that just acted (`seat`).

| Field | Proto | Semantics |
|-------|-------|-----------|
| `nextautogo` | field 8 | If true, **`nextplayer` will be auto-played by the game server** (e.g. last-hand must-play / assist take-out). Client must **not** send `LordTakeoutCardReq` for that turn. |
| `nextautopass` | field 10 | If true, **`nextplayer` will be auto-passed by the game server** (~500ms internal `EVENT_AUTO_PASS`). Client must **not** send pass/play for that turn. |

**Server behavior (reference):** `CTakeOutCardState::ReqTakeOutCard()` sets `bNextAutoGo` / `bNextAutoPass` from assist logic (`CheckNextPlayerIsAutoPut`, `CheckAutoPass`, farmer auto-pass, etc.). If `bNextAutoGo && !bNextAutoLastN` → `LastHandAutoDis()`; else if `bNextAutoPass` → timer then auto pass. See `Doc/TKLordSvr_出牌机制.md`, `Doc/TKLordSvr_出牌机制_辅助出牌.md`.

**Client replay rule:** Use **`nextautopass` / `nextautogo` only** for runtime decisions. **Do not** use XML attribute `auto="1"` on recorded `<a id="10">` to build or filter replay queues — replay timing is driven by live acks, not historical recording metadata.

**Rejected for client replay:** Filtering `id=10` at parse time by `auto="1"`. Origin/Generate XML may still contain `auto` for server recording and B diff metadata; track A ignores it when dequeuing actions.

### Server vs client responsibilities

| Concern | Server | Client |
|---------|--------|--------|
| Deck / init cards | Yes (Cases: from Origin XML) | Consumes `LordInitCardAck.cards` |
| `testrecordid` emission | Sets on `LordInitCardAck` when Cases active | Reads from init ack per seat |
| Bid (`id=2`) | No replay | `XmlReplayBotPolicy` |
| Play/pass (`id=10`) | No replay | `XmlReplayBotPolicy` |
| B diff Generate vs Origin | Yes, post-game | No |

All three seats must receive the **same** non-empty `testrecordid` on their init-card ack. Any mismatch → scenario fail-fast before `GameFlow`.

When `testrecordid` is empty or unset, behavior remains **existing** `MinimalLandlordBot` / `MinimalLandlordBotPolicy`.

---

## Server (TKLordSvr)

### Build macros

| Macro | Meaning |
|-------|---------|
| `TK_INTEGRATION_TEST` | Enables integration-test recording paths and `testrecordid` on init ack. |
| `TK_INTEGRATION_TEST_USE_CASES` | Valid **only** when `TK_INTEGRATION_TEST` is defined. Enables Cases mode: read deck from recording cases (sequential Origin selection). |

Reference: `Define/TestDefine.h` (both macros may be enabled for IT + Cases builds).

### Recording paths

| Mode | Directory | Filename | When written / read |
|------|-----------|----------|---------------------|
| Normal local record (non-Cases) | `Record\TKLord\OnlyLordTakeOut\` | (existing naming) | Replaces old `Log\Record` path for non-Cases IT recording |
| Cases — Origin (golden input) | `Record\TKLord\Cases\Origin\` | `{testrecordid}.xml` | Read for sequential case selection and deck injection |
| Cases — Generate (output) | `Record\TKLord\Cases\Generate\` | `{testrecordid}.xml` | Written after game ends |

**Cases flow:**

1. Select next Origin case sequentially; derive `testrecordid` from filename stem.
2. Inject deck from that Origin XML (server-only decision).
3. Populate `LordInitCardAck.testrecordid` for all seats.
4. Clients drive bid/play; server records actions to Generate XML on game end.
5. **B verification:** semantic diff `Generate\{testrecordid}.xml` vs `Origin\{testrecordid}.xml`.

### B verification rules (semantic diff)

Compare **player actions** and **results**, not raw file bytes.

**Ignore:**

- `rt` on `<a>` elements
- Timestamps (`time`, round-level times)
- `match`, `roundid`, `tourney`, and similar session identifiers

**Compare (per game within the round XML):**

| Action `id` | Attributes | Notes |
|-------------|------------|-------|
| `2` | `s` (seat), `o` (call value) | Landlord bid sequence |
| `10` | `s`, `o` (card string or empty for pass) | Play/pass sequence |

**Results:** `<results>` / `<result seat=… score=…>` — seat scores must match semantically.

**Do not** require byte-identical XML, attribute order, or full round metadata equality.

### Server implementation notes

- Server **only** decides deck in Cases mode; it does **not** inject or replay bid/play from XML.
- `testrecordid` in ack must match the Origin file stem used for that game.
- B failure should surface in server IT logs/assertions (exact mechanism follows existing TKLordSvr IT patterns).

---

## Client (`LordUnion.IntegrationTests`)

### Fixture layout

| Path | Role |
|------|------|
| `Tests/LordUnion.IntegrationTests/Games/TKLord/Cases/{testrecordid}.xml` | Golden replay input; **synced from** server `Record\TKLord\Cases\Origin\` |

Five reference fixtures already exist (e.g. `20260601_7646425803181457480.xml`). Encoding: **GB2312** (per XML declaration `encoding="gb2312"`).

### Replay behavior (track A)

When **any** seat’s `LordInitCardAck` carries non-empty `testrecordid`:

1. After all three inits are observed, assert all three `testrecordid` values are equal (fail-fast on mismatch).
2. Resolve fixture: `Games/TKLord/Cases/{testrecordid}.xml`.
3. If file missing → **fail immediately** before `GameFlow` starts. **No** fallback to `MinimalLandlordBot`.
4. Parse XML → build per-seat action queues for `id=2` and `id=10`.
5. Set `ScenarioRunOptions.PolicyOverride` to `XmlReplayBotPolicy` (per seat or shared catalog indexed by seat).
6. Use `ImmediateActionScheduler` (no artificial pacing delays during replay).

When `testrecordid` is absent/empty on all seats → unchanged `MinimalLandlordBot` path.

### New modules (`Games/TKLord/`)

| Module | Responsibility |
|--------|----------------|
| `XmlRecordParser` | Parse GB2312 case XML; extract all `<a id="2">` / `<a id="10">` per `s` (ignore XML `auto`) |
| `XmlRecordCatalog` | Load fixture by `testrecordid`; expose per-seat sequences for `id=2` and `id=10` |
| `XmlReplayBotPolicy` | Dequeue `id=2`/`id=10` on turn; honor `nextautopass`/`nextautogo`; sync queue on own-seat ack after server auto |

**Optional (non-blocking MVP):** After `CardsDealt`, compare dealt hands against recorded `id=27` `o` attribute (deal gate) for early mismatch detection.

### XML action reference (from fixtures)

Example structure under `<round>…<games><game>…<actions>`:

- `id=27`: deal snapshot in `o` (three bracket groups for seats) — optional client gate only.
- `id=2`: bid; `s` = seat, `o` = call value (e.g. `0`, `2`, `3`).
- `id=10`: play or pass; `s` = seat, `o` = card encoding string or `""` for pass. Optional attribute `auto="1"` marks server-recorded auto actions in Origin/Generate XML only — **not** used by track A to skip or filter queue entries (see **Auto play and auto PASS**).

Replay policy advances independent queues per seat for ids 2 and 10 in document order (filtering to `id=2` and `id=10` only — **not** by `auto`).

### Auto play and auto PASS (client replay)

When the server’s assist take-out logic decides the **next** seat does not need a human (or bot) request, it sets `nextautogo` or `nextautopass` on the current `LordTakeoutCardAck`. The integration-test client must stay aligned with the **live** server state machine, not only with the Origin XML sequence.

#### Why protocol fields, not XML `auto`

| Source | Use in track A |
|--------|----------------|
| `nextautopass` / `nextautogo` on ack | **Authoritative** for “should this seat send a req on this turn?” |
| XML `auto="1"` on `<a id="10">` | **Not used** for replay filtering; may differ between Origin recording and current Cases replay |

Assist outcomes depend on current hands and trick state; the same Origin line may be manual in the recording but auto-pass in replay (or vice versa).

#### Per-ack handling (`GameFlow` order)

For each `LordTakeoutCardAck` decoded to `GameEvent` (`CardsPlayed` / `PassPlayed`):

1. **`ApplyGameEvent` (all seats)**  
   - If `seat == mySeat`: server completed an action **for me** without my prior req (auto go/pass, or timeout/trust). **Advance** my `id=10` queue by one entry (sync with XML).  
   - If I had just sent a req (`awaitingOwnPlayAck`): clear flag on this ack; do **not** advance again (index was bumped in `TryDecide`).

2. **`TryDecide` (only when policy would act)**  
   - When `nextplayer == mySeat` (and other existing triggers: `LandlordDeclared`, `TurnStarted` lead, etc.):  
     - If `nextautopass == true` **or** `nextautogo == true` → **`return null`** (do not send `LordTakeoutCardReq`).  
     - Else → dequeue next `id=10` for `mySeat` from fixture and send play or pass.

This prevents racing the server: an ack that says “seat 1 will auto-pass” must not be followed by seat 1 immediately sending a pass from XML.

#### Queue sync rules (summary)

| Situation | Send req? | Advance XML `id=10` index |
|-----------|-----------|---------------------------|
| `nextplayer == me` and (`nextautopass` or `nextautogo`) | No | No (wait for ack with `seat == me`) |
| `seat == me` on ack, I did not just send | No | Yes (+1) |
| `nextplayer == me`, flags false | Yes (dequeue +1 on send) | +1 on send; ack clears `awaitingOwnPlayAck` |
| `player[x] timeout` then server acts | No until `seat == me` ack | Yes on `seat == me` ack |

#### Landlord first lead (no double-send)

`LandlordDeclared` and `TurnStarted` (lead seat) must consume **one** XML `id=10` only. Use `LandlordFirstLeadDone` (same as `MinimalLandlordBotPolicy`). If the first “your turn” signal is a takeout ack with `nextplayer == lordSeat` before operate-start, route through the same first-lead guard.

Advance `playIndex` only on **own-seat** `CardsPlayed`/`PassPlayed` ack (not when sending). While `awaitingOwnPlayAck`, do not call `DecidePlay` again. Reset bid/play indices on `CardsDealt`.

#### Plumbing (client modules)

| Module | Change |
|--------|--------|
| `ClassicLordVariant` | Map `takeoutAck.Nextautopass` / `Nextautogo` → `GameEvent.NextAutoPass` / `NextAutoGo` |
| `GameEvent` | Optional `bool? NextAutoPass`, `bool? NextAutoGo` |
| `XmlReplayBotPolicy.TryDecide` | Short-circuit when flags true and it is my turn to act |
| `XmlReplayBotPolicy.ApplyGameEvent` | Keep own-seat ack sync (covers auto + timeout) |
| `XmlRecordParser` | Include all `id=10` rows per seat; **do not** skip `auto="1"` |

#### Distinction from timeout / LordTrust

| Mechanism | Client behavior |
|-----------|-----------------|
| `nextautopass` / `nextautogo` | Predictable from ack; **no req** before server acts |
| `player[x] timeout` | Same sync pattern: wait for `seat == me` ack, then advance queue |
| LordTrust (`192.168.x:30021`) | Out of scope for replay policy; connection failures may cause timeout — still sync on broadcast, do not double-send from XML |

#### Example

1. Seat 0 plays; ack: `nextplayer=1`, `nextautopass=true`.  
2. Seat 1 client: `TryDecide` → null.  
3. Server auto-passes for seat 1; ack: `seat=1`, `cards=""`.  
4. Seat 1 client: `ApplyGameEvent` → advance queue (consumes recorded pass for seat 1).  
5. Ack: `nextplayer=2`, flags false → seat 2 dequeues and sends next play from XML.

---

## Integration points (client)

| Location | Change |
|----------|--------|
| `ClassicLordVariant` / `GameEvent` | Surface `TestRecordId` from `LordInitCardAck`; surface `NextAutoPass` / `NextAutoGo` from `LordTakeoutCardAck` |
| `ThreePlayersOneGameScenario.PlayGameAsync` (or equivalent) | After init phase: load catalog, validate fixture + seat agreement, set `PolicyOverride` before `GameFlow` |
| `ScenarioRunOptions` | Existing `PolicyOverride`; replay path sets `XmlReplayBotPolicy` + `ImmediateActionScheduler` via factory/options |
| `GameFlow` | Unchanged orchestration; consumes injected `IBotPolicy` |
| `ScenarioReport` / failure reports | Record `testRecordId` and expected fixture path on failure for debugging |
| `ActionSchedulerFactory` | Replay runs use `ImmediateActionScheduler.Instance` |

Existing scenario entry points (`RunAsync`, live transport, fake transport) stay; replay is orthogonal to transport mode.

---

## Data flow

```text
[Cases IT server]
  Origin\{testrecordid}.xml  ──deck──► LordInitCardAck(testrecordid, cards)
                    │
                    ▼
[3× LordUnion client]  load Cases\{testrecordid}.xml
                    │
                    XmlReplayBotPolicy: id=2, id=10 per seat
                    │
                    ▼
[Game completes]
  Server writes Generate\{testrecordid}.xml
  Server B: semantic diff Generate vs Origin
```

---

## Implementation order

### Client (`LordUnion.IntegrationTests`)

| Step | Deliverable |
|------|-------------|
| 1 | Proto regen + `TestRecordId` plumbing (`ClassicLordVariant` / events / generated types) |
| 2 | `XmlRecordCatalog` + fail-fast fixture resolution and three-seat `testrecordid` agreement |
| 3 | `XmlRecordParser` + `XmlReplayBotPolicy` + `ImmediateActionScheduler` wiring; auto play/pass via `nextautopass`/`nextautogo` |
| 4 | Fake tests: inject `LordInitCardAck` with `testrecordid`, assert replay sends |
| 5 | Live tests: `TK_INTEGRATION_TEST_USE_CASES` server, full scenario |

### Server (TKLordSvr)

Implement in parallel with client steps 1–3 where needed:

- Cases Origin sequential read + deck injection
- `testrecordid` on `LordInitCardAck`
- Generate write on game end
- B semantic diff oracle

---

## Verification

### Phase 1 — Unit (client)

```bash
dotnet test Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj --filter "FullyQualifiedName~XmlRecord"
```

- Parser tests against all **five** existing `Games/TKLord/Cases/*.xml` fixtures.
- Per-seat extraction counts for `id=2` and `id=10` match fixture expectations.

### Phase 2 — Fake (client)

```bash
dotnet test Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj --filter "FullyQualifiedName~XmlReplay&Category=Fake"
```

- Fake `LordInitCardAck` with `testrecordid` drives `XmlReplayBotPolicy` without live server.
- Missing fixture → fail before game (assert message mentions path).
- Mismatched `testrecordid` across seats → fail-fast.

### GameFlow end signals (client)

`GameFlow` ends on the first of: `LordResultAck`, `OverGameAck`, `HandOverAck`, or decoded `GameFinished`. If the match layer ends before `LordResultAck` is received, the test no longer waits for the full `GameOverTimeout`. Three-player scenarios use `TableGamePhaseCoordinator`: when **any** seat receives an end signal, remaining seats complete after **10s** grace (`EndSignal=TableGracePeriod`) instead of each waiting the full `GameOverTimeout`. Set `LORDUNION_TRACE=1` to log per-account `EndSignal`. Prefer `GameOverTimeout` of **3 minutes** per seat as a fallback (see `appsettings.example.json`).

### Phase 3 — Live (client + server)

- Deploy/run TKLordSvr built with `TK_INTEGRATION_TEST` + `TK_INTEGRATION_TEST_USE_CASES`.
- Live scenario: three accounts, one game; server assigns case via `testrecordid`.
- Success: game completes; server B diff passes; client scenario report success.
- Server logs confirm Generate written and Origin/Generate semantic match.

```bash
dotnet test Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj --filter "Category=Live&FullyQualifiedName~XmlReplay"
```

(Exact test class names follow implementation; filter by agreed `Category` / trait.)

### Regression

```bash
dotnet test Tests/LordUnion.IntegrationTests/LordUnion.IntegrationTests.csproj --filter "FullyQualifiedName~ThreePlayersOneGame&Category=Fake"
```

Empty `testrecordid` paths must still pass with `MinimalLandlordBot`.

---

## Fixture sync workflow

1. Server produces or curates golden XML under `Record\TKLord\Cases\Origin\{testrecordid}.xml`.
2. Copy/sync to `Tests/LordUnion.IntegrationTests/Games/TKLord/Cases/{testrecordid}.xml` (preserve GB2312).
3. Commit client fixtures with integration tests; server Origin remains authoritative for deck bytes.

---

## Risk & constraints

| Risk | Mitigation |
|------|------------|
| Seat mapping drift (XML `s` vs protocol seat) | Document mapping in `XmlRecordParser`; unit-test per fixture |
| GB2312 vs UTF-8 mis-read | Explicit GB2312 in parser; do not re-encode fixtures |
| Proto drift | Single source `Games/TKLord/Protocol/Raw/TKLord.proto`; regen script in CI/docs |
| Silent heuristic fallback | Hard fail when `testrecordid` set but fixture/policy missing |
| B false positives from metadata | Strict semantic comparator; ignore list documented above |
| Server auto pass/go vs XML replay race | Use `nextautopass`/`nextautogo`; no req when set; sync queue on own-seat ack |
| Double-send after timeout | `awaitingOwnPlayAck` + own-seat ack sync; do not use XML `auto` filter |

---

## Acceptance criteria

- [ ] With Cases server and synced fixture, one live three-player game completes using XML replay for all bids and plays.
- [ ] Server writes `Generate\{testrecordid}.xml` and B semantic diff vs Origin passes.
- [ ] Missing fixture or seat `testrecordid` mismatch fails before `GameFlow` with actionable report fields.
- [ ] Without `testrecordid`, existing `MinimalLandlordBot` scenarios unchanged.
- [ ] `LordSvr2BotRecordAck` not used for record id in integration tests.
- [ ] Client does not send take-out req when `nextautopass` or `nextautogo` applies to `nextplayer == mySeat`; queue stays aligned after server auto acts.
- [ ] Client replay does not filter `id=10` by XML `auto` attribute.
