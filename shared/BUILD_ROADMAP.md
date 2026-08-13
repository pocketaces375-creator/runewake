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

### P7-01 — First campaign node as teaching duel (r1_n01)

The first campaign node (r1_n01) IS the tutorial. A new profile starts at r1_n01. No separate tutorial mode. The player selects the node on the map, enters a real duel, and learns by playing the actual first fight of the campaign — with real rewards on the line.

**Design constraints (failures of the previous three iterations, resolved here):**

| Previous failure | This version |
|---|---|
| Separate mode — player knew it wasn't "real" | r1_n01 is the first campaign node. Real stakes, real rewards. |
| Random deck + random opponent = teachable moments missable | Fully scripted: fixed opening hand, fixed opponent deck, opponent plays the same cards every time. |
| Keywords (SWIFT) on tutorial cards with no explanation | Zero keywords in the teaching deck. Every card is a plain creature with attack + vigor only. |
| Timed banners vanished while player was figuring out controls | Modal popups with dimmed background and **Continue button only** — nothing advances or vanishes until the player taps. |
| "Tap the button" instructions with no why | Every popup explains the **concept** and then tells the player what to do. |
| Bot could block the face-hit moment | Opponent deck is scripted to leave lane 2 empty on turn 1. The face-hit moment is guaranteed. |
| Player could skip into a different game | Skip Tutorial on first popup only. If skipped, the player still has to beat r1_n01 to advance. |

**The TutorialPopup component (reusable — all future systems use this):**

A `TutorialPopup` is a `Control` node overlay that:

- Centers itself on screen as a rounded rectangle (ColorRect-based, no Panel to avoid Godot theme issues)
- Dims the entire board behind it (full-screen semi-transparent ColorRect)
- Contains: one **concept title** (bold, 16pt), two to three **sentences of explanation text** (14pt, autowrap), and a single **Continue** button
- Has an optional **Skip Tutorial** text link on the first popup only — opens a confirmation dialog
- Does NOT vanish on any timer. Only the Continue button dismisses it.
- **While the popup is open, the relevant UI element glows/points** to show where the concept lives on screen (arrow + pulsing border)
- Emits an `OnDismissed` event so the caller can set up the next popup or advance game state
- Accepts parameters: `popupId`, `title`, `text`, `highlightTarget` (optional node reference + rect), `showSkip` (bool)

Stored at `client/scripts/TutorialPopup.cs` — one file, no scenes.

**Beat-by-beat script — exact text of every popup:**

---

**POPUP 1: YOUR GOAL** (fires immediately when duel starts, before any card plays)

*Title:* **YOUR GOAL**

*Text:* "The enemy has 25 Vigor (health). Your creatures attack to reduce it. When their Vigor reaches 0, you win. Your own Vigor is on the left — protect yours while reducing theirs."

*Highlight:* Enemy Vigor number pulses golden. Player Vigor number pulses blue briefly.

*Skip Tutorial link:* Shown at bottom of this popup only. Reads "Skip Tutorial" in small grey text. Tapping it shows a confirmation: "Skip the tutorial? You'll still need to beat this duel."

*Continue →*

---

**POPUP 2: ATTUNEMENT** (fires after popup 1 is dismissed)

*Title:* **ATTUNEMENT**

*Text:* "Every card costs Attunement to play. Look at your hand: cards with a white border cost 1 — you can play those right now. Cards that are greyed out cost 2 or 3. You gain 1 more Attunement at the start of each of your turns."

*Highlight:* Cards in hand with cost 1 get a white border glow. Cards with cost >1 get a dim overlay. The Attunement counter (bottom center) pulses.

*Continue →*

---

**POPUP 3: SUMMONING** (fires after popup 2 is dismissed)

*Title:* **SUMMONING**

*Text:* "Tap a card in your hand to select it. Then tap an empty lane on your side of the board to summon that creature there. Creatures live in lanes and fight from them — each lane holds one creature."

*Highlight:* All cards in the hand glow (any can be tapped). The 5 empty player lanes flash white.

*Continue →*

---

**POPUP 4a: ATTACKING — YOUR TURN** (fires after the player successfully summons a creature)

*Title:* **ATTACKING**

*Text:* "A creature that was on your board at the start of your turn is ready to attack. Tap your creature to select it — its lane will glow. Then tap an enemy lane to choose your target."

*Highlight:* The player's creature on the board glows golden (ready to attack). Enemy lanes all flash briefly to show they're targetable.

*Advance condition:* Popup dismissed when the player taps their creature (enters attack-targeting mode).

*Continue →*

---

**POPUP 4b: ATTACKING — CHOOSING A TARGET** (fires when the player is in attack-targeting mode with a creature selected)

*Title:* **ATTACKING**

*Text:* "You selected your creature. Now choose a target lane on the enemy side. If the lane is empty, your creature hits the enemy directly — this is called a face attack and damages their Vigor. If the lane has an enemy creature, your creature fights that creature instead."

