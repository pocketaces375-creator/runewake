# AGENT_LOG

## Session 1 — 2026-08-04
### P0-01 — Repo init
### P0-02 — Godot .NET mobile smoke test (scaffolding complete)
### P0-03 — Core state types

## Session 2 — 2026-08-04
### P0-03 — Core state types

## Session 3 — 2026-08-04
### P1-01 — Card definition model + JSON loader

## Session 4 — 2026-08-04

### P1-02 — Turn loop

Created `engine/Engine/` with the pure deterministic duel engine:

| File | Contents |
|---|---|
| `GameAction.cs` | Action discriminated union: `EndTurnAction`, `PlayCardAction`, `AttackAction` |
| `DuelEngine.cs` | `Apply(GameState, GameAction) -> GameState` with turn phase execution |

**Engine flow for EndTurn:**
1. **End phase** — hand truncation (max 10)
2. **Switch player** — toggle current player, increment turn number on wrap
3. **Attune phase** — +1 attunement max (cap 10), refill current attunement
4. **Draw phase** — draw top card from deck; first player skips turn-one draw; fatigue escalates (1, 2, 3...) on empty deck
5. **Start triggers** — no-op until P1-06
6. Returns state in Main phase, ready for player actions

**Fixes applied:**
- `PlayerState` constructor: `AttunementMax` starts at 0 (was 10), `Attunement` starts at 0 (was uninitialized)

**Tests:** 8 tests — attunement ramp, attunement cap at 10, turn number tracking, first-player draw skip, fatigue damage, fatigue escalation, fatigue can kill, hand truncation. 0 warnings, 0 errors.

## Session 5 — 2026-08-04

### P1-03 — Lane placement and combat

**Engine changes:**
- `CardInstance.cs`: Added `BaseAttack`, `BaseVigor`, `CardType`, `Cost`, `Keywords` list, and computed `CurrentAttack`/`CurrentVigor`/`EffectiveKeywords`. Updated clone constructor.
- `DuelEngine.cs`: Implemented `ApplyPlayCard` — validates attunement cost, places CREATURE/RELIC in lane (exhausts CREATURE unless Swift), discards RITUAL. Implemented `ApplyAttack` — validates Ready, resolves target (occupied lane, empty-lane face damage with Guard redirect, Pierce carry-through), handles simultaneous damage, removes dead creatures to discard, marks attacker used, checks game over.
- `GameAction.cs`: Added `Cost` to `PlayCardAction`.
- `FindGuardLane()`: Scans opponent's lanes for first Guard creature.
- `CheckGameOver()`: Sets `IsGameOver` and `WinnerIndex` when vigor reaches 0.

**Tests:** 17 new combat tests — play card to lane, cost deduction, occupied lane rejection, trade (both survive), one-sided kill, both die, defender in discard, face damage, face damage wins game, Guard redirect, Guard no-redirect when occupied, Guard first-lane selection, Pierce carry-through, Pierce exact-kill no-carry, exhausted attack rejected, double-attack rejected, empty-lane attack rejected, Swift not exhausted.

## Session 6 — 2026-08-04

### P1-04 — Keyword handlers

**Engine changes:**
- `KeywordHandlers.cs`: Static handler class with methods for all 11 keywords:
  - `OnPlay` — Swift (no-exhaust on summon), Ward (set WardRemaining=1), SummonedThisTurn (for Fragile)
  - `CanAttack` — Rooted returns false
  - `ResolveTargetLane` — validates Reach adjacency (source±1) or opposing-only
  - `ApplyWard` — absorbs one damage instance, decrements WardRemaining
  - `OnCombatDamageDealt` — Venom marks damaged creatures
  - `ResolveVenom` — destroys all marked creatures after combat
  - `OnDeath` — Unearth intercepts death, queues card for return
  - `ProcessUnearth` — returns queued cards to hand on turn start (cost deducted)
  - `ProcessFragile` — destroys Fragile creatures summoned this turn at end phase
  - `IsSealed` — returns true for Sealed creatures
