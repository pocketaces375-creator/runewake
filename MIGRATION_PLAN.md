# RUNEWAKE — Element/Class Coexistence Plan v1.1 (formerly "Migration Plan")

**Author:** Claude · **Date:** 2026-08-12 (v1.1 same day)
**v1.1 change:** Trikzos ruled that **deck cards are class-agnostic** — class identity comes only from the Artifact pair. That kills the hard element→class retag from v1.0 entirely. Elements are no longer being removed from cards; they demote from "archetype system" to "flavor + synergy tags." This document is now a much smaller cleanup plan. **v1.0's mapping table (Ember→Warrior etc.) survives only as flavor guidance and implied-synergy targets — it is NOT a data migration.**

---

## 1. What changes and what doesn't

**Stays exactly as-is:** all 65 cards' mechanics, costs, stats, art, element tags in data, element-referential card mechanics ("if you control a Tide card" still works — any class can build a Tide deck), the DSL, all 463 tests.

**Changes:**
1. **Deckbuilding rules:** any class may include any deck card. If any code currently gates cards by element at deck validation, that gate is removed (VERIFY whether one exists).
2. **Deckbuilder UI:** primary grouping switches from element to cost/type; element becomes a filter chip, not the top-level taxonomy. Class selection (with its fixed Artifact pair preview) becomes the first screen of deck creation.
3. **Elements demote in presentation:** element frames/colors may stay on cards as flavor, but nothing in UI should imply "you are an Ember player" anymore — the player *is* their class now.
4. **New content direction (design note, not a task):** future cards can carry light Artifact-synergy hooks (e.g., cards that care about Charges, Prey, or healing) so each class has natural deck-card homes. Ember cards lean Warrior-shaped, Tide lean Mage-shaped, etc. — the v1.0 mapping as a north star for *synergy*, not a restriction.

## 2. Hermes tasks (small, in order)

1. **Audit deck validation** for element gating; remove if present; test that a deck mixing all five elements validates for every class.
2. **Schema:** confirm `element` stays; add nothing card-side. (The `kind: artifact` and `class` fields apply only to Artifact cards per FIELD_EFFECT_SPEC §4.)
3. **Client:** deckbuilder regrouping per §1.2–1.3. Feature-flag the new grouping so it can ship independently of the Artifact system.
4. **AI decks:** existing element-pure AI decklists remain legal; retag each AI opponent with one of the 7 classes (use the v1.0 flavor mapping: Ember decks→Warrior, Tide→Battle Mage, Hollow→Thief or Necromancer, Verdant→Runesmith or Ranger, Dawn→Cleric) so campaign opponents exercise Artifact pairs in sims.
5. **Campaign/tutorial copy sweep:** find text that names elements as the player's identity ("choose your element", etc.) and log each hit in HERMES_STATUS.md for rewrite — log, don't rewrite solo.

## 3. Sanity checklist before closing this workstream

- A Warrior running 30 Tide cards is legal, playable in the duel scene, and the AI can pilot it.
- No test references a class-gated deck card.
- Deckbuilder first-run flow: pick class → see Artifact pair → build/borrow a deck.
- Zero data loss: all 65 cards present and unmodified, `git diff` on card data shows no mechanical changes.