*Highlight:* Lane 2 on the enemy side has a pulsing arrow pointing to the enemy portrait. Lane 0 (where the bot's token sits) has a crosshair icon showing a creature is there.

*Continue →*

---

**POPUP 5: FACE HIT** (fires after the player attacks lane 2)

*Title:* **FACE HIT**

*Text:* "Direct hit! Your creature's attack value goes straight to the enemy's Vigor. Notice their Vigor number dropped. Keep attacking empty lanes to reduce it to 0. If an enemy creature is in your way, you'll have to destroy it first or choose a different lane."

*Highlight:* The enemy Vigor number pulses yellow where it changed. The damage number still floats on screen.

*Continue →*

---

**POPUP 6: THE TURN CYCLE** (fires after popup 5 is dismissed)

*Title:* **THE TURN CYCLE**

*Text:* "Each of your turns goes like this: gain 1 Attunement, draw 1 card, take your actions (summon, attack, use abilities), then tap End Turn to pass. Your creature that just attacked is now spent for this turn. End your turn so it can rest and be ready again."

*Highlight:* The End Turn button glows green.

*Continue →* — dismisses the tutorial overlay entirely. The player now plays the rest of the duel without guidance, winning by reducing enemy Vigor to 0.

---

**Visual requirements:**
- Popup container: centered, 80% screen width max, 60% screen height max, rounded corners (ColorRect with modulatable background), dark navy (`0.06, 0.06, 0.15, 0.95`) with a thin gold border (`0.8, 0.6, 0.2, 0.6`).
- Dim overlay: full-screen ColorRect, black at 0.55 alpha, blocks input behind popup.
- Title: 16pt bold, gold color (`0.9, 0.75, 0.3`).
- Body text: 14pt white, autowrap, generous line spacing 1.5.
- Continue button: 14pt white, navy background with gold border, centered at bottom of popup, 60% width.
- Skip Tutorial link: 12pt grey, below Continue, shows only on popup 1.
- Highlight pointers: arrow sprites (pre-loaded or code-drawn) that animate from the popup edge toward the highlighted UI element. Also highlight borders on the target element.

**Player deck — fully scripted, zero keywords:**

All cards are plain creatures (no keywords) with attack and vigor only. The opening hand is fixed, not drawn.

Opening hand (always these 4 cards, in this order):
1. `tut_c_student_of_embers` — cost 1, 2/1 — card text: "A simple creature of fire."
2. `tut_c_verdant_initiate` — cost 2, 2/3 — card text: "A sturdy forest follower."
3. `tut_c_student_of_embers` — cost 1, 2/1 — second copy
4. `tut_c_iron_apprentice` — cost 3, 3/3 — card text: "A slow but steady fighter."

Rest of the deck (drawn normally after turn 1): 8 more plain creatures at costs 1–3, no keywords, no abilities.

**Opponent deck — fully scripted, guarantees the teaching arc:**

The opponent's turn-1 play is forced: summon a 1/1 token into lane 0. Lane 2 stays empty. This guarantees an empty lane for the face-hit moment when the player attacks on turn 2.

After turn 1, the opponent plays normally (draws from a small deck of 1-cost plain creatures), but the opponent never fills more than 1 lane so at least one empty lane always remains for face damage.

**Full concept inventory — everything that will eventually need a TutorialPopup:**

| # | Concept | Doc reference | Status |
|---|---|---|---|
| 1 | Vigor / Win condition | `01_GAME_RULES.md` §7 | Defined |
| 2 | Attunement (resource, ramp, loss on EoT) | `01_GAME_RULES.md` §3 | Defined |
| 3 | Summoning to lanes | `01_GAME_RULES.md` §4 | Defined |
| 4 | Attacking: creature vs empty lane (face) | `01_GAME_RULES.md` §6 | Defined |
| 5 | Face hit (direct damage to Vigor) | `01_GAME_RULES.md` §6 | Defined |
| 6 | Turn cycle (Attune → Draw → Main → End) | `01_GAME_RULES.md` §5 | Defined |
| 7 | Exhaustion (creatures rest after summon/attack) | `01_GAME_RULES.md` §6 | Defined |
| 8 | Card types (Creature, Ritual, Relic, Curse, Token) | `01_GAME_RULES.md` §10 | Defined |
| 9 | Strata (the 5 colors — Verdant, Ember, Tide, Hollow, Dawn) | `01_GAME_RULES.md` §2 | Defined |
| 10 | Keywords (Guard, Swift, Pierce, Ward, etc.) | `01_GAME_RULES.md` §8 | Defined |
| 11 | Rune Pages (slots, RP budget, equipping) | `03_RUNE_SYSTEM.md` §1–3 | Defined |
| 12 | Rune fragments and forging | `03_RUNE_SYSTEM.md` §5 | Defined |
| 13 | Dig sites and excavation (grid, strikes, reveal) | `04_WORLD_AND_MAP.md` §4 | Defined |
| 14 | Dig charges (earning, spending) | `04_WORLD_AND_MAP.md` §4 | Defined |
| 15 | Barrow / Bury (third zone, inert cards) | `01_GAME_RULES.md` §9 | Defined |
| 16 | Excavate keyword (look N, take 1, Bury rest) | `01_GAME_RULES.md` §9 | Defined |
| 17 | Relics and Unidentified status (0/3 artifact, Identify condition) | `01_GAME_RULES.md` §9 | Defined |
| 18 | Lost Relic minting (engraving, discovery index) | `04_WORLD_AND_MAP.md` §4 | Defined |
| 19 | Campaign map (node graph, connections, lock states) | `04_WORLD_AND_MAP.md` §2 | Defined |
| 20 | Node types (Duel, Elite, Warden, Dig, Shrine, Cache, Merchant) | `04_WORLD_AND_MAP.md` §2 | Defined |
| 21 | Codex (lore entries, clue-gated CACHE nodes) | `04_WORLD_AND_MAP.md` §5 | Defined |
| 22 | Trinkets (small passives on map layer) | `04_WORLD_AND_MAP.md` §6 | Defined |
| 23 | Deck builder and collection | Roadmap P4-05 | Defined |
| 24 | **The Tower** | **Not in any doc** | **UNDEFINED** |
| 25 | **Delver Level** (XP, level thresholds, what unlocks) | `03_RUNE_SYSTEM.md` §2 references it but no XP table | **PARTIALLY DEFINED** |
| 26 | **Dig tools** (Brush, Spade, Rod, Lens — sources, stacking) | `04_WORLD_AND_MAP.md` §4 mentions them but no spec | **PARTIALLY DEFINED** |
| 27 | **Supabase account** (why link, what syncs, when prompted) | Master spec mentions auth only | **UNDEFINED** |

*Items 24–27 flagged as not fully defined in current docs. The TutorialPopup component will handle all 27 when they're ready — no per-system code changes needed, just content (popupId, title, text, highlightTarget).*

**Implementation files:**
- `client/scripts/TutorialPopup.cs` — NEW reusable popup component. One file, no scenes. Accepts popupId/title/text/highlightTarget/showSkip. ColorRect-based. Skip on first popup only.
- `client/scripts/TutorialController.cs` — rewrite: manage popup queue, track which popups have been shown, determine when to fire each popup based on game events (not step enum). Persist shown popups in save data so replayed duels don't re-teach.
- `client/scripts/DuelScene.cs` — add tutorial popup queue management in _Ready and OnStateChanged. Instantiate TutorialPopup at the right moments. Release game state for free play after popup 6.
- `client/content/encounters/region_01_early.json` — replace r1_duel_wayfarer deck with scripted opponent deck; add `"is_tutorial": true` flag
- `engine/Cards/EncounterDef.cs` — add `IsTutorial` bool field
- `client/content/cards/tutorial_pack.json` — new card pack containing the 4 plain tutorial creatures and the opponent's 1/1 token
- `client/scripts/Main.cs` — remove `ShouldRunTutorial()` / `StartTutorial()` logic — campaign always starts at r1_n01
- `client/scripts/MapScene.cs` — force r1_n01 as unlocked for new profiles; handle the case where the tutorial was skipped but r1_n01 must still be beaten

**Definition of Done:** A new player can start the game, navigate to the map, click r1_n01, see 7 modal popups in sequence with Continue-only advancement, correctly explain all 3 core concepts after finishing (Attunement resource, summon to lanes and attack empty lanes for face damage, win by reducing Vigor to 0), and beat the duel without further guidance. The TutorialPopup component accepts any (title, text, highlightTarget) triple and can be reused for future systems without code changes. Items 24–27 are flagged in this doc but not blocking P7-01.

(Skipping Excavate and Runes teaching duels for P7-01 — they share the same teaching structure and will be built in P7-02/P7-03.)

---

**P7-02** Onboarding tutorial duels 2 and 3 (Excavate, Runes).

---

Onboarding tutorial (first 3 duels teach lanes, then Excavate, then runes), Supabase account + relic ledger sync, telemetry, settings/accessibility, App Store and Play listings, privacy policy, age rating, TestFlight/closed beta with 50+ players, crash reporting, launch.

---

## PHASE 8+ — Live

Region expansions on a quarterly cadence, Codex season arcs, PvP (Delve and Pure queues), Museum/sharing screen, draft mode using the generation pipeline for on-the-fly sets.

---

## Critical path

`P0-02` (mobile export) and `P1-*` (engine) are the only true blockers. If the engine is right, everything else is replaceable. If the engine is wrong, everything else has to be rewritten. Spend the extra week on Phase 1.
