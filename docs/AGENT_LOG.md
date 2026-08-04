# AGENT_LOG

## Session 1 — 2026-08-04

### P0-01 — Repo init
### P0-02 — Godot .NET mobile smoke test (scaffolding complete)
### P0-03 — Core state types

## Session 2 — 2026-08-04

### P0-03 — Core state types

Created `engine/State/` with five data types:

- **`SeededRng`** — Deterministic SplitMix64 PRNG. `Clone()` produces an independent copy at the same position.
- **`CardInstance`** — Runtime card instance. Tracks zone, lane, damage, modifiers, exhaustion, identification status, granted/removed keywords, and attached curses. Deep Clone.
- **`LaneState`** — One of five lanes per player. Holds an occupant (creature/relic) and attached curses. Deep Clone.
- **`PlayerState`** — Vigor, attunement (max/per turn), deck/hand/discard/barrow lists, five lanes, fatigue counter, max hand size. Deep Clone.
- **`GameState`** — Two players, current player index, turn number, seeded RNG, content version, next instance ID, trigger depth cap, game-over flag. `CurrentPlayer`/`Opponent` shortcuts. Deep Clone (except ActionLog, which is append-only).

Removed the template `Class1.cs` from engine.

**Tests:** 6 clone-isolation tests — SeededRg, CardInstance, LaneState, PlayerState, GameState deep copy, RNG independence. All pass with 0 warnings.

## Session 3 — 2026-08-04

### P1-01 — Card definition model + JSON loader

Created `engine/Cards/` with complete card data model matching `docs/02_CARD_DSL.md` §2 enums and `schema/card.schema.json`:

| File | Contents |
|---|---|
| `CardEnums.cs` | Strata, CardType, Rarity, Duration, Trigger, Op, Scope, ConditionOp — all values match JSON SCREAMING_SNAKE_CASE |
| `CardDef.cs` | Top-level card: id, set, name, strata, type, rarity, cost, attack, vigor, keywords, abilities, identify_condition, flavor, art, power_score, content_version |
| `AbilityDef.cs` | Trigger, optional condition, activation_cost, effects list (1–2) |
| `EffectDef.cs` | Op, TargetDef, amount, attack, vigor, keyword, token_id, duration |
| `TargetDef.cs` | Scope, optional filter string, TargetCount (1–3 or ALL) |
| `TargetCount.cs` | Custom struct + JSON converter for count (int or "ALL") |
| `ConditionDef.cs` | Op, value (JsonElement), compound all/any |
| `ArtDef.cs` | Prompt + asset URL |
| `CardLoader.cs` | `LoadPack(path)` — deserializes JSON array of CardDef |

**Tests:** 7 tests — one per example card (Root Warden, Cinder Runner, Silt Reader, Gravewrit Thrall, Sealing Light, Aelin's Seal) asserting all field values, plus one count test. All 6 cards from `schema/example_cards.json` load and validate correctly.
