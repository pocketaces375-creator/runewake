# ART WAVES — Wave-Gate Release Policy

**Established:** HOMESTRETCH-1, 2026-08-17
**HARD RULE:** Production STOPS at the end of every wave. No self-releasing. Ever.

## Wave Sequence

| Wave | Content | Status |
|------|---------|--------|
| **W1** | Ember samples (6) in v3.0 locked style → `pipeline/work/samples_ember_s3/` | ✅ Done (HOMESTRETCH-1) |
| **W2** | Verdant portrait redo (6) → `pipeline/work/samples_verdant_s2/` | ⏳ Awaiting Trikzos release brick |
| **W3** | Tide samples (6) → `pipeline/work/samples_tide_s1/` | ⏳ Awaiting W2 approval |
| **W4** | Hollow samples (6) → `pipeline/work/samples_hollow_s1/` | ⏳ Awaiting W3 approval |
| **W5** | Dawn samples (6) → `pipeline/work/samples_dawn_s1/` | ⏳ Awaiting W4 approval |
| **W6+** | Full-batch production per strata for remaining launch cards, then future roster | ⏳ Awaiting W5 approval |

## Wave Protocol

1. Generate 6 sample images in the **current locked style** (v3.0 as of 2026-08-17).
2. Run **RULE (8) corner check** — any lettering/signatures → regenerate once with reinforced negatives; if persists, post anyway but flag in caption.
3. **Post all 6 individually to Telegram**, numbered 1–6, captioned with card names.
4. **Commit the images** to `pipeline/work/samples_<stratum>_s<N>/`.
5. **STOP.** Do not proceed to the next wave.
6. Trikzos sends a **release brick** (e.g. `BRICK: WAVE-2-RELEASE`) approving the previous wave.
7. Only then may the next wave begin.

## Per-Wave Details

### W1: Ember (DONE)
- 6 cards: Flame Javelin (COMMON), Wildfire Adept (UNCOMMON), Lava Serpent (UNCOMMON), Cinderstorm Elemental (UNCOMMON), Phoenix Ash (RARE), The Last Ember (RELIC)
- v3.0 spine: classical storybook oil painting, chiaroscuro, expressive brushwork, Renaissance tableau composition, impasto texture
- Palette: "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents"
- RULE (8) applied: corner check for painted signatures/lettering

### W2: Verdant portrait redo
- 6 cards: Thornbark Defender (COMMON), Wildwood Stalker (COMMON), Canopy Archer (UNCOMMON), Elder Treant (UNCOMMON), Nature's Renewal (RARE), Heartwood Relic (RELIC)
- Existing WIP in `pipeline/work/samples_verdant_s1/` (1024x1024 square) — redo in 832x1216 portrait with v3.0 spine
- Palette: "deep forest greens and earthy moss browns with golden highlights"

### W3: Tide
- 6 cards from `content/cards/tide.json`
- Palette: "abyssal blue-teal depths with pale foam edges and scattered sea-green light"

### W4: Hollow
- 6 cards from `content/cards/hollow.json`
- Palette: "bone-white and murky violet with patches of sickly green, shadow-heavy"
- HOLLOW canon: never soften prompts. On API refusal, accept fallback and note the id in DONE line.

### W5: Dawn
- 6 cards from `content/cards/dawn.json`
- Palette: "warm cream and pale gold with soft amber light, dawn-sky tones"

### W6+: Full-batch production
- Remaining launch cards per strata, then future roster
- Full pipeline: generate → validate → score → simulate → dedupe → art → review → publish