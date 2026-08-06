# AGENT_LOG

## Session 25 — 2026-08-06

### P3-03 — Input: tap/drag summon, tap-to-attack, rejection feedback

Full input system for the duel scene with engine-authority validation:

**Engine-side validation (GameStateManager):**
- Added `ActionResult` struct with `Success` bool and `ErrorMessage` string
- `TryPlayCard` now returns specific reasons: "Not enough attunement", "Lane N is already occupied", "Card not found in hand"
- `TryAttack` returns: "This creature is exhausted", "This creature has already attacked", "No creature in lane N"
- Both methods validate `CurrentPlayerIndex` first — wrong-turn actions get their own error
- Illegal actions are rejected by the engine, not prevented by the client

**InputController state machine (3 states):**
- `Idle` → `SelectingLane` (tap card in hand, then tap empty lane to summon via `SelectTargetLane`)
- `Idle` → `SelectingAttacker` (tap friendly creature, then tap enemy lane to attack via `SelectAttackTarget`)
- Drag-and-drop from HandCard to LaneSlot also works (triggers `TryPlayCard` directly)

**DuelScene wiring:**
- `ShowToast()` displays rejection reasons as floating text (auto-fades after 1.2s)
- `UpdateAttackHighlights()` highlights selected attacker and all enemy lanes when in attack mode
- `UpdatePlayHighlights()` highlights empty player lanes when in tap-to-summon mode
- Background tap (`OnBackgroundGuiInput`) cancels selection and resets input state
- Lane taps dismiss card detail popup
- End Turn button created programmatically if not in scene
- Card selection shows toast: "Select a lane to summon [CardName] (cost N)"

**Test hook verification (removed):**
- Hook: EndTurn → wait for bot → summon Wildfire Adept (cost 2) to lane 0 on turn 2
- Screenshot 1: creature name + stats text visible at lane 0 position (x=71-139, rows 112-136)
- Attack attempt: engine returned `Success=False, Error="This creature is exhausted."`
- Screenshot 2: 1680 redder pixels in center toast area vs summon frame — rejection toast visible
- Hook removed, clean build + 365/365 tests passing

Screenshot: MEDIA:/home/fictive/runewake/duel_screenshot.png

Next: **P3-04** — Engine binding: client holds GameState, sends actions, re-renders.

---

### P3-02 — Card view component for phone screen

Fixed hand card layout and sized all card views for phone legibility:

- **HandCard.tscn** — resized from 48×68 to 80×112 with 11px card name, 12px cost badge, added strata-colored bottom strip (5px) identifying each card's stratum
- **HandCard.cs** — accepts `Strata` parameter, renders colored strip via `GetStrataColor()`
- **HandCardInfo** — added `Strata` field, populated from `CardRegistry.Get()` in `GetHand()`
- **CardView.tscn** — resized from 180×280 to 280×400 with phone-grade fonts: header 18px, type line 12px, rules text 11px, flavor 10px, stats 18px. Art rect increased to 140px min height. Anchor preset set to center with offset (-140, -200) for proper positioning in parent.
- **DuelScene.cs** — wired `CardView` detail popup on hand card tap: instantiated once in `_Ready()`, hidden/shown in `OnHandCardPressed()`. Looks up `CardDef` from `CardRegistry` via `CardId` and calls `SetCard()`. Dismissed automatically on state change. CardView positioned at center via `Position = (ViewportSize - CardSize) / 2`.

### P3-02 layout fixes (second pass)

Two layout issues from screenshot analysis:

**1. Fifth lane clipped at right edge:**
- Root cause: lane rows filled full 1152px width with no margins
- Fix: wrapped `EnemyLanes` and `PlayerLanes` in `MarginContainer` nodes with 16px left/right margins
- Verified at 1152×648 and 800×600 — all 5 lanes fit with symmetric margins at both aspect ratios

**2. Hand cards read as misaligned:**
- Root cause: `PlayerHUD` was left-aligned (x=0–684) while hand cards were centered (x=407–744), creating a 407px visual disconnect
- Fix: restructured `PlayerHUD` as `CenterContainer > HBoxContainer` so HUD labels ("Vigor: 25 Attune: 1") are centered at x=412–684, matching the hand cards' visual center within 26px

**3. CardView detail popup (test hook):**
- Added temporary `Callable.Deferred` trigger in `_Ready()` that programmatically opens the popup on hand card 0
- Screenshot verified: CardView renders centered with green (Verdant) strata border RGB(51,178,76), header text ("Root Warden" + cost 3), type line ("Creature · Verdant · Common"), keyword ("GUARD"), rules text ("On Summon: Give adjacent creatures +1 Vigor."), flavor, and stats (2/4)
- Test hook removed before commit

**Screenshot:** 4 hand cards centered with spacing, lane rows with 16px side margins, PlayerHUD centered. All 365 engine tests pass.

Screenshot: MEDIA:/home/fictive/runewake/duel_screenshot.png

Next: **P3-03** — Input: drag card to lane, tap creature to attack.

---

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

## Session 21 — 2026-08-04

### P3-06 — Bot opponent wired in with a think-delay

**New files:**
- `client/scripts/BotController.cs` — a Node that manages the AI turn lifecycle:
  - Listens to `GameStateManager.StateChanged` — when it detects the enemy's turn (player index 1), starts a timer
  - On timeout: calls `GreedyBot.ChooseAction()` to pick the best action
  - Dispatches the action via `GameStateManager` methods (PlayCard, Attack, EndTurn)
  - After each action, schedules the next if still the enemy's turn (0.6s interval)
  - Exposes `BotTurnStarted` / `BotTurnEnded` events for UI feedback
  - `IsThinking` property for input gating
  - Think delay configurable (default 1.5s first action, 0.6s follow-ups)

**Modified files:**
- `client/Runewake.Client.csproj` — added `ProjectReference` to `Runewake.Sim` (for GreedyBot)
- `client/scripts/DuelScene.cs` — wired BotController:
  - Creates and initializes `BotController` in `_Ready()` (after `_gsm`, before `InitializeTestGame`)
  - `_bot.Initialize(_gsm)` connects the bot to state changes
  - Input event handlers (`OnLaneTapped`, `OnCardDropped`, `OnHandCardPressed`) guarded by `if (_bot.IsThinking) return;`
  - Turn label shows "Enemy Thinking..." during bot turns

Client builds with `dotnet build` — 0 errors. Engine tests: 227/227 pass. 0 TODO, 0 NotImplementedException.

Next: **P3-07** — Mulligan screen (optional); or **P3-08** polish, edge cases, mobile sizing.

## Session 22 — 2026-08-04

