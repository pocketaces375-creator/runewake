# RUNEWAKE — Artifact System (Field-Effect Cards) — Design Specification v1.1

**Author:** Claude (design lead) · **Date:** 2026-08-12 (v1.1 same day)
**Status:** ACTIVE — §11 decisions resolved by Trikzos; engine work is GO. Still pending reconciliation against PROJECT_EXPORT.md (§12).
**v1.1 changes:** system officially named **Artifacts** · Guard stance shelved · 7-class roster confirmed (see ARTIFACT_CLASSES.md for classes 4–7) · deck cards are class-agnostic — all class identity lives in the Artifact pair · migration downgraded (see MIGRATION_PLAN v1.1)

---

## 1. Overview & Design Goals

Every player brings a **class** into a duel. Each class carries **two Artifacts** that sit in permanent slots flanking the player's character portrait and act as persistent field effects for that player.

- Warrior → **Sword** + **Shield**
- Battle Mage → **Wand** + **Aura**
- Thief → **Dagger** + **Dagger** (twin pair)

Design goals, in priority order:

1. **Playability first.** Every rule here must be readable at a glance in the duel scene. If a player can't tell what an Artifact is doing right now by looking at it, the design has failed.
2. **Art integration is a gameplay feature.** Artifacts are always visible, full-art, and their state (ready / charged / triggered / suppressed) is communicated through the art layer, not through text scanning.
3. **Fix the combat-decision problem.** Combat currently has no meaningful choices (every creature that can attack should attack). The Artifact system is the primary vehicle for fixing this — see §7.
4. **Future-proof the slot count.** Launch classes use 2 slots, but the framework must support 1-slot and 3-slot classes later without engine rework.

## 2. Terminology & Zones

- **Artifact** — a class-specific card occupying an Artifact Slot. Not part of the 30-card deck. Cannot be drawn, discarded, or put into any other zone.
- **Artifact Slot** — a new zone, `ARTF_LEFT` and `ARTF_RIGHT` (engine: an ordered array `artifactSlots[]` per player, so 1/3-slot variants are a data change, not a schema change).
- **Suppressed** — an Artifact state in which its passive is off and its triggers do not fire. The card itself never leaves the slot.
- **Charge** — a counter some Artifacts accumulate (see §5). Charges are the bridge between combat decisions and Artifact payoffs.

## 3. Acquisition & Deckbuilding

- Artifacts are **chosen at deckbuilding** as part of the class loadout. At launch each class has exactly one fixed pair; the deckbuilder shows them as locked-in identity, not choices.
- The framework supports **variants** later (e.g., three different Swords the Warrior can unlock via campaign). A loadout is: `class + one card per slot from that class's legal pool for that slot`. Thief is the special case: both slots draw from the Dagger pool, and the two chosen daggers may be identical or different.
- Both players' Artifacts are **revealed at the start of the duel**, before mulligans. They are open information at all times.
- Artifacts enter play at duel start, active immediately (passives on from turn 1). Any "enters play" trigger they carry fires at duel start, after the coin flip, in turn-order.

## 4. Card Anatomy

