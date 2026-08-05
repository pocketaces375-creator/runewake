# OPEN_QUESTIONS

## Q1: HOLLOW stratum — FLUX.2-pro content moderation rejection rate

**Date:** 2026-08-04
**Filed by:** Agent (P6-07 live test)
**Status:** OPEN — awaiting human decision

### The problem

`black-forest-labs/flux.2-pro` applies a safety filter that flags certain HOLLOW-stratum prompts as "Violence" and refuses to generate them. The HOLLOW stratum's aesthetic is defined as "decayed palette, bone-white and murky violet with sickly green" — this inherently involves undead, bones, graves, and death imagery. The FLUX moderation filter blocks a significant fraction of these prompts.

### Live test data

10 unsanitised HOLLOW prompts were sent to `black-forest-labs/flux.2-pro` via the live OpenRouter API:

| Status | Count | Rejected prompts |
|--------|-------|-----------------|
| Success | 7 | — |
| MODERATED | 3 | "Gravewrit Thrall" (rotting flesh/bones), "Soul Harvest" (reaper/souls), "Barrow Revenant" (revenant/burial mound) |

**Rejection rate: 30% (3/10)**
**Cost: ~$0.35 for 7 successful images**

All 3 rejections were for "Violence" — specifically:
- "rotting flesh" / "decayed undead" (Gravewrit Thrall)
- "gathering souls" / "scythe" / "blood moon" (Soul Harvest)
- "revenant rising from burial mound" (Barrow Revenant)

Prompts that succeeded: Skeletal Reaver, Deathspeaker, Bone Shard Volley, Crypt Crawler, Ossuary Guard, Wraith Stalker, Curse of Binding.

### Options (need human decision)

1. **Alternate model for HOLLOW** — Switch to `black-forest-labs/flux.2-flex` or `black-forest-labs/flux.2-klein-4b` (may have different moderation thresholds). FLUX.2-flex is described as "excels at rendering complex text, typography, and fine details" — unknown if moderation is identical.

2. **Prompt strategy** — Tune the generate module to produce less graphic HOLLOW prompts (e.g. "ancient bone construct" instead of "skeleton", "shadowy figure" instead of "undead", "moonlit graveyard" instead of "blood moon"). This preserves the HOLLOW identity while avoiding trigger words.

3. **Hand-commission** — Accept the ~30% rejection rate and hand-commission the ~3 Relic-rarity HOLLOW cards. The pipeline's fallback (coloured frame + rune glyph) handles the rest gracefully.

4. **Live with it** — 30% fallback rate is acceptable. The fallback frame looks intentional (Stratum-colored frame with rune glyph). Player perception risk: all HOLLOW cards with missing art look the same.

### What's needed

A decision on which option(s) to pursue before the pipeline processes HOLLOW batches in production. The art module already handles API failures gracefully (falls back to coloured frame), so no code changes are needed to keep the pipeline running — but the visual quality of HOLLOW cards will vary.