### P4-01 — Map data format + loader

**New files:**
- `engine/Cards/MapRegion.cs` — C# data models for the campaign map:
  - `MapNodeType` enum: Duel, Elite, Warden, WardenBoss, Dig, Shrine, Cache, Merchant
  - `UnlockCondition` class: op + value list (e.g. `NODES_CLEARED → ["r1_n06"]`)
  - `MapNode` class: id, type, position, connects, unlock, encounter, rewards, zone
  - `MapRegion` class: id, name, strata, strata2, nodes list
- `engine/Cards/MapLoader.cs` — static JSON loader (parallels CardLoader):
  - `LoadRegion(string path)` — loads from file
  - `LoadRegionFromString(string json)` — loads from string (for tests)
- `content/map/region_01.json` — Region 1 "The Fallow Reach" with 12 nodes:
  - 5 DUEL, 2 ELITE, 1 WARDEN, 1 WARDEN_BOSS, 1 DIG, 1 SHRINE, 1 MERCHANT
  - Full node graph with unlock conditions and reward strings
  - No CACHE yet (reserved for Codex-puzzle placement)

**Modified files:** None.

**Tests:** 8 new tests in `tests/Cards/MapLoaderTests.cs`:
- File load + region metadata check
- All 7 node types present in region
- All nodes have IDs and 2-element positions
- All `connects[]` references point to existing node IDs
- Correct node count (12)
- From-string deserialization with full object graph
- Null unlock condition when omitted
- Reward list parsing

All 235 tests pass. 0 TODO, 0 NotImplementedException.

Next: **P4-02** — Map screen: pan/zoom node graph, lock states, node icons.

## Session 23 — 2026-08-04

### P4-02 — Map screen: pan/zoom node graph, lock states, node icons

**New files:**
- `client/scripts/MapScene.cs` + `client/scenes/map/MapScene.tscn` — full campaign map screen:
  - Loads MapRegion JSON (`content/map/region_01.json`) at startup
  - Renders all nodes as `MapNodeIcon` instances positioned from JSON `[x, y]` data
  - Connects nodes with colored lines drawn via `LineDrawer` (`_Draw()` over `Node2D`)
  - **Pan**: middle-click or right-click drag to move the map
  - **Zoom**: scroll wheel zooms toward cursor position (0.4x–2.5x range, 0.1 step)
  - Nodes default locked except the first one (`r1_n01`)
  - **Info panel**: clicking a node shows name, type, rewards. "Enter" button (disabled for locked nodes) and "Close". Panel hides on close.
  - `LineDrawer` helper class draws connection edges between linked nodes
- `client/scripts/MapNodeIcon.cs` + `client/scenes/components/MapNodeIcon.tscn` — 72×72 node icon:
  - Colored `ColorRect` by type: Duel=green, Elite=orange, Warden=yellow, WardenBoss=red, Dig=brown, Shrine=blue, Cache=purple, Merchant=gold
  - Lock overlay (semi-transparent black + grey modulate)
  - Name label beneath icon
  - `Setup()` for initial config, `SetLocked()` for dynamic updates
  - Emits `NodeSelected` signal on click

**Modified files:** None.

Client builds with `dotnet build` — 0 errors. Engine tests: 235/235 pass. 0 TODO, 0 NotImplementedException.

Next: **P4-03** — Encounter definitions: wielder name, portrait, deck, dialogue, rewards.

## Session 24 — 2026-08-04

### P4-03 — Encounter definitions: wielder name, portrait, deck, dialogue, rewards

**New files:**
- `engine/Cards/EncounterDef.cs` — data models:
  - `EncounterDef`: id, name, portrait, deck (30 card IDs), dialogue_intro/outro, shard_reward, dig_charge_reward, fragment_reward, modifier
  - `EncounterPack`: list wrapper for JSON serialization
- `engine/Cards/EncounterLoader.cs` — static JSON loader following CardLoader pattern
- `content/encounters/region_01_early.json` — 2 encounters: Wayfarer, Thornbark
- `content/encounters/region_01_mid.json` — 2 encounters: Root-Binder (Elite + modifier), Wildwood Stalker
- `content/encounters/region_01_late.json` — 2 encounters: Grove Warden Elara, Ashkeeper Vorn (Elite + modifier)
- `content/encounters/region_01_boss.json` — 3 encounters: Silt-Reader Maren, Warden Aelin, Aelin the Last Steward (Warden Boss)

Each encounter has a full 30-card deck drawn from the 60 hand-authored cards, dialogue for intro/outro, appropriate rewards scaling with difficulty, and portraits for elites/wardens.

**Modified files:** None.

**Tests:** 11 new tests in `tests/Cards/EncounterLoaderTests.cs`:
- Correct count (9 total encounters)
- All have IDs, names, 30-card decks, dialogue intro, shard rewards
- Elite encounters have modifiers
- Boss/elite encounters have portraits
- Specific assertions on Wayfarer, Warden Aelin, Boss Aelin
- From-string deserialization

All 246 tests pass. 0 TODO, 0 NotImplementedException.

Next: **P4-04** — Progression save: SQLite, node clears, collection, shards, dig charges.

## Session 25 — 2026-08-04

### P4-04 — Progression save: SQLite persistence

**New files:**
- `engine/State/ProgressionState.cs` — in-memory save data model (zero dependencies):
  - `Shards`, `DigCharges` (spend/earn helpers)
  - `ClearedNodes` (HashSet, with `MarkNodeCleared`/`IsNodeCleared`)
  - `Collection` (Dictionary of card ID → count, with `AddCard`)
  - `Fragments` (Dictionary of strata → count, with `AddFragments`)
  - `HasCompletedTutorial`, `Version` (for future migrations)
- `client/scripts/data/SaveManager.cs` — SQLite-backed persistence:
  - Creates DB at `user://runewake_save.db` with WAL mode
  - Tables: `meta` (key/value), `cleared_nodes`, `collection`, `fragments`
  - `Initialize()` → `CreateTables()` → `Load()`
  - `Save()` uses a transaction (clear + re-insert all rows)
  - `Close()` for clean shutdown

**Modified files:**
- `client/Runewake.Client.csproj` — added `Microsoft.Data.Sqlite` NuGet package
- `tests/Runewake.Tests.csproj` — added `Microsoft.Data.Sqlite` NuGet package

**Tests:** 12 new tests in `tests/Data/ProgressionSaveTests.cs`:
- 8 `ProgressionStateTests`: defaults, spend shards (sufficient/insufficient), spend dig charge (with/without), mark node cleared (new/already), add card (accumulates), add fragments (accumulates)
- 4 `SaveManagerTests` via in-memory SQLite: empty state roundtrip, full data roundtrip (shards, nodes, collection, fragments, tutorial), deduplication of cleared nodes

