# 05 — AI Content Generation Pipeline v0.1

The backbone. Everything here runs **offline, server-side, batched**. Nothing here ever runs on a player's phone or during a match.

---

## 1. Stages

```
SEED -> GENERATE -> VALIDATE -> SCORE -> SIMULATE -> DEDUPE -> MODERATE -> ART -> APPROVE -> PUBLISH
```

Each stage is a separate Python module with a CLI entry point, reading and writing JSON to a working directory. Any stage can be re-run independently on a batch. Failed cards go to `rejects/` with a reason code, never silently dropped — the reject pile is the most valuable tuning data we will have.

### Stage 1 — SEED
A **generation request** is fully specified before any model call:
```json
{
  "batch_id": "b_2026_08_ember_01",
  "count": 60,
  "strata": "EMBER",
  "type_mix": { "CREATURE": 40, "RITUAL": 14, "RELIC": 6 },
  "cost_curve": { "1":6, "2":10, "3":12, "4":10, "5":8, "6":6, "7":4, "8":4 },
  "rarity_mix": { "COMMON": 28, "UNCOMMON": 20, "RARE": 9, "RELIC": 3 },
  "theme": "Cinderhold — forge city, ash, iron, things that were mined too deep",
  "mechanic_emphasis": ["SWIFT", "PIERCE", "BURY"],
  "forbidden_mechanics": ["EXCAVATE"]
}
```
Seeds are authored by a human (or by a template per region). **The model never decides what to make, only how to make it.** This is the difference between a coherent set and 60 pieces of noise.

### Stage 2 — GENERATE
- Model: any capable instruct model via OpenRouter. Batch of 10 cards per call.
- Prompt = system rules (DSL grammar, hard constraints) + 6 few-shot examples from `schema/example_cards.json` + the seed + a list of the last 200 names in this Stratum ("do not produce anything similar to these").
- Output: strict JSON array. Temperature ~0.9 for names/flavor coherence with variety.
- Retry policy: on parse failure, one repair attempt with the parse error appended; then reject.

### Stage 3 — VALIDATE (deterministic, no model)
- JSON Schema validation against `schema/card.schema.json`.
- Enum whitelist check on every trigger, op, scope, filter, keyword.
- Structural rules from `02_CARD_DSL.md` §3: ability count, effect count, nesting depth, summon-loop check, stat ranges, relic identify condition present.
- **Executability check:** construct the card in the C# engine via a small CLI bridge (`Runewake.Sim validate-card`) and confirm it instantiates and its abilities bind to real handlers. If the engine can't build it, it doesn't ship.

### Stage 4 — SCORE
Apply the power formula from `02_CARD_DSL.md` §4. Anything outside the rarity band is either auto-adjusted (nudge cost ±1 and re-score, once) or rejected.

### Stage 5 — SIMULATE
This is what separates this project from every "AI makes cards" demo.

- Build 3 archetype decks per Stratum (aggro / midrange / control) as fixed baselines.
- For each candidate card, substitute it into the appropriate baseline deck and run **1,000 headless games** against the full baseline gauntlet using `Runewake.Sim`.
- Metrics: win rate delta vs. the unmodified deck, average turn played, play rate when drawn, games where the card was in the winner's opening hand.
- Flag rules: win-rate delta > +4% → too strong. < −3% → too weak/unplayable. Both go to the review queue rather than auto-rejecting, because outliers are sometimes the interesting cards.
- The AI opponent used in sim is a **greedy heuristic bot** (evaluate all legal actions one ply, score board state), not a neural net. Cheap, deterministic, good enough for relative comparison. Do not build an MCTS agent in v1.

### Stage 6 — DEDUPE
Embed `name + rendered rules text`. Reject if cosine similarity > 0.93 against any published card. Also exact-match name check against the full catalog and against an IP-name blocklist.

### Stage 7 — MODERATE
Text safety pass plus a hard blocklist for real-world religious figures, trademarked names, and slurs. Cheap insurance against an App Store rejection that would cost weeks.

### Stage 8 — ART
- One **locked style prompt prefix per Stratum**, so the set looks like one game and not a stock-image folder. Example: `"EMBER: dark fantasy oil painting, ash and ember palette, soot-black and molten orange, heavy impasto, dramatic rim light, medieval woodcut influence, no text, no border, centered subject"`.
- Card-specific suffix generated at Stage 2 alongside the card and stored in `art.prompt`.
- Generate at 1024px, downscale to two mip levels, convert to WebP, upload to Supabase Storage, store CDN path.
- **Cost reality:** art dominates the budget. Text generation for a card is fractions of a cent; images are orders of magnitude more. Plan on generating art only for cards that clear Stage 5, and consider hand-commissioning the ~40 Relic-rarity cards, since those are the ones players screenshot.
- Every card ships with a fallback: a Stratum-colored frame with a rune glyph, so a missing image never blocks a release.
- **Moderation reality (HOLLOW stratum):** the default model `black-forest-labs/flux.2-pro` applies a content-safety filter that flags HOLLOW-stratum prompts (undead, bones, graves, death imagery) as "Violence" at a ~30% rejection rate. Alternate OpenRouter image models (`flux.2-flex`, `flux.2-klein-4b`) were tested and rejected the same or more prompts — **no model passes the full set at comparable quality**, so HOLLOW stays on FLUX.2-pro. **Never soften a prompt to clear moderation — the aesthetic is the product.** COMMON/UNCOMMON HOLLOW failures fall back to the Stratum frame; RARE/RELIC HOLLOW failures are flagged to `docs/ART_COMMISSION_QUEUE.md` for hand-commissioning. (See `docs/OPEN_QUESTIONS.md` Q1.)

### Stage 9 — APPROVE
A local review UI (a simple FastAPI + HTML page is enough) showing card, rendered text, power score, sim results, and art. Human clicks approve/reject/edit. **Keep the human in the loop for v1.** Once the reject rate is stably under ~10%, auto-approve COMMONs and review only UNCOMMON and above.

### Stage 10 — PUBLISH
- Approved cards append to `content/packs/<set_id>.json`.
- Pack gets a version integer and a SHA-256 hash.
- Client checks version on launch, downloads if newer, verifies hash, hot-swaps.
- Every published card is immutable in identity; balance changes ship as a new content version with a changelog, never a silent edit.

## 2. Where the AI is *not* used

Stating this explicitly, because scope creep here is the main risk to the project:

- Not for rules text (derived by renderer).
- Not for balance decisions (formula + simulation).
- Not at runtime, ever.
- Not for the Codex without heavy human editing — the lore is the voice of the game and it is the one thing that cannot read as machine-made.
- Not for the opponent AI in duels (heuristic bot).

## 3. Throughput target

One region expansion = ~60 cards. Pipeline should produce a reviewed, simulated, art-complete 60-card set in **under a week of wall-clock time with a few hours of human review**. If it takes longer than that, the bottleneck is almost certainly the approval UI, not the model.

## 4. Directory layout

```
pipeline/
  seeds/                 # generation request JSON
  work/<batch_id>/
    01_raw.json
    02_valid.json
    03_scored.json
    04_simulated.json
    05_approved.json
    rejects/
  modules/
    generate.py  validate.py  score.py  simulate.py
    dedupe.py    moderate.py  art.py    publish.py
  review_app/            # FastAPI approval UI
  config.yaml
```
