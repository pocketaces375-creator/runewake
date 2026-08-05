# 06 — Build Roadmap

Every ticket is scoped for a fast, small coding model: **one concern, explicit file paths, a stated Definition of Done, and a test.** Do them in order. Do not start a ticket until the previous one's tests pass.

---

## PHASE 0 — Skeleton (target: 1 week)

**P0-01 — Repo init**
Create the repo structure below. Add `.gitignore`, `README.md`, MIT-or-proprietary license file, and a `docs/` folder containing these spec files.
```
/engine      Runewake.Engine   (C# .NET 8 class library)
/sim         Runewake.Sim      (C# console app)
/tests       Runewake.Tests    (xUnit)
/client      Godot 4 project
/pipeline    Python content pipeline
/content     JSON packs, maps, codex
/schema      JSON schemas
/docs        these files
```
DoD: `dotnet build` succeeds at solution root; `dotnet test` runs with 0 tests and exits 0.

**P0-02 — Godot .NET mobile smoke test (DO THIS EARLY)**
Empty Godot 4.3 C# project displaying a label, exported and installed on one physical Android device and one iOS device (or simulator + TestFlight). This de-risks the single riskiest stack assumption in the whole plan.
DoD: screenshot of the app running on hardware. If this fails after a genuine attempt, STOP and escalate to the human — the Flutter fallback in `00_MASTER_SPEC.md` §3 is on the table.

**P0-03 — Core state types**
`engine/State/`: `GameState`, `PlayerState`, `LaneState`, `CardInstance`, `SeededRng`. Plain data, no logic beyond a deep `Clone()`.
DoD: unit test clones a state, mutates the clone, asserts the original is unchanged.

---

## PHASE 1 — The engine (target: 3 weeks)

**P1-01 — Card definition model + JSON loader**
`engine/Cards/CardDef.cs`, `AbilityDef.cs`, `EffectDef.cs`, `TargetDef.cs`, `ConditionDef.cs` + `CardLoader.LoadPack(path)`. Enums exactly as in `docs/02_CARD_DSL.md` §2.
DoD: loads `schema/example_cards.json`, all 6 cards, asserts field values.

**P1-02 — Turn loop**
Attune → Draw → StartTriggers → Main → End. Actions: `PlayCard`, `Attack`, `EndTurn`. `Engine.Apply(state, action) -> state`.
DoD: test plays a 10-turn game of nothing but EndTurn; asserts attunement caps at 10 and fatigue triggers correctly.

**P1-03 — Lane placement and combat**
Summon into a chosen empty lane; attack resolution per `01_GAME_RULES.md` §6, including empty-lane face damage and Guard redirection.
DoD: tests for trade, one-sided kill, face damage, Guard forcing, Pierce carry-through.

**P1-04 — Keyword handlers**
All 11 keywords from `01_GAME_RULES.md` §8, one handler each.
DoD: one unit test per keyword. Eleven tests.

**P1-05 — Effect executor**
Implement every `OP` in the DSL against `TargetDef` resolution. Targeting resolution is its own function with its own tests.
DoD: table-driven test covering each op at least once and each filter at least once.

**P1-06 — Trigger bus**
Register/fire triggers, ordered deterministically (controller first, then lane index). Cap trigger chain depth at 20 with a hard stop to prevent loops.
DoD: test with a death-trigger chain of 3; test that a forced loop terminates.

**P1-07 — Barrow, Excavate, Bury, Relic identification**
The signature mechanics from `01_GAME_RULES.md` §9.
DoD: relic enters unidentified, condition met at turn start, flips, ability comes online.

**P1-08 — Replay determinism**
Serialize `(seed, contentVersion, actions[])`; replay reproduces identical final state.
DoD: fuzz test — 200 random legal games, each replayed, states hash-equal.

---

## PHASE 2 — Simulator and first cards (target: 2 weeks)

**P2-01 — Greedy heuristic bot** — one-ply evaluation, board-state score = own stats + vigor − enemy equivalent. `sim/Bot.cs`.
**P2-02 — Batch runner** — `Runewake.Sim run --deck-a x.json --deck-b y.json --games 1000 --seed 42`, outputs win rate, avg turns, JSON report.
**P2-03 — Card validator CLI** — `Runewake.Sim validate-card <file>` for pipeline Stage 3.
**P2-04 — 60 hand-authored cards** — 12 per Stratum, human-designed. These are the balance anchors the AI-generated cards will be measured against. Do not skip this and start with generated cards.
**P2-05 — Rules text renderer** — `AbilityDef -> string`. Snapshot tests on all 60 cards.

---

## PHASE 3 — Playable duel client (target: 4 weeks)

