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
