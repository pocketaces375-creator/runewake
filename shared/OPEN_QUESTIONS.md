# Open Questions & Tech Debt

Source: `~/runewake/docs/OPEN_QUESTIONS.md` and `~/runewake/docs/TECH_DEBT.md`

## Open Questions (awaiting human decision)

### Q1: HOLLOW stratum art — 30% FLUX moderation rejection
When generating HOLLOW-themed card art (undead, bones, graves), FLUX.2-pro
rejects ~30% as "Violence" content. Options:
1. Switch to FLUX.2-flex or FLUX.2-klein-4b (different moderation thresholds)
2. Tune prompts to avoid trigger words ("ancient bone construct" vs "skeleton")
3. Hand-commission the ~3 Relic-rarity HOLLOW cards
4. Accept 30% fallback (pipeline handles gracefully with colored frame)

**Current status:** OPEN. Not blocking launch.

## Key Tech Debt Items

### Pre-Release Gate: Dev Menu
`client/scripts/DevMenu.cs` must be removed before store submission.
Grants: jump to boss, +10 dig charges, +20 fragments, unlock all nodes,
clear save. Search for `REMOVE BEFORE RELEASE` in `.cs` files.

### Tutorial Architecture: Needs Rebuild
Three attempts at onboarding all failed. Root cause: instruction-before-action
model is a memory test. Next version must use consequence-first model
(do → see → understand). Priority: High after art lands.

### Combat Design Gap
Attacking has no meaningful cost — no blocking, no bad trades, no combat tricks.
Every creature that can attack should attack, which means no combat decisions.
Fix: introduce blocking, guard, vigor cost to attack. Priority: Medium,
post-launch.

### Engine Tests Encode Implementation
Tests written by reading code, not docs. When code had bugs, tests matched them.
Fix: every test must cite the doc section it tests. Tests in `tests/State/`
are the first with proper citations.

### First-Player Advantage (Bot-vs-Bot)
P0 always wins in bot-vs-bot simulations. GreedyBot is deterministic.
Pure first-player advantage may need compensation. Not a launch blocker;
flag for balance pass with real decks.

### Cost-10 Cards Never Tested
Cost 10 is mathematically reachable since v0.2 calibration but no card has
ever reached it. Unknown balance implications. Flag for manual review.

### Pacing Values Provisional
All animation timings (summon 0.3s, death 0.4s, damage float 0.9s) are
placeholders tuned against grey rectangles. Do NOT tune until real card
art is present.

### 3 Pre-Existing Python Test Failures
In `pipeline/tests/test_generate.py` — all from text drift between test
assertions and actual prompt strings. Easy fixes, not blockers.