Every Artifact has exactly two rules components (per Trikzos's decision — no activated abilities in v1):

1. **Passive (aura):** an always-on static effect. One line. Applies while the card is not Suppressed.
2. **Trigger:** one triggered ability, `WHEN <event> [IF <condition>]: <effect>`. Routed through the existing TriggerBus. Does not fire while Suppressed.

DSL extension (adjust to the real schema — Hermes reconciles against the actual card DSL):

```
{
  "id": "artf_warrior_sword_01",
  "kind": "artifact",
  "class": "warrior",
  "slotPool": "sword",
  "passive": { ... static effect DSL ... },
  "trigger": { "event": "...", "condition": {...}, "effect": {...} },
  "charges": { "max": 3, "gainOn": "...", "spendOn": "trigger" }   // optional block
}
```

Rules text on the card face shows passive on top, trigger below, always in that order, so players learn one reading pattern.

## 5. Launch Loadouts (v1 numbers — first-pass, expect tuning)

These six cards are deliberately built so each class answers the "should I attack?" question differently (§7). All numbers assume the existing engine's scale (creatures roughly 1–8 attack/health — VERIFY against export §5 and retune proportionally if wrong).

### Warrior — the committed aggressor with a safety net
- **Ancestral Blade (Sword).** *Passive:* Your creatures have +1 attack while attacking. *Trigger:* WHEN three or more of your creatures attack in the same turn: the first creature the opponent's next removal spell targets this round survives with 1 health instead of dying. — Rewards the all-in attack but makes the opponent's punish weaker, creating a real "swing wide or hold" fork for both players.
- **Bulwark of the Line (Shield).** *Passive:* Your creatures that did NOT attack this turn get +0/+1 until your next turn. *Trigger:* WHEN a friendly creature is attacked while you control no attackers (nothing of yours attacked last turn): prevent the first 2 damage dealt to it. — This is the card that makes *not attacking* a real option for the first time.

### Battle Mage — spell-tempo engine
- **Warden's Focus (Wand).** *Passive:* The first spell you cast each turn costs 1 less. *Trigger:* WHEN you cast your second spell in a turn: gain 1 Charge (max 3). Spend all Charges automatically when you cast a spell that targets a creature — it deals +1 damage (or heals +1) per Charge spent.
- **Mantle of the Living Rune (Aura).** *Passive:* Your character takes 1 less damage from the first attack against them each turn. *Trigger:* WHEN an enemy creature attacks your character: your next spell this turn costs 1 less. — Punishes mindless face-attacks by converting them into mage tempo.

### Thief — twin daggers, positional tempo
Daggers are intentionally the simplest cards (thief complexity comes from doubling):
- **Whisperfang (Dagger).** *Passive:* The first friendly creature to attack each turn gains stealth-strike: it can't be counter-damaged this attack (takes no damage back from the creature it attacks). *Trigger:* WHEN exactly one of your creatures attacks this turn: draw a card at end of turn.
- **Duskfang (Dagger).** *Passive:* Your creatures with 2 or less attack cost 1 less to play. *Trigger:* WHEN a friendly creature deals damage to the enemy character: gain 1 Charge (max 3). At 3 Charges, automatically: your opponent's Artifacts are Suppressed for one full turn, then reset Charges to 0.
- **Twin rule:** if both slots hold the *same* dagger, its trigger fires twice but its passive does not stack. (General rule: passives with the same id never stack; triggers always both fire.) A same-dagger loadout is the "all-in" thief build; mixed daggers is the flexible build.

Note the deliberate asymmetry: Warrior asks "attack wide or hold?", Mage asks "spend spells now or bank the discount?", Thief asks "attack with exactly one, or push everything for Charges?" Three classes, three different combat-math textures — from six cards.

### 5a. The Runewake question — VERIFY
The project has an existing rune system (export §says done). If runes are a per-turn resource, Artifact costs/discounts above refer to runes. If runes are something else entirely (sockets, cosmetics, campaign progression), Hermes must flag this in HERMES_STATUS.md and the discount effects get re-expressed in whatever the real resource is. **The spec intentionally does not invent a resource system — it plugs into the existing one.**

## 6. Suppression (the counterplay rule)

Artifacts are **indestructible but suppressible** (Trikzos's decision):

- Suppression durations are always counted in the *suppressed player's* turns: "Suppress for 1 turn" = until the end of that player's next turn.
- While Suppressed: passive off, triggers don't fire, Charges are frozen (not lost, don't accumulate).
- Suppression sources: (a) deck cards with a Suppress effect — each non-tutorial class list should get 1–2, costed like premium removal; (b) Duskfang's 3-Charge effect (§5); (c) campaign/boss effects.
- Suppression does not stack in duration from the same source id (re-applying refreshes, not extends). Different sources extend.
- **Art/UI requirement:** a suppressed Artifact gets a full-art state change (chained / frosted / dimmed — art team's call), a turn-counter pip, and its passive's ongoing visual effects on the board disappear immediately. This state must be readable from across the room. No tooltip-only states.

## 7. Combat Depth — how Artifacts fix "everything always attacks"

Diagnosis (from Hermes's export, priority #4): with no blocking and no counter-pressure, attacking is free value, so the attack decision is degenerate.

The fix is Layer 1 below. (A Layer 2 "Guard stance" proposal existed in v1.0; **Trikzos decided NO — shelved.** Kept here only as a historical note; do not implement any Guard mechanics.)

**Layer 1 — Artifact-driven incentives (no new combat rules).** Every launch Artifact above makes some attack pattern *conditionally* correct: Bulwark makes holding back real, Mantle taxes face-attacks, Whisperfang makes single-attacker turns real, Ancestral Blade makes alpha-strikes a calculated risk rather than the default. This alone breaks "always attack everything" — the correct attack set now depends on both players' visible Artifacts. Because Artifacts are open information, this is *plannable* depth, not randomness. Zero new combat rules; it's all TriggerBus work.

**Layer 2 — SHELVED.** The Guard-stance proposal was rejected by Trikzos (2026-08-12). Combat depth is delivered entirely through Artifact incentives (Layer 1), now across seven class textures — see ARTIFACT_CLASSES.md.

## 8. Variant Framework (later, but designed now)

- Slot count is per-class data (`artifactSlots: N`), not engine constant. 1-slot class = one monolithic artifact (e.g., a Colossus class with a single Greathammer with a bigger rules budget); 3-slot = three weaker pieces.
- Rules budget guideline for balance: total Artifact power per player is a constant; divide it by slot count. One card per slot pool at launch; variants are added per-pool with campaign unlock gates.
- Nothing else in this spec may assume "exactly two."

## 9. Art Integration Requirements (hard requirements, not nice-to-haves)

1. Artifacts render **full-art, always visible**, flanking the character portrait — left slot and right slot mirror-composed so class pairs read as a set (sword left / shield right, etc.).
2. Four visual states, each a distinct art treatment: **Ready** (idle glow), **Triggered** (fire animation ≤ 0.8s, non-blocking — must not delay input), **Charged** (pips or intensity scaling with Charge count), **Suppressed** (per §6).
3. When a passive modifies board objects (e.g., +1 attack while attacking), affected creatures show a small class-colored rune, and hovering the Artifact highlights every object it is currently affecting. This is the single highest-value readability feature — prioritize it.
4. Trigger announcements use the existing effect-banner pipeline if one exists (VERIFY in client code); otherwise a minimal banner: card art chip + one line of text.

## 10. Balance Guardrails & Test Checklist

- Power budget (updated v1.1, since Artifacts now carry all class identity): an Artifact *pair* should generate roughly 1 rune of value per turn when the player plays into its pattern. A trigger should be opt-in (opponent or owner can play around it), never a pure clock. If an Artifact card would win the game with zero deck support, cut it.
- Engine tests Hermes must add (extend the existing 463-test suite): slot zone integrity (Artifact cards can never change zones), suppression on/off symmetry for every passive and trigger, Charge freeze under suppression, twin-dagger stacking rule (trigger doubles, passive doesn't), duel-start reveal and trigger ordering, N-slot generalization (a fake 3-slot test class), and a regression test that combat AI does not evaluate Artifact slots as attackable targets.
- Playtest metric for the combat fix: in logged sim games, % of turns where the chosen attack set differs from "all legal attackers attack." Baseline is ~0% today; target ≥ 25% of non-empty combat turns after Layer 1.

## 11. Decision Log — RESOLVED by Trikzos, 2026-08-12

1. **Name: Artifacts.** (Relics rejected — collides with existing RELIC card type; Glyph rejected — collides with rune-system Glyphs.) All docs, code identifiers, and UI use "Artifact".
2. **Guard stance: NO.** Shelved permanently unless Trikzos reopens it.
3. **Launch class count: 7**, each with 2 Artifact slots. Warrior / Battle Mage / Thief specced here (§5); Cleric / Ranger / Necromancer / Runesmith specced in **ARTIFACT_CLASSES.md**. 14 launch Artifacts total.
4. **Duskfang suppression: KEEP.** Thief is the designated anti-Artifact class. First nerf lever if sims show a problem: suppress one slot of the owner's choice (pre-approved, on the shelf).
5. **Variant scope (answered by Claude, pending Trikzos veto): one fixed pair per class at launch.** No per-slot choices in v1. Rationale: 7 fixed pairs = 21 matchup identities to balance and 14 artifacts × 4 visual states of art scope, which is already the largest art item on the board; per-slot pools multiply both. The §8 framework means variants are pure content later — campaign-unlocked alternates per slot pool, no engine work.
6. **Deck cards are class-agnostic.** Any class can run any deck card; only Artifacts are class-locked (a mage can't wield daggers, a thief can't wield a staff). Consequences: (a) class identity budget moves entirely into Artifacts — guardrail updated in ARTIFACT_CLASSES roster notes (~1 rune of value per turn per pair, up from §10's original figure); (b) element tags stay on deck cards as flavor/synergy hooks — see MIGRATION_PLAN v1.1; (c) balance watch: any deck-card combo that is broken with one specific Artifact pair can't be class-gated away, so sims must run each strong deck archetype against all 7 classes.

## 12. Assumptions to VERIFY against PROJECT_EXPORT.md

I wrote this without having seen the export (it hasn't reached my workspace yet — only Hermes's summary of it). Every "VERIFY" tag above, plus: the resource system's actual shape (§5a), creature stat scale (§5), whether "attacking a creature" vs "attacking the character" are both legal in current combat, the DSL's real field names (§4), and whether the TriggerBus supports once-per-turn trigger conditions natively. Anything that conflicts: the export wins on engine facts, this spec wins on design intent, and conflicts get logged in HERMES_STATUS.md rather than silently resolved.