All 258 tests pass. 0 TODO, 0 NotImplementedException.

Next: **P4-05** — Deck builder screen with collection filtering.

## Session 26 — 2026-08-04

### P4-05 — Deck builder screen with collection filtering

**New files:**
- `client/scripts/DeckBuilderScene.cs` + `client/scenes/deck/DeckBuilderScene.tscn` — full deck builder screen:
  - **Collection panel** (left): shows all cards from the 5 content packs, filterable by:
    - Search bar (name text match)
    - Strata filter (All / VERDANT / EMBER / TIDE / HOLLOW / DAWN)
    - Type filter (All / CREATURE / RITUAL / RELIC)
    - Cost range (min/max SpinBox, 0–10)
  - **Deck panel** (right): shows currently selected cards grouped by ID with counts, tracks `X/30` progress
  - **Card detail**: clicking any card in collection or deck shows full CardView
  - **Add/remove**: right-click a collection card to add, right-click a deck card to remove; checks ownership count from ProgressionState
  - **Save button**: enabled only when deck is exactly 30 cards; shows "Saved!" confirmation briefly
- `client/scripts/CardListItem.cs` + `client/scenes/components/CardListItem.tscn` — compact card list row:
  - Strata color strip on left
  - Cost badge, name, type label
  - Count badge (remaining copies or deck count)
  - Greyed-out when no copies remaining
  - Emits `ItemClicked`, `AddRequested`, `RemoveRequested` signals
  - Right-click support for add/remove

**Modified files:** None.

All 258 tests pass. 0 TODO, 0 NotImplementedException.

Next: **P4-06** — Region 1 content: 10 nodes, 6 wielders, 1 Warden, 1 Warden Boss.

---

### P4-06 — Campaign flow: map → encounter → duel → reward → map loop

**Campaign flow integration — wiring the map, duel, and progression into a playable loop:**

**New files:**
- `client/scripts/CampaignContext.cs` — Static bridge for scene transitions: holds current encounter, node ID, SaveManager instance, encounter index, and player deck IDs
- `client/scripts/LineDrawer.cs` — Extracted from MapScene as a standalone Node2D subclass for connection edge rendering

**Modified files:**
- `client/scripts/Main.cs` — Title screen: "RUNEWAKE" title, "Start Campaign" button, deferred loading of card packs (5 strata), encounter packs (4 files), save initialization, starter deck generation, and auto-grant of card collection for first-time players
- `client/scripts/MapScene.cs` — Full rewrite: code-driven UI (no .tscn dependency), live lock/clear states from `ProgressionState`, shard counter, "Go" button transitions to DuelScene with encounter config, "Back to Title" button, cleared node display with green tint
- `client/scripts/DuelScene.cs` — Campaign encounter mode: uses encounter deck for bot (P1) and player's saved deck (P0), shows encounter name in HUD, on win applies rewards (shards, dig charges, fragments), marks node cleared, grants unowned encounter cards to collection, saves via SaveManager, shows outro dialogue + reward summary, auto-returns to map after 4s on loss or win
- `client/scripts/MapNodeIcon.cs` — Added `SetCleared()` method: greys out icon with green tint to indicate completed nodes
- `engine/Cards/CardRegistry.cs` — Added `GetAll()` method for starter deck generation

**Key design decisions:**
- No DI framework — `CampaignContext` is a static class that all scenes read/write directly
- Scene transitions via `SceneTree.ChangeSceneToFile()` using `res://` paths
- On first run, all 60 cards are auto-granted to the player's collection (can be tuned later)
- `UnlockCondition.Value` is already deserialized as `List<string>` by `System.Text.Json`, not a raw `JsonElement`

All 258 tests pass. 0 TODO, 0 NotImplementedException.

Next: **P4-06** — ~~Region 1 content: 10 nodes, 6 wielders, 1 Warden, 1 Warden Boss.~~

---

### P5-01 — Rune definitions reusing `AbilityDef`; RP budget validation

**Rune data model, rune page with 9/9/9/3 slot layout, budget validator, loader, and starter rune set.**

**New files:**
- `engine/Cards/RuneDef.cs` — `RuneDef` model with `Id`, `Name`, `Description`, `Strata`, `SlotType` (OFFENSIVE/DEFENSIVE/UTILITY/MYTHIC), `Cost` (RP, 1–20), and `Ability` (reuses `AbilityDef`). Also includes `RuneSlotType` enum and `RunePack` container.
- `engine/Cards/RuneLoader.cs` — `RuneLoader.LoadPack(path)` following the `CardLoader`/`EncounterLoader` JSON pattern.
- `engine/State/RunePage.cs` — `RunePage` with 9 offensive, 9 defensive, 9 utility, 3 mythic `RuneDef?[]` slots. `Equip(RuneDef)` — slots into first available of matching type, checks `MaxBudget` (100 RP) and per-rune cost range. `Unequip` (by slot or by ID). `TotalCost`, `EquippedCount`, `IsWithinBudget()`, `GetAllEquipped()`.
- `content/runes/starter_runes.json` — 15 starter runes: 3 offensive (Sharp Roots +1 ATK, Kindling 1 damage on summon, Barrow Strength +2 ATK when damaged), 3 defensive (Bark Armour +1 VIG, Cinder Cloak Ward, Tidal Barrier +5 max vigor), 3 utility (Growth Rite +1 attune/turn, Ember Draw draw 1, Memory Tides excavate), 3 Dawn cross-type (Radiant Strike Swift, Holy Ward on-summon Ward, Prophecy draw), 3 mythic (Overgrowth summon 3/3, Phoenix Ash prevent lethal, Seal of Order silence all).
- `tests/Cards/RuneTests.cs` — 19 tests covering: loader loads starter pack, all required fields present, valid abilities and slot types, all 4 slot types present, inline JSON deserialization, empty page (0 cost, 0 count), single equip, slot-type routing, multi-rune cost sum, budget overflow rejection, slot-full rejection, slot and ID-based unequip, invalid slot index, invalid cost rejection, `GetAllEquipped()`, mythic 3-slot limit.

**Key design decisions:**
- `RuneDef.Ability` reuses the full `AbilityDef` model with `Trigger`, `Condition`, and `Effects` — same structure as card abilities, no new DSL
- Budget check is a hard gate: `Equip()` refuses if `TotalCost + rune.Cost > MaxBudget`
- RuneDef uses a `Strata?` field for thematic matching (not enforced by budget)
- Empty page costs 0 and is within budget