- `CardInstance.cs`: Added `WardRemaining`, `IsVenomed`, `SummonedThisTurn`, `UnearthCost`
- `PlayerState.cs`: Added `UnearthQueue` (List of CardInstance)
- `GameAction.cs`: Added `TargetLane` to AttackAction (for Reach)
- `DuelEngine.cs`: Refactored to call KeywordHandlers instead of inline keyword checks; Guard/Pierce remain inline but use handler helpers. Fragile processed in End phase, Unearth in Start triggers.
- Files under 400 lines. No `TODO` or `NotImplementedException`.

**Tests:** 15 new keyword tests — Guard redirect, Swift no-exhaust, Pierce carry, Ward blocks one hit, Ward consumed, Venom destroys after combat, Reach adjacent attack, Reach non-adjacent rejects, Rooted can't attack, Unearth returns to hand, Unearth discards if unaffordable, Echo flag recognized, Fragile destroyed at end of turn, Fragile non-fragile survives, Sealed recognized as untargetable.

## Session 7 — 2026-08-04

### P1-05 — Effect executor

**New files:**
- `ResolvedTarget.cs`: `CreatureTarget` and `PlayerTarget` discriminated union for effect targeting
- `TargetResolver.cs`: Resolves `TargetDef` (scope + filter + count) into concrete target lists. All 7 scopes, all 17 filters, and count selection.
- `EffectExecutor.cs`: Executes all 23 OPs against resolved targets — DAMAGE, HEAL, BUFF, DEBUFF, DESTROY, DRAW, DISCARD, EXCAVATE, BURY, UNBURY, SUMMON, GRANT_KEY, REMOVE_KEY, SILENCE, BOUNCE, ATTUNE, MOVE_LANE, IDENTIFY, GAIN_VIGOR, LOSE_VIGOR, COPY, SET_STAT, REFRESH.

**Model changes:**
- `CardInstance.cs`: Added `Strata` field (for STRATA filter matching). Switched `CurrentAttack`/`CurrentVigor` from `int.Max` to `Math.Max`.

**Key implementation details:**
- EXCAVATE: deterministic — always picks the first of the revealed cards into hand, buries the rest
- SUMMON: finds first empty lane, creates a TOKEN-type card
- COPY: creates a fresh CardInstance copying source stats
- MOVE_LANE: moves source card to the first empty lane on its board
- All effects handle both CreatureTarget and PlayerTarget where applicable
- Files under 400 lines. No TODO or NotImplementedException.

**Tests:** 67 new tests — theory test covering all 23 OPs; detailed assertion tests for each OP (DAMAGE kills, DRAW adds cards, etc.); filter tests for all 17 filters (ADJACENT, OPPOSING, SAME_LANE, EDGE_LANE, CENTER_LANE, DAMAGED, UNDAMAGED, STRATA, KEYWORD, TYPE, LOWEST_VIGOR, HIGHEST_ATTACK, LOWEST_COST, HIGHEST_COST, CHOSEN); scope and count tests.

## Session 8 — 2026-08-04

### P1-06 — Trigger bus

**New files:**
- `TriggerBus.cs`: Deterministic trigger bus with chain depth cap at 20. Fires abilities ordered by controller first, then lane index. Supports compound conditions (all/any). Condition evaluation for all 13 condition ops.

**Integration with DuelEngine:**
- `ApplyPlayCard`: Fires `ON_SUMMON` after placing a CREATURE
- `ApplyAttack`: Fires `ON_DEATH` (via `FireDeathEvents`) when creatures die in combat
- `ApplyEndTurn`: Fires `ON_TURN_END` in the end phase, `ON_TURN_START` in the start triggers phase
- `EffectExecutor.KillCreature`: Also fires `ON_DEATH` for any destroyed creatures

**Model changes:**
- `CardInstance.cs`: Added `Abilities` list (List of AbilityDef) with deep clone support in the copy constructor.

