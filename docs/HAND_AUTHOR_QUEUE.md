# Hand-Author Queue — EMBER Set (costs 5+)

**Generated 2026-08-05 by pipeline v0.2 (Phase 6 close-out)**
**Pipeline yield:** 36/60 cards auto-generated (costs 1-4 cluster). Remaining
slots require hand-authoring — these are the memorable cards players build
decks around.

---

## By cost & rarity

| # | Cost | Rarity | Type | Slots | What it should be |
|---|:---:|:---:|:---:|:---:|:---|
| 1 | 5 | COMMON | CREATURE | 3 | Curve-filler beater — efficient stats with a keyword (e.g. 7/6, SWIFT) |
| 2 | 5 | UNCOMMON | CREATURE | 1 | Efficient threat — good stats + a small ability |
| 3 | 5 | RARE | CREATURE | 1 | Finisher — high stats + keyword synergy |
| 4 | 5 | RELIC | RELIC | 1 | Build-around bomb — strong relic effect worth building a deck for |
| 5 | 6 | COMMON | CREATURE | 2 | Curve-topper — big body, maybe one keyword, no abilities |
| 6 | 6 | UNCOMMON | CREATURE | 1 | Stabilizer — solid stats + a defensive ability |
| 7 | 6 | RARE | CREATURE | 1 | Game-swinging threat — big stats + impactful ability |
| 8 | 6 | RELIC | RELIC | 1 | Win condition — identifies into a game-ending effect |
| 9 | 7 | COMMON | CREATURE | 1 | Rarely-played vanilla — big dumb body (players will cut it) |
| 10 | 7 | RARE | CREATURE | 1 | Splashable finisher — works in any deck that reaches 7 mana |
| 11 | 7 | RELIC | RELIC | 1 | Splashable finisher — same, as a relic |
| 12 | 8 | COMMON | CREATURE | 2 | Build-around finisher — requires specific deck support |
| 13 | 8 | RARE | CREATURE | 1 | Build-around finisher — requires specific deck support |
| 14 | 8 | RELIC | RELIC | 1 | Build-around finisher — requires specific deck support |
| | **Total** | | | **18** | |

**14 CREATURE + 4 RELIC slots.** All at costs 5-8. No cost 9 or 10 in the
seed target for this set.

---

## Design notes per cost tier

### Cost 5 — the curve topper (8 target, 1 scored)

The pipeline scored one (Furnace Golem, 6→5). Gap is 7 cards. Cost 5 is the
last cost where a "fair" creature still works — 7/6 with a keyword or small
ability. At COMMON, this is the slot for the playable-but-unexciting filler
that makes a curve work.

### Cost 6 — the stabilizer (6 target, 2 scored)

Both scored cards are RELIC-type (Cinderheart Relic, Forgekeeper). Zero
CREATURE-type cards scored at cost 6. This is the "I need to not die" cost
range — cards that gain life, destroy a thing, or present an immediate
blocker. The model can't value defensive stats + abilities correctly here.

### Cost 7 — the splashable bomb (4 target, 1 scored)

One RELIC scored. No creatures. Cost 7 should contain cards that any deck can
run if the game goes long — they don't need synergy, they're just good. The
model puts conservative stats here (6/7 with WARD) that don't pass score.

### Cost 8 — the build-around finisher (5 target, 1 scored)

One RELIC scored. No creatures. Cost 8 is for cards that are unplayable
without the right deck but win the game when they land. These need hand-authoring
because they're the emotional center of the set.

---

## Stat constraints for reference

Under the v0.2 formula, a cost-N creature needs roughly these stats to pass:

| Cost | attack + vigor floor | Example |
|:---:|:---:|:---|
| 5 | 13 | 7/6 = 13 |
| 6 | 15 | 8/7 = 15 |
| 7 | 17 | 10/7 = 17 |
| 8 | 19 | 11/8 = 19 |

RELIC-type cards use `1.8 × cost` as their base (replacing attack/vigor), so
a cost-6 relic base = 10.8 before keywords/abilities.

See `02_CARD_DSL.md` §4 and `05_AI_PIPELINE.md` §Stage 4 for the full formula.