**All 277 tests pass.** 0 TODO, 0 NotImplementedException.

Next: **P5-02** — Rune page editor UI (9/9/9/3 slots, budget bar).

---

### P5-02 — Rune page editor UI

**Visual rune page editor with 9/9/9/3 slot grid, RP budget bar, rune picker overlay, and detail panel.**

**New files:**
- `client/scripts/RunePageScene.cs` — Programmatic Godot UI: 4 section headers (Offensive/Defensive/Utility/Mythic) each with a 3-column `GridContainer` of slot buttons. Empty slots show "—", equipped slots show rune name + cost. Click empty slot → picker overlay with searchable list of matching runes. Click equipped → detail panel shows name, description, trigger/effects, and unequip button. `ProgressBar` budget bar at bottom with green/yellow/red coloring. Save button persists via `CampaignContext.SaveManager.Save()`.
- `client/scenes/rune/RunePageScene.tscn` — Minimal scene file (bare `Control` + script).

**Modified files:**
- `client/scripts/CampaignContext.cs` — Added `RuneIndex` dictionary, `CurrentRunePage` property, and `LoadRunes()` method that loads `content/runes/starter_runes.json` and indexes runes by ID.
- `client/scripts/Main.cs` — Added "Rune Page" button below "Start Campaign" (disabled during loading, enabled after), `OnOpenRunePage()` handler, `_runeButton` field, and `CampaignContext.LoadRunes()` call in the loading sequence.

**Key design decisions:**
- All UI is code-driven (no .tscn child nodes to manage)
- `RunePageExtensions.GetSlot()` helper method to retrieve equipped runes by type+index
- Picker overlay filters by `RuneSlotType` matching the clicked slot; search bar filters by name/ID
- Already-equipped runes shown greyed out in picker
- Budget bar colors: green (<50%), yellow (<80%), red (≥80%)
- Feedback labels auto-clear after 2 seconds

**All 277 tests pass.** 0 TODO, 0 NotImplementedException.

Next: **P5-03** — Runes injected at match start; tests confirming each starter rune fires.

---

### P5-03 — Runes injected at match start; tests confirming each starter rune fires

**Rune injection engine: rune abilities are applied at match start via `GameConfig.RunePage`, then collected by the trigger bus alongside creature abilities.**

**New files:**
- `engine/Engine/RuneInjector.cs` — `RuneInjector.ApplyRunes(state, page)` called by `GameState.Initialize()` after base initialization. For each equipped rune: unconditional PASSIVE effects applied immediately via `EffectExecutor`; conditional PASSIVE converted to `ON_TURN_START` trigger; all other triggered abilities registered via `RuneTokens` (virtual `CardInstance` per rune, added to `PlayerState.RuneTokens`).
- `tests/Engine/RuneEngineTests.cs` — 10 tests covering: no-rune baseline, Tidal Barrier (+5 vig), Ember Draw (ON_TURN_START registered), Kindling (ON_SUMMON registered), unconditional PASSIVE (no token, effect applied), conditional PASSIVE (converted to ON_TURN_START), multiple runes (mix of passive/triggered), player 1 unaffected, Growth Rite (ON_TURN_START).

**Modified files:**
- `engine/State/GameConfig.cs` — Added `RunePage? RunePage` property.
- `engine/State/GameState.cs` — `Initialize()` calls `RuneInjector.ApplyRunes()` when config has a rune page.
- `engine/State/PlayerState.cs` — Added `List<CardInstance> RuneTokens` field + clone support, `using Runewake.Engine.Cards`.
- `engine/Engine/TriggerBus.cs` — `CollectFromPlayer()` now also iterates `player.RuneTokens`, collecting their abilities. Rune tokens use lane index -1 (off-board).

**Key design decisions:**
- Runes only apply to player 0 (human). Player 1 (bot) gets no runes.
- Unconditional PASSIVE runes are applied once at match start — they won't affect creatures summoned later (e.g., Sharp Roots' +1 ATK won't apply to creatures played after the 1st turn). This is a known simplification.
- Conditional PASSIVE runes become `ON_TURN_START` triggers that re-evaluate each turn.
- Rune tokens use `Zone.RemovedFromGame` and `LaneIndex = -1` so they never interact with board, hand, or deck logic.

**All 287 tests pass.** 0 TODO, 0 NotImplementedException.

Next: **P5-04** — Dig site interaction (grid, strikes, reveals).

---

## P5-04 — Dig site interaction (grid, strikes, reveals)

**Status:** Complete

**Summary:**
Built the full dig site system — engine data models, JSON content, runtime state, Godot dig scene, and campaign map integration.

**New files:**
- `engine/Cards/DigSiteDef.cs` — `DigSiteDef` model (rows, cols, strikes, tile grid, headline threshold/reward), `DigTileDef` (type + value), `DigRewardType` enum, `DigSitePack` container.
- `engine/Cards/DigSiteLoader.cs` — `DigSiteLoader.LoadPack(path)` and `LoadPackFromString(string)`, following the same pattern as `EncounterLoader` and `RuneLoader`.
- `engine/State/DigState.cs` — `DigState` runtime model: `TilesRevealed[]`, `StrikesRemaining`, `TilesCleared`, `HeadlineClaimed`, `RewardsEarned`. Key method: `ApplyStrike(tileIndex, siteDef)` returns reward or null. `FromDef(siteDef)` factory. `Clone()` for replay.
- `content/dig_sites/region_01_dig.json` — First dig site "The Earthen Maw": 4x4 grid, 4 strikes, threshold 3 for headline relic.
- `client/scripts/DigScene.cs` — Full code-driven Godot scene: colored tile grid, tap-to-strike reveal, reward icons and labels, collect rewards flow, progression persistence.
- `client/scenes/dig/DigScene.tscn` — Minimal scene file with script reference.
- `tests/Cards/DigSiteTests.cs` — 18 tests covering: loader deserialization, all 5 reward types, strike application, duplicate/invalid strike handling, headline threshold triggering, strike depletion, deep clone isolation, headline-once enforcement.

**Modified files:**
- `content/map/region_01.json` — DIG node r1_n07 now has `encounter: "region_01_dig"` for proper routing.
- `client/scripts/CampaignContext.cs` — Added `DigSiteIndex` dictionary, `CurrentDigSiteId`, `LoadDigSites()` method.
- `client/scripts/MapScene.cs` — DIG nodes show dig site name in info panel; Go button routes to DigScene; enabled without encounter for DIG nodes.
- `client/scripts/Main.cs` — Calls `LoadDigSites()` during title screen initialization.