**Key implementation details:**
- `Fire(GameState, Trigger, eventPlayerIndex)`: Collects matching abilities from all board creatures, ordered by controller then lane. Tracks `state.TriggerDepth` with a hard cap at 20.
- `FireDeathEvents`: Fires ON_DEATH for a specific dead card (since it's no longer on the board for the standard collection).
- Conditions evaluated: ALLY_COUNT_GTE, ENEMY_COUNT_GTE, BARROW_COUNT_GTE, HAND_COUNT_GTE/LTE, TURN_GTE, VIGOR_GTE/LTE, ATTUNEMENT_GTE, CONTROLS_KEYWORD, CONTROLS_STRATA, DAMAGED_THIS_TURN.
- Files under 400 lines. No TODO or NotImplementedException.

**Tests:** 7 new trigger tests — ON_SUMMON fires when creature played, ON_DEATH chain of 3 death triggers, trigger depth cap at 20 (at cap blocks, below cap fires), ON_TURN_START fires for next player, ON_TURN_END fires for ending player.

## Session 9 — 2026-08-04

### P1-07 — Barrow, Excavate, Bury, Relic identification

**Relic identification flow:**
- `ApplyPlayCard`: When a RELIC is played, BaseAttack is overridden to 0, BaseVigor to 3, IsIdentified=false, IsExhausted=true. CREATURE handling is unaffected.
- `ApplyEndTurn` (start triggers phase): After Unearth and ON_TURN_START, calls `IdentifyRelics()` to check the next player's lane relics. Any with a met identify condition flips (IsIdentified=true) and fires ON_RELIC_IDENTIFY triggers.
- `CardInstance.cs`: Added `IdentifyCondition` field (ConditionDef?) and deep clone support via `CopyCondition()` helper.
- `TriggerBus.cs`: Added public `EvaluateCondition()` wrapper for use by the engine.

**Already implemented in P1-05:** EXCAVATE, BURY, UNBURY ops in EffectExecutor. BARROW zone in PlayerState.

**Tests:** 6 new tests — relic enters as unidentified 0/3, relic stats overwritten on play, relic identifies when condition met at turn start (BARROW_COUNT_GTE), relic stays unidentified when condition not met, ON_RELIC_IDENTIFY fires abilities (draws cards), EXCAVATE from ON_SUMMON works end-to-end.

## Session 10 — 2026-08-04

### P1-08 — Replay determinism

**New infrastructure:**
- `engine/State/GameConfig.cs` — seed + contentVersion + player deck ID lists. Fully determines initial state.
- `engine/Cards/CardRegistry.cs` — thread-safe static registry mapping card def IDs to `CardDef` objects.
- `engine/State/GameState.cs` — added `GameState.Initialize(GameConfig)` factory: resolves card defs via registry, shuffles decks with seeded Fisher-Yates, deals starting hands (P0=4, P1=5), sets P1 starting attunement. Added `ComputeStateHash()` — deterministic FNV-1a over all observable state fields (player stats, zones, lanes, creature state, effective keywords).
- `engine/Engine/ReplayLog.cs` — JSON-serializable envelope `(config, actions[])` with polymorphic `GameActionConverter` using `$type` discriminator.
- `engine/Engine/ReplayRunner.cs` — static `Replay(ReplayLog)` → creates fresh state from config, applies all actions sequentially.

**Model changes:**
- `GameState.ActionLog` changed from `List<object>` to `List<GameAction>` for type safety.

**Tests:** 1 fuzz test (`Fuzz200_ReplayedGames_ProduceIdenticalFinalState`) — 200 random legal games (20 seeds × 10 variants), each played by a bot that picks random valid actions from the hand/board state. For each game, the action log is JSON-serialized, deserialized, and replayed. Original and replayed final state hashes are asserted equal. All pass.

**Files changed:** 6 files added, 2 modified. 0 TODO, 0 NotImplementedException.

## Session 11 — 2026-08-04

### P2-01 — Greedy heuristic bot

**New file:**
- `sim/Bot.cs` — `GreedyBot` class with three public methods:
  - `Evaluate(GameState, int playerIndex)` → score = Σ(ally creature ATK+VIG) + ally Vigor − Σ(enemy creature ATK+VIG) − enemy Vigor
  - `EnumerateValidActions(GameState, int playerIndex)` → all legal play/attack/end-turn actions
  - `ChooseAction(GameState, int playerIndex)` → evaluates each action one-ply deep, picks the highest-scoring

**Model changes:** None. Test project gained a `ProjectReference` to `Runewake.Sim`.

**Tests:** 8 new unit tests:
- `Evaluate_EmptyBoard_ReturnsVigorDifference` — score 0 when both sides equal
- `Evaluate_AllyHasCreature_ReturnsPositiveScore` — creature stats counted
- `Evaluate_EnemyHasStrongerCreature_ReturnsNegativeScore` — negative when outmatched
- `EnumerateActions_EmptyBoardNoHand_OnlyEndTurn` — only legal move is pass
- `EnumerateActions_CardsInHand_IncludesPlayActions` — play actions per empty lane
- `ChooseAction_PrefersPlayingCreatureOverEndTurn_WhenHandHasPlayableCard` — bot plays a 4/4 instead of passing
- `ChooseAction_PrefersAttackingOverEndTurn_WhenCreatureIsReady` — bot attacks over doing nothing
- `ChooseAction_PrefersAttackingGuardOverEndTurn_WhenCreatureIsReady` — bot attacks Guard even when face is blocked

All 142 tests pass. 0 TODO, 0 NotImplementedException.

## Session 12 — 2026-08-04

### P2-02 — Batch runner

**New files:**
- `sim/BatchRunner.cs` — `BatchRunner.Run(BatchConfig)` plays N games between two `GreedyBot` instances, each with a unique seed offset. Returns `BatchReport` with per-game results and aggregates (wins, win rate, average turns). `BatchReport.ToJson()` for JSON output.
- `sim/Program.cs` — CLI entry point: `Runewake.Sim run --deck-a <path> --deck-b <path> [--games N] [--seed N]`. Loads card packs via `CardLoader`, registers them with `CardRegistry`, runs batch, prints JSON report to stdout.

**Supporting types:** `BatchConfig` (seed, games, deck paths), `GameResult` (game index, winner, turns, final stats), `BatchReport` (aggregates + result list).

**Tests:** 4 new tests:
- `RunBatch_100Games_ProducesReport` — 100 games, verifies report structure and win/turn sanity
- `RunBatch_JSONSerialization_ProducesValidJson` — report serializes to well-formed JSON with expected keys
- `RunBatch_DifferentDecks_CanSeeImbalance` — tank deck (3/5) beats scout deck (1/1) as expected
- `RunBatch_SameSeed_ProducesDeterministicResults` — same config produces identical results

All 146 tests pass. 0 TODO, 0 NotImplementedException.

## Session 13 — 2026-08-04

### P2-03 — Card validator CLI

**New files:**
- `sim/CardValidator.cs` — `CardValidator.Validate(CardDef)` checks all schema constraints: required fields, ID format, name length, cost range, type-specific rules (CREATURE needs attack/vigor, RITUAL must not have attack/vigor, RELIC needs identify_condition), keyword/ability limits, nested ability/effect/target/condition validation.
- `tests/Engine/CardValidatorTests.cs` — 20 tests covering valid creatures, rituals, relics; invalid ID, name, cost, missing attack/vigor, wrong type fields, too many keywords/abilities, unknown keyword, negative power score, low content version, long flavor.

**Model changes:** `CardLoader.JsonOptions` made public (was private) for reuse by validator CLI.

**CLI:** `Runewake.Sim validate-card <card-file>` — loads a JSON card pack, validates each card, prints `[✓]` or `[✗]` with error details. Exits 0 on success, 1 on errors.

**Tests:** 20 new unit tests (166 total). All pass. Verified on 6 example cards — all pass. 0 TODO, 0 NotImplementedException.

## Session 14 — 2026-08-04

### P2-04 — 60 hand-authored cards (12 per Stratum)

**New content:** 60 playable cards + 1 token across 5 JSON pack files:

| File | Cards | Strata |
|------|-------|--------|
| `content/cards/verdant.json` | 12 + token | VERDANT — life, healing, growth |
| `content/cards/ember.json` | 12 | EMBER — aggression, burn, speed |
| `content/cards/tide.json` | 12 | TIDE — knowledge, excavation, control |
| `content/cards/hollow.json` | 12 | HOLLOW — death, unearth, decay |
| `content/cards/dawn.json` | 12 | DAWN — protection, order, buffs |

**Card distribution per stratum:** 8 creatures, 3 rituals, 1 relic. Rarities: 4 COMMON, 4 UNCOMMON, 3 RARE, 1 RELIC. Keywords used: GUARD, SWIFT, PIERCE, WARD, VENOM, REACH, ROOTED, UNEARTH, ECHO, FRAGILE, SEALED. All triggers, ops, and effects use the engine's existing DSL. Every card validated with `validate-card` CLI — 0 errors.

These are the balance anchors the AI-generated cards will be measured against.

All 166 tests pass. 0 TODO, 0 NotImplementedException.

## Session 15 — 2026-08-04

### P2-05 — Rules text renderer

Created `engine/Cards/RulesTextRenderer.cs` with `Render(CardDef)` and `RenderAbilityTextOnly(CardDef)` — full human-readable rules text from the DSL, covering:

- **Stat line**: `A/V` for creatures, keyword badges
- **Triggers**: all 14 trigger types rendered to natural English ("When this enters play", "At the start of your turn", etc.)
- **Effects**: all 22 ops with target scope, filter, and count phrases
- **Conditions**: all 13 condition ops with value formatting
- **Identify**: relic identify conditions rendered inline
- **Keywords**: all 11 keywords formatted as display names
- **Flavor**: quoted flavor text appended last

Created `tests/Cards/RulesTextSnapshotTests.cs` — 61 snapshot tests, one per card (60 hand-authored + 1 token), each asserting the exact rendered output. Every card's text verified correct.

227 tests total (166 existing + 61 new). 0 TODO, 0 NotImplementedException.

## Session 16 — 2026-08-04

### P3-01 — Duel scene layout

Created the duel scene visual shell in the Godot client:

- **`client/scenes/duel/DuelScene.tscn`** — full-screen Control with: enemy HUD (name, vigor, attunement), 5+5 lane rows with divider, player HUD, and hand fan area at the bottom
- **`client/scripts/DuelScene.cs`** — `PopulateLanes()` instantiates 10 `LaneSlot` instances (5 per row), `SetTestData()` places test creatures + hand cards for visual verification
- **`client/scenes/components/LaneSlot.tscn`** — panel container with dark theme, shows card name + A/V stats, `LaneSlot.cs` has `SetCard()`/`SetEmpty()` and `Row`/`LaneIndex` metadata
- **`client/scenes/components/HandCard.tscn`** — small tappable button showing card name + cost, `HandCard.cs` has `SetCard(cardId, name, cost)`
- **`project.godot`** — `main_scene` switched from `Main.tscn` to `DuelScene.tscn`

Client builds with `dotnet build` — 0 errors. Engine tests still at 227/227 passing.

Next: **P3-02** — Card view component driven entirely by CardDef + rendered text + art path.

## Session 17 — 2026-08-04

### P3-02 — Card view component

Created a full card view driven entirely by CardDef data:

- **`client/scripts/CardView.cs`** + **`client/scenes/components/CardView.tscn`** — self-contained card component (180×280) showing:
  - **Header**: cost badge + card name
  - **Art**: strata-colored placeholder (or loads from `Art.Asset` path when available)
  - **Type line**: "Creature · Verdant · Common" etc.
  - **Keywords**: formatted keyword badges
  - **Rules text**: rendered via `RulesTextRenderer.RenderAbilityTextOnly()` (ability text only)
  - **Flavor**: quoted italic text
  - **Stats**: Attack/Vigor panel (only shown for creatures/tokens)
  - **Border**: strata-colored with StyleBoxFlat (green/red/blue/purple/gold)
  - Includes `SetCard(CardDef)` and `Clear()` methods

- **`engine/Cards/RulesTextRenderer.cs`** — added `RenderAbilityTextOnly(CardDef)` method (renders just abilities, no stats/flavor/keywords), made `FormatKeyword()` public for reuse by the client

Engine: 227/227 tests pass. Client: `dotnet build` 0 errors. 0 TODO, 0 NotImplementedException.

Next: **P3-03** — Input: drag card to lane, tap creature then tap target lane to attack.

## Session 18 — 2026-08-04

### P3-03 — Input: drag card to lane, tap creature to attack

Wired up the full input flow for the duel scene:

- **`client/scripts/InputController.cs`** — input state machine with two states:
  - `Idle`: waiting for player action
  - `SelectingAttacker`: player has tapped a friendly creature and must pick a target
  - Events: `PlayCardRequested(cardId, laneIndex)`, `AttackRequested(attackerLane, targetLane)`, `SelectionCancelled`
  
- **`client/scripts/HandCard.cs`** — added `_GetDragData()` returning a `Dictionary{type, card_id, card_name, card_cost}` for Godot's drag-and-drop system; drag preview shows card name

- **`client/scripts/LaneSlot.cs`** — added:
  - `_CanDropData()` / `_DropData()` for accepting hand card drops on empty player lanes
  - `_GuiInput()` for tap detection → emits `LaneTapped` and `CardDropped` signals
  - `Highlight()` / `Unhighlight()` for visual feedback during attack targeting

- **`client/scripts/DuelScene.cs`** — rewired to connect all signals:
  - `OnCardDropped` → `InputController.TryPlayCard()`
  - `OnLaneTapped` → state-machine dispatch: idle→select attacker, selecting→confirm/cancel
  - `OnHandCardPressed` → cancel attack selection
  - `OnSelectionCancelled` → clears all highlights

Client builds with `dotnet build` — 0 errors. Engine tests still 227/227 passing.

Next: **P3-04** — Engine binding: client holds GameState, sends actions, re-renders from returned state.

## Session 19 — 2026-08-04

### P3-04 — Engine binding: GameState lifecycle in the client

Wired the client to the deterministic engine:

- **`client/scripts/GameStateManager.cs`** — manages the GameState lifecycle:
  - `Initialize(GameConfig)` / `InitializeTestGame(seed)` sets up a fresh game
  - `TryPlayCard(playerIndex, cardDefId, laneIndex)` / `TryAttack(playerIndex, source, target)` / `TryEndTurn()` dispatch `Engine.Apply()`
  - On each state change: raises `StateChanged` event for UI re-render; raises `GameOver` on win
  - Query helpers: `GetHand()`, `GetLanes()`, `GetPlayerHud()` return DTOs for rendering
  - Data structs: `HandCardInfo`, `LaneInfo`, `PlayerHudInfo`

- **`client/scripts/DuelScene.cs`** — fully rewired:
  - `OnStateChanged()` → `RenderHud()`, `RenderBoard()`, `RenderHand()` rebuild all UI from state
  - `OnPlayCardRequested` / `OnAttackRequested` → `_gsm.TryPlayCard()` / `_gsm.TryAttack()`
  - `LoadCardPacks()` loads all 5 strata packs into CardRegistry at startup
  - `InitializeTestGame()` creates a 30-card test game with real playable cards
  - Added `TurnLabel` to the scene for turn indicator + game-over message

- **Test flake fix**: Added `[Collection("NonParallel")]` to `ReplayDeterminismTests`, `RulesTextSnapshotTests`, and `BatchRunnerTests` to prevent parallel-test races on the shared static `CardRegistry`

Client builds with `dotnet build` — 0 errors. Engine tests: 227/227 pass (stable across repeated runs).

Next: **P3-05** — Animation and feedback layer (damage numbers, death, summon).

## Session 20 — 2026-08-04

### P3-05 — Animation and feedback layer

**New files:**
- `client/scripts/effects/FloatingText.cs` + `client/scenes/effects/FloatingText.tscn` — reusable floating text label that tweens upward, fades out, then frees itself. Used for damage numbers (red, "-X") and heal numbers (green, "+X").

**Modified files:**
- `client/scripts/LaneSlot.cs` — added:
  - `PreviousVigor` property for tracking vigor diffs
  - `PlaySummonEffect()` — scale from 0→1 with OutBack easing (pop-in)
  - `PlayDeathEffect()` — fade to transparent + scale to 0 with InBack easing, then reset
  - `ShowDamageNumber(int)` / `ShowHealNumber(int)` — spawn FloatingText at lane position
- `client/scripts/DuelScene.cs` — added state snapshotting for diff-based animation:
  - Captures board state snapshot before each render pass
  - After render, compares against the previous snapshot to detect:
    - Empty → occupied: triggers `PlaySummonEffect` on the lane slot
    - Occupied → empty: triggers `PlayDeathEffect`
    - Vigor decreased: triggers `ShowDamageNumber`
    - Vigor increased: triggers `ShowHealNumber`
  - Also tracks player vigor for face-damage/heal floating numbers
  - Skips animation on the first (initialization) render

Client builds with `dotnet build` — 0 errors. Engine tests: 227/227 pass. 0 TODO, 0 NotImplementedException.

Next: **P3-06** — Bot opponent wired in with a small think-delay.
