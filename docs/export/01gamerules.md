# 01 — Game Rules (current, reconciled against engine as of 2026-08-31)

Canonical sources: `docs/01_GAME_RULES.md` (duel rules v0.1), `FIELD_EFFECT_SPEC.md` (artifact system v1.1), `ARTIFACT_RULINGS.md` (timing contract R1–R26 + G1–G8), `DECK_SPEC.md`, engine code (`engine/`). This file merges them and marks where the built engine diverges from the older rules doc.

## Setup

- Deck: exactly **30 cards** (hard 30 — POLISH-30CAP-1 changed the original min-30/max-40 to exactly 30; `DeckRules.MaxSize = 30`, tests pin it). Max **2 copies** of any card, max **1 copy** of any Relic-rarity card. Decks may draw from one or two Strata; **deck cards are class-agnostic** (class identity lives entirely in Artifacts — Decision Log 2026-08-12).
- Starting Vigor (life): configurable **20–30, default 25** (pre-duel brass dial, `MatchConfig.StartingVigor`).
- Starting hands & first-player compensation, as actually built (`GameState.cs`, `DuelEngine.cs`): **P0 (first) draws 4 cards, takes an Attune step at Initialize, and skips their turn-1 draw. P1 (second) draws 5 cards.** (The rules doc's "+1 Attunement for the Second Delver" reads differently from what's built — what's built is authoritative; whether it's *sufficient* is the open first-player-advantage problem, see 08.)
- Both players' Artifacts are revealed at duel start, before mulligans; mulligan = shuffle back any subset once, redraw the same number.

## Resources: Attunement

- +1 max at the start of each of your turns, cap 10, refills fully every turn. No resource cards, by design (mobile: shuffle-variance losses read as bugs).
- Cards cost 0–10. Temporary raises past 10 possible via effects, never permanent.

## The board: five lanes

Each player has 5 lanes (0–4), one creature each, facing the opponent's same-numbered lane. Summoning chooses an empty lane. Lane choice powers the design space: adjacent / opposing / flanking / empty-lane / edge-lane effects.

## Turn structure

1. **Attune** — +1 max, refill.
2. **Draw** — draw 1 (P0 skips on turn 1).
3. **Start triggers** — `ON_TURN_START` in board order, active player first. Ranger's Prey mark resolves BEFORE all other turn-start effects and before draw (R15).
4. **Main** — play cards, attack (no separate declaration step: tapping a ready creature attacks immediately and resolves per-lane).
5. **End** — `ON_TURN_END` triggers; all "at end of turn" effects resolve BEFORE "until end of turn" effects expire (G2); hand cap 10, discard excess.

## Combat

- A **Ready** creature attacks once per turn; summoned-this-turn creatures are **Exhausted** unless **Swift**.
- Opposing lane occupied → simultaneous damage equal to Attack; 0-or-less Vigor is destroyed. Damage persists between turns; no auto-heal.
- Opposing lane empty → attacker hits the enemy player, UNLESS the enemy has any **Guard** creature — then you must attack a Guard lane (lane-based guard, NOT global taunt — decided). **Reach** may attack the opposing lane or adjacent to it.
- **Pierce**: excess damage over a destroyed blocker carries to the enemy player.
- The "everything always attacks" degeneracy is fixed via Artifact incentives, not new combat rules (Guard stance proposal REJECTED by Trikzos, permanently — do not implement).

## Win condition

Opponent at 0 Vigor. Empty-deck draws cause escalating **Fatigue** (1, then 2, then 3...). No instant deck-out loss.

## Zones

Deck · Hand · Board (5 lanes) · Discard · **Barrow** (face-down buried cards; public count, private contents; fed by Bury/Excavate; mined by Hollow/Tide effects) · **Artifact slots** (`artifactSlots[]` per player, ordered array — launch classes use 2; slot count is data, not schema). Artifact cards can never change zones; combat AI must never target artifact slots (tested).

## Artifacts (the class/weapon system — the game's signature mechanic)

- Each class brings a fixed pair (launch: 7 classes × 2 = 14 artifacts; later: 3 variants per slot = 42). Not part of the 30-deck; can't be drawn/discarded; always visible full-art flanking the portrait; active from turn 1.
- Anatomy: one **passive** (static, always-on unless Suppressed) + one **trigger** (`WHEN event [IF cond]: effect`) — no activated abilities in v1.
- **Charges**: per-card counters, cap 3, visible to both players; charge-full effects fire immediately on the 3rd unless the card says end-of-turn (G8).
- **Suppression** (the counterplay): artifacts are indestructible but suppressible. While suppressed: passive off, triggers dead, charges frozen; permanent buffs already granted remain (G3). Duration counts in the suppressed player's turns; same source refreshes, different source extends (G4). Sources: deck cards costed like premium removal, Duskfang's 3-charge effect, campaign bosses. Suppressed state must be readable at a glance (full-art state change).
- Trigger ordering: active player's artifacts first, then opponent's; left slot then right; through the TriggerBus, never interleaved mid-effect (G1).
- Twin rule (Thief): identical daggers = passive does not stack, both triggers fire with separate charge pools (R10). Same-id passives never stack in mirrors; each player's charges/marks are their own (G6).
- **Prey marker** (Ranger): a single mutable per-player reference marking one enemy creature — the only new engine primitive in the entire roster. Tie-break: longest in play. Details R15–R18.
- Full per-card text: export file 02 (verbatim DSL) + FIELD_EFFECT_SPEC.md §5 + ARTIFACT_CLASSES.md. All 26 per-card rulings + 8 general rulings are the test contract (ARTIFACT_RULINGS.md) — every ruling has at least one engine test.

## Keyword set (CLOSED — additions require engine change + version bump)

Guard · Swift · Pierce · Ward · Venom · Reach · Rooted · Unearth N · Echo · Fragile · Sealed — definitions in export file 03. Signature mechanics: **Excavate N**, **Bury**, **Relic identification** (relics enter play Unidentified as 0/3, flip when their Identify condition is met at turn start).

## Card types

CREATURE (attack/vigor, occupies lane) · RITUAL (one-shot spell) · RELIC (permanent, enters Unidentified) · CURSE (persistent attachment) · TOKEN (never in deck) — plus ARTIFACT (`kind: artifact`, slot-zone only).

## Determinism

Single seeded PRNG in `GameState.Rng`. Same seed + same actions = same game. Every match writes a replay `(seed, contentVersion, List<Action>)`. All balance work runs on this.

## Known rules-level open items

See 08-open-questions: first-player advantage (structural, measured ~62.5% P0 mirror winrate), sim roster vs real class roster mismatch, 25-vs-20 starting Vigor, second-player compensation retune options (b)–(e) awaiting mirror-harness data.