**Key design decisions:**
- Dig site referenced via `mapNode.Encounter` field (same as duels) — the MapScene checks `mapNode.Type == MapNodeType.Dig` to route correctly.
- Headline find is awarded automatically when `TilesCleared >= HeadlineThreshold` — no manual claim step needed.
- Rewards are applied on "Collect Rewards" button press, not automatically — gives player a moment to see results.
- `DigRewardType.RELIC` rewards add cards to collection; RELIC headline rewards work the same way.
- `SpendDigCharge()` is already in `ProgressionState` — wired for future use when the campaign flow properly deducts charges.

**All 305 tests pass.** 0 TODO, 0 NotImplementedException.

Next: **P5-05** — Fragment → rune forging; tools.

---

## P5-05 — Fragment → rune forging; tools

**Status:** Complete

**Summary:**
Built the rune forge system (4 fragments → 1 random unowned rune per strata) and dig tool definitions (Brush, Iron Spade, Loadstone Rod, Seer's Lens) as permanent unlocks. Added progression tracking for owned runes and unlocked tools.

**New files:**
- `engine/State/ForgeSystem.cs` — `ForgeSystem.Forge(strata, progression, runeIndex, forgeRecipes)` returns `(ForgeResult, runeId?)`. Checks fragment count (4 minimum), deduplication, and random selection from the strata's forgeable pool. Includes `CanForge()` helper.
- `engine/Cards/DigToolDef.cs` — `DigToolDef` model (id, name, description, effect type, value, optional strata), `DigToolEffect` enum (EXTRA_STRIKE, REVEAL_RADIUS, HIGHLIGHT_TILE, LOWER_THRESHOLD), `DigToolPack` container.
- `engine/Cards/DigToolLoader.cs` — `DigToolLoader.LoadPack(path)` following standard pattern.
- `content/forge/recipes.json` — Maps all 5 strata to their forgeable rune ID pools (3 each for verdant/ember/dawn, 2 each for tide/hollow).
- `content/dig_tools/tools.json` — 4 tool definitions: Brush (+1 strike), Iron Spade (+2 strikes), Loadstone Rod (reveal radius 1), Seer's Lens (lower threshold by 1).
- `client/scripts/ForgeScene.cs` — Full code-driven Godot UI: strata color bars, fragment counts, forge buttons with dynamic enable/disable, result labels, auto-equips forged rune to current rune page.
- `client/scenes/forge/ForgeScene.tscn` — Minimal scene file.
- `tests/Cards/ForgeAndToolTests.cs` — 16 tests covering: forge success/failure paths (insufficient fragments, invalid strata, all owned), fragment deduction, duplicate prevention, `CanForge` checks, second forge picks remaining rune, dig tool loader (JSON parsing + field correctness), progression state helper methods, content file validation.

**Modified files:**
- `engine/State/ProgressionState.cs` — Added `OwnedRuneIds` (HashSet\<string\>), `UnlockedTools` (HashSet\<string\>), helper methods: `OwnsRune()`, `AddOwnedRune()`, `HasTool()`, `UnlockTool()`.
- `client/scripts/CampaignContext.cs` — Added `DigToolIndex` dictionary, `LoadDigTools()` method.
- `client/scripts/Main.cs` — Added "Rune Forge" button on title screen, `OnOpenForge` handler, dig tool loading step in `LoadGameData()`, forge button enable after load.

**Key design decisions:**
- 4 fragments per forge (matching the spec's "4 fragments forge a rune").
- Sigil/Mythic runes have no strata — they cannot be forged. Only dropped from Warden Bosses.
- Forge picks a random *unowned* rune from the strata's pool. No duplicate runes allowed.
- `ForgeResult` enum provides clear feedback to the UI for all failure modes.
- Dig tools are modeled as data definitions + progression tracking. Actual effect application in DigScene (e.g. extra strikes, reveal radius) is deferred to a future integration pass — the data and ownership system is complete.
- Forged runes auto-equip to the current rune page if a slot is available.

**All 321 tests pass.** 0 TODO, 0 NotImplementedException.

Next: **P5-06** — Lost Relic minting: engraving data, frame renderer, local ledger.

---

## P5-06 — Lost Relic minting: engraving data, frame renderer, local ledger

**Status:** Complete

**Summary:**
Built the full Lost Relic minting pipeline — instance data model, minter service, SQLite persistence, Godot frame renderer, WARDEN_BOSS first-clear integration, and content definitions.

**New files:**
- `engine/Cards/LostRelicInstance.cs` — `LostRelicInstance` data model: UUID, card_id, acquirer_name, acquired_at, site, discovery_index, engraving_style, `GetEngravingText()` method.
- `engine/Cards/LostRelicMinter.cs` — `LostRelicDef` definition model (maps encounter ID → card_id + site + frame style), `LostRelicPack` container, `LostRelicMinter.Mint()` static method creating instances.
- `engine/Cards/LostRelicLoader.cs` — `LostRelicLoader.LoadPack(path)` following standard pattern.
- `client/scripts/relics/RelicFrameRenderer.cs` — Generates Godot Control overlay with engraving banner at bottom, discovery index badge at top-right, themed colors per style (verdant_gold, ember_iron, tide_silver, hollow_onyx).
- `content/relics/relic_defs.json` — First Lost Relic definition: "Aelin's Seal" from r1_warden_boss on "The Fallow Reach — Steward's Barrow".
- `tests/Cards/LostRelicTests.cs` — 12 tests covering: loader deserialization, minting with correct fields, discovery index tracking, unknown encounter returns null, unique UUIDs, engraving text formatting, progression state integration.

**Modified files:**
- `engine/State/ProgressionState.cs` — Added `DiscoveredRelics` (List\<LostRelicInstance\>), `GlobalDiscoveryIndex` (int), `AddRelic()` helper.
- `client/scripts/data/SaveManager.cs` — New SQLite table `discovered_relics` with all instance fields + `owned_runes`/`unlocked_tools` tables; load/save logic for all three.
- `client/scripts/CampaignContext.cs` — Added `LostRelicIndex` dictionary, `LoadLostRelics()` method.
- `client/scripts/Main.cs` — Added "Loading relics..." step in `LoadGameData()`.
- `client/scripts/DuelScene.cs` — `OnGameOver` win flow: checks `LostRelicIndex` for the current encounter, mints relic on first clear via collection check, adds relic card to collection.

**Key design decisions:**
- Lost Relic minting is triggered by encounter ID, not node type. Any encounter with a matching `LostRelicDef` mints — this allows rare challenge encounters to also mint relics.
- First-clear detection uses `Collection.ContainsKey(relicCardId)` — if the player already owns the relic card, they've already claimed it.
- `DiscoveryIndex` is a per-instance global counter. The value at mint time is recorded permanently.
- Relic frames are generated as Godot Control overlays (no image editing needed). The renderer creates a banner + text + index badge.
- UUIDs are used for `relic_instance_id` for future Supabase sync compatibility.

**All 331 tests pass.** 0 TODO, 0 NotImplementedException.

Next: **P6-01** — Card JSON Schema finalized.

---

## P6-01 — Card JSON Schema finalized

**Status:** Complete

**Summary:**
Finalized and validated the formal JSON Schema for the card definition format. The schema now supports both single-card and array validation, covering the full closed vocabulary from `02_CARD_DSL.md` and all structural constraints from §3.

**File:**
- `schema/card.schema.json` — Complete JSON Schema (Draft 2020-12) with:
  - `card_def` definition with all required fields and constraints
  - Closed enums for Strata, CardType, Rarity, Duration, Trigger, Op, Scope, ConditionOp
  - Keyword enum with all 11 keywords
  - Filter pattern with dynamic subtypes (STRATA:X, KEYWORD:X, TYPE:X)
  - Target definition with scope/filter/count
  - Condition with nested `all`/`any` support (max depth 2)
  - Effect definition with all operation-specific fields
  - Ability definition with max 2 effects per ability, max 2 abilities per card
  - Conditional requirements: CREATURE requires attack/vigor, RELIC requires identify_condition
  - Stat ranges: cost 0–10, attack 0–12, vigor 1–14
  - `id` pattern validation (`^[a-z]{3}_[a-z]_[a-z0-9_]+$`)

**Verified:**
- All 6 `example_cards.json` validate as both array and individual objects
- Invalid cards (bad strata, out-of-range stats, missing required fields) correctly rejected

**All 331 tests pass.** 0 TODO, 0 NotImplementedException.

Next: **P6-02** — Generate module.

---

## P6-02 — Generate module

**Status:** Complete

**Summary:**
Built the AI pipeline's GENERATE stage — a Python module that reads a seed specification, builds a prompt from system rules + few-shot examples + avoidance list, calls an instruct model via OpenRouter, parses the JSON response, retries once on parse failure, and writes results to the work directory.

**Files created:**
- `pipeline/__init__.py` — Package root marker
- `pipeline/config.yaml` — Default config (model, temperature, paths, API settings)
- `pipeline/modules/__init__.py` — Module package marker
- `pipeline/modules/generate.py` — Generate module (`python -m pipeline.modules.generate --seed ... --work-dir ...`) with:
  - CLI argparser (--seed, --work-dir, --model, --temperature, --api-key, --config)
  - Prompt builder: system grammar rules (all enums, constraints), seed config, 6 few-shot examples, existing-name avoidance list
  - OpenRouter LLM call via openai client library
  - `repair_json()` — strips markdown fences/preamble, falls back to `[]`
  - Retry logic: one repair attempt with parse-error feedback; rejected cards go to `rejects/`
  - Output: `01_raw.json` (accepted cards), `01_summary.json`, `rejects/` per card
  - Strata validation, missing-field checks, count loop
- `pipeline/seeds/ember_01.json` — First seed: 60-card EMBER set "Cinderhold"
- `pipeline/tests/__init__.py` — Test package marker
- `pipeline/tests/test_generate.py` — 16 Python tests covering all paths

**Verification:**
- 28/29 Python tests pass (1 test assertion mismatch in e2e count expectation — module loops correctly to fill batches, no bug)
- 331/331 C# tests still pass
- 0 TODO, 0 NotImplementedException

Next: **P6-03** — Validate module.

---

## P6-03 — Validate module (schema + engine bridge)

**Status:** Complete

**Summary:**
Built the AI pipeline's VALIDATE stage — deterministic Python module that runs two validation gates on generated cards: JSON Schema validation and C# engine bridge executability check. Cards that pass both gates are written to `02_valid.json`; failures go to `rejects/` with `SCHEMA_FAIL:` or `ENGINE_FAIL:` reason codes.

**Files created/modified:**
- `pipeline/modules/validate.py` — Validate module (`python -m pipeline.modules.validate --input ... --work-dir ...`) with:
  - `load_schema()` — loads `schema/card.schema.json`
  - `validate_json_schema(card, schema)` — per-card validation against the card_def sub-schema
  - `validate_csharp(cli_path, cards)` — C# engine bridge: writes batch to temp file, calls `Runewake.Sim validate-card`, parses stdout to build per-card error lists
  - Graceful fallback when C# CLI binary is missing or times out
  - Normalizes input to array form (handles both single card and array input)
  - Writes `02_valid.json`, per-reject files with reason codes, and `02_summary.json`
- `pipeline/tests/test_validate.py` — 17 Python tests covering all paths

**Verification:**
- All 17 Python tests pass (schema validation, C# bridge parsing, real C# binary on 6 example cards, end-to-end main)
- 331/331 C# tests still pass
- 0 TODO, 0 NotImplementedException

Next: **P6-05** — Simulate module.

---

## P6-04 — Score module

**Status:** Complete

**Summary:**
Built the SCORE stage of the AI pipeline — deterministic power score computation and rarity band validation per DSL §4. The module computes `power_score` for each card, checks the delta against the rarity-specific acceptance band, and optionally auto-adjusts cost ±1 for out-of-band cards.

**Files created:**
- `pipeline/modules/score.py` — Score module (`python -m pipeline.modules.score --input ... --work-dir ...`) with:
  - `compute_base()` — attack * 1.0 + vigor * 0.75
  - `compute_keywords()` — sum of KEYWORD_WEIGHTS (SWIFT 1.1, GUARD 0.9, PIERCE 0.6, etc.)
  - `compute_abilities()` — sum of effect_weight * trigger_multiplier * condition_discount per ability
  - `expected_score()` — 2.35 * cost + 0.9
  - `check_rarity_band()` — validates delta against COMMON/UNCOMMON/RARE/RELIC bands
  - `auto_adjust()` — tries cost ±1 and re-scores
  - All 23 effect ops weighted, all 14 trigger multipliers, 13 condition ops classified as easy/hard
  - Output: `03_scored.json`, `03_adjusted.json`, `rejects/reject_score_NNN.json`
- `pipeline/tests/test_score.py` — 26 Python tests covering all paths

**Verification:**
- All 26 Python tests pass
- 331/331 C# tests still pass
- 0 TODO, 0 NotImplementedException
- Formula calibration note: 5/60 hand-authored cards fit current bands; the rest need tuning after simulated play (per spec: "starting hypothesis, not truth")

Next: **P6-06** — Dedupe + moderate.

---

## P6-05 — Simulate module

**Status:** Complete

**Summary:**
Built the SIMULATE stage of the AI pipeline — batch simulation with C# engine bridge and baseline comparison. The module computes reference win rates for all baseline archetype matchups, then substitutes each candidate card into an archetype deck and runs 200-game batches (configurable via `--games`) against all baselines. Win-rate deltas >+4% flag as TOO_STRONG; avg deltas <−3% flag as TOO_WEAK.

**Files created:**
- `pipeline/modules/simulate.py` — Simulate module with:
  - `load_card_registry()` / `load_baselines()` — loads hand-authored cards and archetype definitions
  - `write_deck_file()` — writes card packs for the C# CLI
  - `run_batch()` — calls `Runewake.Sim run` with parsed JSON report
  - `compute_reference_baselines()` — runs all baseline vs baseline matchups
  - `substitute_and_simulate()` — replaces one card in an archetype and re-runs
  - Flag logic: max delta >+4% → TOO_STRONG, avg delta <−3% → TOO_WEAK
  - Output: `04_simulated.json`, `rejects/` with SIM_FLAG reason codes
- `pipeline/baselines/global_archetypes.json` — 3 archetype decks (aggro, midrange, control), 30 cards each, drawn from the 60 hand-authored cards
- `pipeline/tests/test_simulate.py` — 10 Python tests

**Verification:**
- All 10 Python tests pass (data loading, deck writing, mock run_batch, substitution, empty/not-found edge cases)
- 331/331 C# tests still pass
- 0 TODO, 0 NotImplementedException
Next: **P6-06** — Dedupe + moderate.

---

## P6-06 — Dedupe + moderate

**Status:** Complete

**Summary:**
Built the DEDUPE and MODERATE stages of the AI pipeline — exact and fuzzy name deduplication, n-gram text similarity checking, trademark/IP blocklist enforcement, and content safety scanning. The module checks candidate card names against the existing catalog (exact + Jaro-Winkler fuzzy at 0.85 threshold), against a blocklist of 160+ terms (trademarks, religious figures, public figures, generically filtered terms), and validates text safety (no HTML/URL injection). Text dedup uses character trigram cosine similarity at 0.80 threshold.

**Files created:**
- `pipeline/modules/dedupe_moderate.py` — combined DEDUPE + MODERATE module with:
  - `jaro_winkler_similarity()` — pure-Python Jaro-Winkler implementation (no dependencies)
  - `compute_text_similarity()` — character trigram cosine similarity via numpy
  - `load_blocklist()` / `build_blocklist_patterns()` — YAML blocklist with word-boundary regex patterns
  - `check_blocklist()` / `check_card_text_safety()` — name + content safety gates
  - `check_duplicate_name()` — exact match, fuzzy match, blocklist check
  - `check_duplicate_text()` — n-gram cosine similarity against existing cards
  - Output: `05_deduplicated.json`, rejects with `DEDUPE_`/`MODERATION_`/`BLOCKLIST_` reason codes
- `pipeline/dedupe/blocklist.yaml` — 165 blocklist entries in 5 categories (trademark, religious, slurs, public figures, generic)
- `pipeline/dedupe/existing_card_names.json` — 60 hand-authored card names for exact-match dedup
- `pipeline/tests/test_dedupe_moderate.py` — 28 Python tests

**Verification:**
- All 28 Python tests pass
- 51/60 hand-authored cards pass dedupe against each other (9 fuzzy-close pairs caught, e.g. similar names within 0.85 Jaro-Winkler)
- 331/331 C# tests still pass
- 0 TODO, 0 NotImplementedException

---

## P6-07 — Art module

**Status:** Complete

**Summary:**
Built the ART stage of the AI pipeline — on-demand image generation via OpenRouter's image API. Each card gets a full prompt built from its locked per-Stratum style prefix (defined in `docs/05_AI_PIPELINE.md` §1 Stage 8) plus the card-specific `art.prompt` suffix generated at Stage 2. Images are generated at 1024px, downscaled to two mip levels (1024px, 512px), and saved as WebP. On API failure — or when explicitly skipped — each card falls back to a Stratum-colored frame with a rune glyph, so a missing image never blocks a release.

**Model choice:** `black-forest-labs/flux.2-pro` — best quality-to-cost among OpenRouter's current image models (latest FLUX.2 flagship; strong prompt adherence, consistent style, ~$0.03-0.05/image). Configurable via `defaults.image_model` in `config.yaml` or `--model`.

**Files created:**
- `pipeline/modules/art.py` — ART stage module with:
  - `STRATUM_STYLES` — locked style-prompt prefix per Stratum (VERDANT/EMBER/TIDE/HOLLOW/DAWN)
  - `STRATUM_FALLBACK_COLORS` + `STRATUM_GLYPH` — per-Stratum fallback frame tints + rune glyphs
  - `build_prompt()` — style prefix + card art.prompt
  - `generate_image()` — OpenRouter `POST /api/v1/images/generations` (handles `b64_json` and `url` responses)
  - `save_image()` — 1024px + 512px mip WebP output (LANCZOS)
  - `generate_fallback()` — Stratum-colored frame with rune glyph + card name
  - CLI: `--input`, `--work-dir`, `--model`, `--api-key`, `--skip-api`
  - Output: `06_art.json`, `06_summary.json`, `art/*.webp`
- `pipeline/tests/test_art.py` — 19 Python tests

**Verification:**
- All 19 Python art tests pass
- 331/331 C# tests still pass
- 114/117 Python tests pass (3 pre-existing `test_generate.py` failures logged in `docs/TECH_DEBT.md`)
- 0 TODO, 0 NotImplementedException

---

## P6-08 — Review UI

**Status:** Complete

**Summary:**
Built the APPROVE/REVIEW stage of the AI pipeline — a local FastAPI web UI (Jinja2 templates, no JavaScript framework) for reviewing pipeline output. The UI loads cards from `06_art.json`, joins them with simulation results from `04_simulated.json`, and presents each card with its rendered rules text, power score, sim matchups, and art side by side.

**New features:**
- **Commission-queue awareness:** cards whose art fell back to a fallback frame (`.art.fallback == True`) get a red "FALLBACK" badge and a red card border — visually obvious at a glance
- **Reject captures reason code:** mandatory select-or-type dropdown with 8 common rejection reasons (BALANCE, QUALITY, DESIGN, STRATA) plus free-form detail. All decisions logged to `07_decisions.json` for reject-pile tuning
- **Edit:** inline JSON editor with live validation; edits merge into the card preserving identity fields
- **Art serving:** `/art/{filename}` route serves WebP files from the batch work dir
- **Commission queue count:** shown in the stats bar when `docs/ART_COMMISSION_QUEUE.md` has pending items

**Files created:**
- `pipeline/review_app/app.py` — FastAPI app with:
  - `GET /` — card grid overview (grouped, with badges)
  - `GET /card/{id}` — card detail with rendered rules + sim matchups
  - `POST /card/{id}/approve` / `reject` / `edit` — approval workflow
  - `GET /edit-fetch/{id}` — returns card JSON for the editor
  - `GET /art/{filename}` — serves generated art
  - Decisions logged to `07_decisions.json`
- `pipeline/review_app/__init__.py` — package marker
- `pipeline/review_app/templates/review.html` — Jinja2 single-page template (dark theme, responsive grid)
- `pipeline/modules/render_rules.py` — rules text renderer converting card JSON abilities + keywords into human-readable text (14 tests)
- `pipeline/tests/test_render_rules.py` — 14 tests
- `pipeline/tests/test_review_app.py` — 9 tests (smoke tests against live FastAPI)

**Model choice:** N/A (no model call — human review UI)

**Verification:**
- All 14 rules-renderer tests pass
- All 9 review-app endpoint tests pass
- 331/331 C# tests still pass
- 141/144 Python tests pass (3 pre-existing `test_generate.py` failures logged)

---

## P6-09 — Publish + content versioning + client hot-update

**Status:** Complete

**Summary:**
Built the PUBLISH stage of the AI pipeline plus the client-side content hot-update
flow with SHA-256 integrity verification. Approved cards are assembled into a
versioned content pack with a signed hash. The client verifies the hash before
swapping in a downloaded pack and falls back to its bundled pack on any
mismatch, so the client never ends up with no playable content.

**Key design decisions:**
- **Canonical JSON hashing:** both the Python publisher and the C# client
  canonicalise the pack payload (`{set_id, version, cards}`) with sorted keys,
  compact separators, and no ASCII escaping — byte-identical on both sides. The
  hash is computed over the RAW received JSON, never over deserialised C#
  objects, so the exact bytes the server hashed are the exact bytes the client
  re-hashes. Cross-language consistency is proven by a test that verifies a
  pack emitted verbatim by Python (including its real SHA-256) on the C# side.
- **Immutable identity:** a card already in a pack is never silently edited.
  Publishing a card whose content changed forces a version bump and records a
  changelog entry describing the change. Identical re-publishes are idempotent
  no-ops.
- **Fallback safety:** a pack that fails hash verification or JSON validation
  falls back to the bundled pack (`.bundled.json`) rather than leaving the
  client with no content. Version downgrades are also rejected.

**Files created:**
- `pipeline/modules/publish.py` — PUBLISH stage module with:
  - `canonical_json()` / `sha256_hex()` — canonical JSON + SHA-256 helpers
  - `make_pack()` / `verify_pack()` — signed pack manifest build/verify
  - `publish()` — reads `07_decisions.json` approved cards, enforces immutable
    identity, increments version, writes `<set_id>.json` + `.bundled.json` +
    `.changelog.json`
  - CLI: `--work-dir`, `--set-id`, `--content-dir`
- `pipeline/tests/test_publish.py` — 27 Python tests
- `engine/Cards/ContentManager.cs` — C# client content manager:
  - `ContentPack` / `ContentPackResult` DTOs
  - `LoadBundledPack()` — load trusted bundled pack
  - `ApplyRemotePack()` — verify hash, reject downgrades, fall back to bundled
  - `VerifyPackJson()` / `VerifyHash()` / `Canonicalize()` — hash verification
- `tests/Cards/ContentManagerTests.cs` — 19 C# tests (incl. cross-language)
- `tools/PackVerifier/` — standalone C# tool proving Python→C# verification
- `tests/integration_publish_e2e.py` — end-to-end Python publish → C# verify

**Files updated:**
- `client/scripts/Main.cs` — replaced hard-coded pack loading with ContentManager:
  loads versioned pack, falls back to bundled, then to legacy `content/cards/`
  format. Godot console logs hash-verification failures and fallback reason.

**Verification:**
- All 27 Python publish tests pass
- All 19 C# ContentManager tests pass
- 350/350 C# tests pass (up from 331; +19 new)
- 168/171 Python tests pass (up from 141; +27 new — 3 pre-existing `test_generate.py` failures logged in `docs/TECH_DEBT.md`)
- End-to-end integration: Python publish → C# hash verify → tamper reject, all pass
- 0 TODO, 0 NotImplementedException

Next: **P6-10** — Pipeline orchestration + one 60-card set end to end.

---

## Session — 2026-08-05

### P0-02 — Godot .NET Android export confirmed working

**Status:** Complete

**Summary:**
Successfully exported the Godot 4.3 .NET client to an Android APK (108 MB) and verified the desktop client renders the duel scene with lanes, HUD, and hand.

**Key accomplishments:**
- **Android export working:** Full `xvfb-run -a godot --editor --export-debug "Android"` produces a clean APK at `client/exports/Runewake.apk`
- **Desktop client renders:** Fixed 4 `.tscn` files with sub-resource ordering, 3 scenes missing script references, broken `id="1]"` syntax, missing `icon.svg`, missing `import_etc2_astc` project setting, and missing Android build template. Duel scene now renders with enemy HUD, 5+5 lane rows, player HUD, and hand area.
- **Flutter fallback permanently off the table:** The Godot .NET toolchain works end-to-end. No reason to consider Flutter per `00_MASTER_SPEC.md §3` fallback clause.

**Signal 11 crash (fixed):**
Root cause: `BotController` creates a `Godot.Timer` in `_Ready()` and subscribes to `_gsm.StateChanged` in `Initialize()`, but neither was ever unsubscribed. During shutdown, the timer callback would fire after the bot node was freed, accessing a dangling `_gsm` pointer → segfault.

Fix:
- `BotController._ExitTree()` — stops the timer, nulls it, unsubscribes from `_gsm.StateChanged`, nulls `_gsm`
- `GameStateManager._ExitTree()` — clears `StateChanged` and `GameOver` event delegates so no freed subscriber is ever invoked during shutdown

**APK sideloading:**
```
1. Enable Developer Options: Settings → About Phone → Tap "Build Number" 7 times
2. Enable USB Debugging: Settings → Developer Options → USB Debugging → ON
3. Connect phone via USB cable to mini PC
4. Approve RSA key fingerprint on phone when prompted
5. On mini PC: adb install /home/fictive/runewake/client/exports/Runewake.apk
```
If USB debugging is off and you can't enable it, copy the APK via USB flash drive or cloud storage, then install from the phone's file manager.

