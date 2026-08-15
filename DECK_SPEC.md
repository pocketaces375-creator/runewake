# DECK_SPEC.md v1.0 — Deck Creation Architecture (Trikzos directives 2026-08-15)
RULES (engine-enforced, single source of truth):
- Deck size: 30 to 40 cards inclusive (was: exactly 30). Both bounds in one place (DeckRules class), never scattered constants.
- SINGLETON: max 1 copy of each unique card id per deck. No exceptions at launch.
- Artifacts are NOT deck cards (unchanged) — chosen separately, live in artifactSlots.
- Starting life (Vigor): configurable 20-30 per match via new MatchConfig (default 25). Replaces hardcoded 25 in PlayerState. All existing healing/damage/artifact vigor math unchanged — only the starting/max value becomes data.
- Deck validation errors must be specific strings: "too few cards (28/30 minimum)", "duplicate: <card name>", never a bare invalid flag.
PRESENTATION (the Ancient Tome — house storybook-brushwork canon):
- Deck builder is a weathered ancient tome, open two-page spread. LEFT page: your collection, cards laid out like painted illustrations in an old bestiary, page-turn navigation (corner arrows + swipe), filters as ribbon bookmarks along page edge (strata/type/cost/rarity). RIGHT page: current deck as an inked manifest list (name, cost, count N/40) with singleton violations shown as red ink annotations.
- Adding a card: brief non-blocking animation, card drifts from left page to right manifest (<=0.4s). Removing: reverse drift.
- Palette from ThemeTokens: parchment, aged leather, faded gold leaf, ink. NO flat gray panels. Placeholder art uses existing parchment/stone placeholder.
- Title screen: new "Decks" entry alongside existing buttons, opens the tome. Same visual language as existing title screen.
- Life-point setting: a small brass dial/slider (20-30) on the pre-duel confirm step, default 25.