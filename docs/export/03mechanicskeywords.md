# 03 — Mechanics & Keywords (definitions + rulings)

Canonical: `docs/01_GAME_RULES.md` §8–9, `docs/02_CARD_DSL.md`, `ARTIFACT_RULINGS.md`. The keyword set is **CLOSED** — the AI card generator may only use these; adding one requires an engine change and a content version bump.

## Creature keywords (v1, closed set)

| Keyword | Rules text |
|---|---|
| **Guard** | Enemies must attack a lane containing a Guard creature while one exists (lane-based, not global taunt). |
| **Swift** | Not Exhausted the turn it is summoned. |
| **Pierce** | Excess combat damage to a destroyed creature hits the enemy player. |
| **Ward** | Prevents the next instance of damage dealt to this creature, then is removed. |
| **Venom** | Any creature damaged by this is destroyed at end of combat. |
| **Reach** | May attack the opposing lane or lanes adjacent to it. |
| **Rooted** | Cannot attack. (Prices up defensive statlines.) |
| **Unearth N** | When destroyed, returns to its owner's hand next turn at cost N. |
| **Echo** | This card's `ON_SUMMON` ability triggers twice. |
| **Fragile** | Destroyed at end of the turn it was summoned. (Tokens, tempo swings.) |
| **Sealed** | Cannot be targeted by enemy abilities. |

## Signature mechanics (the archaeology layer in-duel)

- **Excavate N** — look at top N of your deck, take one to hand, **Bury** the rest.
- **Bury** — put a card face-down in your **Barrow** (third zone; public count, private contents). Retrievable by Hollow/Tide effects; feeds "buried count" conditions.
- **Relic Identification** — Relics enter play Unidentified: face-down 0/3 in a lane, no attack. At your turn start, if the Identify condition is met, it flips permanently. Conditions come from a fixed list: `3+ cards in your Barrow` · `you control 3 creatures` · `you took damage last turn` · `turn 6 or later` · `you cast 2 spells this game`.

## Artifact-era keywords/primitives (added with the artifact system)

- **ANCESTRAL_SHIELD** (engine keyword, TASK-DSL-7) — the first enemy spell/ability that would drop an ally below 1 vigor clamps it to 1 instead of dying. Clamp, not prevention — damage triggers still fire. One use, lasts until your next turn (ruling R1).
- **STEALTH_STRIKE** (TASK-DSL-7) — the attack takes no counter-damage; decided at attack declaration (R8).
- **Prey marker** — per-player mutable reference to one enemy creature (Ranger). Marked at Ranger's turn start BEFORE all other turn-start effects; highest attack, tie → longest in play; persists through bigger creatures appearing until re-mark; not removed by suppression (R15–R18). Built as a generic reusable marker, not a bespoke path.
- **Suppression** — see rules file 01; G3/G4 define scope and duration precisely.
- **Charges** — per-artifact counter, cap 3, both players see it; frozen (not lost) under suppression; charge-full fires on the 3rd charge immediately unless text says end-of-turn (G8, R9, R12, R20, R24).

## DSL ops (engine effect vocabulary — see docs/02_CARD_DSL.md for schemas)

Core ops observed in content: `BUFF`, `DAMAGE`, `HEAL`, `DRAW`, `SUMMON`, `BURY`, `EXCAVATE`, `DESTROY`, `RETURN`, `ATTUNE` (raises AttunementMax — NOT a discount). Artifact-era additions (TASK-DSL-1..7):

- Turn-scoped counters/conditions: `ATTACKERS_THIS_TURN_GTE/EQ`, `SPELLS_CAST_THIS_TURN_EQ`, `NO_ATTACKERS_LAST_TURN`, `CREATURE_DIED_THIS_TURN` (side-aware), filters `HAS_NOT_ATTACKED` / `FIRST_ATTACKER` / `FIRST_ATTACKED`. Counters reset at the START of every turn, tracked per player (G5).
- `PREVENT_DAMAGE` — amount, source filter (ATTACK vs SPELL), frequency (`FIRST_ATTACK_EACH_TURN`, `ONCE_PER_ENEMY_TURN`), conditions (e.g. `FEWER_ALLY_CREATURES_THAN_ENEMY`).
- `COST_MOD` — THE discount mechanic (ATTUNE was wrongly used as a discount in early artifact JSON and was migrated): applies_to CREATURE|SPELL, filters (`ATTACK_LTE`), conditions, per-turn filters (`FIRST_SPELL_EACH_TURN`), duration, stacking, floor 0.
- Cadenced passives: `ON_TURN_START` with explicit ordering key (Prey first per R15, Censer heal after, then draw).
- Charge plumbing: `RESET_CHARGES`, `max_per_turn`, `max_per_creature_per_turn`, `ON_CHARGE_FULL` (with END_OF_TURN timing option), charge-freeze under suppression.
- Partner-slot: `PARTNER_CHARGES_GTE`, `FORGE` with `spend_from: PARTNER_SLOT` (all charges, +1/+1 per charge, target HIGHEST_COST, tiebreak OLDEST_IN_PLAY, charges kept if no creature — R25).

## General artifact rulings (G1–G8, the test contract — abbreviated)

G1 trigger order: active player first, then opponent; left slot then right; via TriggerBus. · G2 end-of-turn effects resolve before until-end-of-turn expiry. · G3 suppression: passive off, triggers dead, charges frozen; already-granted permanent buffs stay. · G4 duration in suppressed player's turns; same source refreshes, different extends. · G5 turn counters reset at every turn start, per player; conditions read the OWNER's counter unless text says otherwise. · G6 mirrors: same-id passives never stack, all triggers fire, charges/marks per player. · G7 "creature died" = any death, either side, unless text narrows it. · G8 charges per-card, cap 3, public; fire on 3rd immediately unless end-of-turn.

Per-card rulings R1–R26 are in `ARTIFACT_RULINGS.md` verbatim and each is pinned by at least one engine test (naming: `Ruling_R15_PreyTieBreaksOldest`). If engine and ruling disagree, the ruling wins.

## Rune system (meta-progression, out-of-duel)

Pre-game loadout modeled on pre-2017 LoL rune pages (docs/03_RUNE_SYSTEM.md): a page has 30 slots — 9 **Marks** (offense), 9 **Seals** (endurance), 9 **Glyphs** (arcana), 3 **Sigils** (identity). Every rune costs 1–4 **Rune Points**; page budget grows with Delver Level (12 at L1 → 48 at L20 cap), so slots and budget bind against each other (anti-stacking guardrail). Runes come in **Flat** (full effect turn 1) vs **Growing** (exceeds flat from ~turn 5–6) flavors. Pages unlock via campaign (1 → 6), swappable on the map, never mid-duel. IMPORTANT disambiguation (settled): **Attunement is the in-duel mana; runes are the pre-game loadout system; Artifacts never touch runes.** Sigil runes drop from Warden first-clears.