**P3-01** Duel scene layout: 5+5 lanes, hand fan, attunement track, vigor counters.
**P3-02** Card view component driven entirely by `CardDef` + rendered text + art path.
**P3-03** Input: drag card to lane, tap creature then tap target lane to attack.
**P3-04** Engine binding: client holds `GameState`, sends actions, re-renders from returned state. **The client never mutates game state directly.**
**P3-05** Animation and feedback layer (damage numbers, death, summon).
**P3-06** Bot opponent wired in with a small think-delay.
DoD for Phase 3: a full duel is playable start to finish on a phone.

---

## PHASE 4 — Campaign (target: 3 weeks)

**P4-01** Map data format + loader (`04_WORLD_AND_MAP.md` §2).
**P4-02** Map screen: pan/zoom node graph, lock states, node icons.
**P4-03** Encounter definitions: wielder name, portrait, deck, dialogue, rewards.
**P4-04** Progression save: SQLite, node clears, collection, shards, dig charges.
**P4-05** Deck builder screen with collection filtering.
**P4-06** Region 1 content: 10 nodes, 6 wielders, 1 Warden, 1 Warden Boss.

---

## PHASE 5 — Runes and excavation (target: 3 weeks)

**P5-01** Rune definitions reusing `AbilityDef`; RP budget validation.
**P5-02** Rune page editor UI (9/9/9/3 slots, budget bar).
**P5-03** Runes injected at match start; tests confirming each starter rune fires.
**P5-04** Dig site interaction (grid, strikes, reveals).
**P5-05** Fragment → rune forging; tools.
**P5-06** Lost Relic minting: engraving data, frame renderer, local ledger.

---

## PHASE 6 — AI pipeline (target: 4 weeks)

**P6-01** Card JSON Schema finalized. **P6-02** Generate module. **P6-03** Validate module (schema + engine bridge). **P6-04** Score module. **P6-05** Simulate module. **P6-06** Dedupe + moderate. **P6-07** Art module. **P6-08** Review UI. **P6-09** Publish + content versioning + client hot-update. **P6-10** Pipeline orchestration + one 60-card set end to end. **P6-11** Stage-schema continuity + report hardening.
DoD: produce one 60-card set end to end with under 15% rejection at Stage 3.

### P6-11 — Stage-schema continuity + report hardening

**Status:** Logged (from P6-10 findings)

**Problem — stage-schema discontinuity.** Each stage passes a different JSON
schema downstream, so the pipeline loses card identity between stages:
- `GENERATE` → `01_raw.json` = full `CardDef` objects (has `name`, `strata`, `art.prompt`).
- `SIMULATE` → `04_simulated.json` = sim-result objects (`card_id`, `card_name`,
  `matchup_results`, `avg_delta`, `flags`) — **not** `CardDef`.
- `DEDUPE+MODERATE` → `05_deduplicated.json` = passes through the sim-result shape.
- `ART` expects full `CardDef` (needs `name`/`strata`/`art.prompt`) but receives the
  sim-result shape, so it rendered cards as `card_000` with `VERDANT` fallback strata.

**Workaround in place (P6-10):** the orchestrator merges `02_valid.json` card
definitions back into `05_deduplicated.json` before ART, keyed by card id. This
is a band-aid, not a fix.

**Required fix:** define one canonical stage-boundary schema. Each stage should
carry the full `CardDef` forward and attach its stage-specific results as a
nested field (e.g. `card["simulation"]`, `card["dedupe"]`) rather than replacing
the card with a results-only object. Investigate having each stage read the
original `01_raw.json`/`02_valid.json` and join by id, or restructure the
intermediate files to include BOTH the card and its stage metadata.

**Also in scope — report hardening:** the P6-10 acceptance report had several
impossible-value bugs (dedupe stage reported 31/12 rejects by counting prior
stages' reject files; cost reported $0.00 despite 12 image calls; seeded count
reported 0). These are fixed in the orchestrator with stage-scoped reject
prefixes, cost parsing from actual stage output, seeded-count read from the
seed file, and a `_validate_report()` sanity check that fails on impossible
values. Tests in `pipeline/tests/test_orchestrator.py`.

---

## PHASE 7 — Ship (target: 4 weeks)

Onboarding tutorial (first 3 duels teach lanes, then Excavate, then runes), Supabase account + relic ledger sync, telemetry, settings/accessibility, App Store and Play listings, privacy policy, age rating, TestFlight/closed beta with 50+ players, crash reporting, launch.

---

## PHASE 8+ — Live

Region expansions on a quarterly cadence, Codex season arcs, PvP (Delve and Pure queues), Museum/sharing screen, draft mode using the generation pipeline for on-the-fly sets.

---

## Critical path

`P0-02` (mobile export) and `P1-*` (engine) are the only true blockers. If the engine is right, everything else is replaceable. If the engine is wrong, everything else has to be rewritten. Spend the extra week on Phase 1.
