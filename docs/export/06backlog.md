# 06 — Backlog (done / in progress / remaining, with priorities)

Live queue: `TASKS_QUEUE.md` on origin/main (the operational source of truth — 46+ checked task records with full text). Production log: `HERMES_STATUS.md`. This file is the strategic snapshot as of 2026-08-31 ~17:30 UTC.

## DONE (verified working)

Core duel engine (~717 tests green) · lane-locked combat (Guard redirects, Reach, empty-lane hits Vigor) · Artifact system end-to-end (slots zone, suppression state machine, charges, 14 launch artifacts via DSL, rulings G1–G8 + R1–R26 as tests) · DSL extensions (turn counters, PREVENT_DAMAGE, COST_MOD + ATTUNE migration, cadenced passives, charge plumbing, partner-slot FORGE, ANCESTRAL_SHIELD, STEALTH_STRIKE) · opponent AI v1 (lane-aware; kills passive player by turn 5; bot_duel regression harness) · hard-30 class starter decks + deck rules + Ancient Tome deck builder · 9 unique encounter decks (no duplicate card ids, permanent test) · Region 1 hand-built map (Fallow Reach: duels, elites, dig site, Warden Aelin) · campaign profiles & saves v2 + save hardening (versioned format, auto-repair, init-race guard — TASK-SAVE-1) · stability soak (5 clean seeded bot duels) + warning cleanup · title screen art + intro splash · audio: AudioManager + bus hierarchy + settings screen + 81 CC0 files + 13-event manifest + call-site hookup (TASK-AUDIO-HOOK-1) · build/ship pipeline (GitHub Releases with verified-hash rule; latest public alpha tag `alpha-2026-08-29`) · 65 of ~440 card arts in-game (style-locked) · sim: 49-pairing matrix + telemetry + threshold gate (TASK-SIMGATE-1; currently FAILING for structural reasons — see below) · first-player-advantage diagnosis (TASK-P2COMP-1 report committed) · Deck Forge art-tile fix (TASK-DECKART-1) · walkthrough tutorial data+runner (TU1/TU2) and TUT-BUILD-1 (⚠ built against a broken border layout — re-verify).

## IN PROGRESS / HOT (priority order)

1. **TASK-BORDER-FIX-2 — the active fire.** BORDER-FIX-1 covered every card face in stone: NinePatchRect draws patch margins 1:1 in screen px; 148–197px margins on ~88px cards. Fix = downscale texture so margins land at band_px, or revert to the 8-piece scaled TextureRects; z-order band above art below text; name floor ≥8px + ellipsis; gate check "occupied card center must not match its own border"; re-verify tutorial highlight rects after. A follow-up fix claiming success was announced by Jett at 3:58 PM 08-31 but NOT pushed and its preview thumbnails still looked stone-covered — verify committed captures before accepting (a scheduled check at ~20:46 UTC does this).
2. **TASK-APK-SHIP-2** — queued, gated on Trikzos approving the border capture in the Telegram group. Standard ship + verified hash.
3. **TASK-CLASS-7** — add tidecaller/dawnward/ranger/occultist to content/classes.json (schema-matched; art exists; strata per DeckBuilderScene ~247-251; 4 class-core cards each from existing pools only; skip-and-report any class whose strata lacks cards).
4. **TASK-DECKSAVE-1** — Deck Forge save/load/overwrite-protect/unsaved-guard through the versioned save system (corrupt deck blob repairs to "no decks", never blank screen).
5. **TASK-DECKFILTER-1** — strata filter chips as real 44px buttons with selected states; captures at both resolutions.

## NEXT (queued conceptually, not yet in TASKS_QUEUE.md)

Fix P1 compensation from the mirror-harness variant data → re-run the balance gate green → THEN class tuning (numbers are Fable's) · tutorial re-verification against the fixed border (and fix Main.cs not navigating to duel when TutorialScriptId set — pre-existing bug found in BORDER-FIX-1) · duel-screen resolution audit (captures run at 1152×648 with ~88px cards while the approved target is 2316×1080 — decide design resolution before more pixel-perfect UI work).

## REMAINING TO ALPHA/LAUNCH (the gap list)

- **Backend & accounts (load-bearing for the Tower):** Supabase stand-up + unique-player identity + basic anti-cheat on boss-kill reporting. Required before the Tower's community gate and live counter.
- **Card drops on discovery:** per-encounter drop tables (foes drop their cards, Wardens drop rares — lore-driven); data model then reveal-moment UI.
- **Economy loop:** shards + dig charges buy nothing yet. One decision needed (packs / crafting / cosmetics) to unlock reward screens.
- **Collection browser** with the "NEW" moment · **victory/defeat/reward screens** (the dopamine hit; currently minimal) · **settings screen** exists via AUDIO-SYS but needs volume/graphics/replay-intro/credits/account completion.
- **Region generator:** map graph + themed enemy decks + elites/boss per batch, every batch through the sim gate.
- **The remaining ~375 cards** (333 deck + 42 artifacts target): pipeline exists (gen → IP screen → pixel gate → sim gate); starts once the balance gate is trustworthy. Art in waves per docs/ART_WAVES.md with the 6-sample veto gate.
- **The Tower (designed LAST, agreed):** one expansive floor + boss; community gate (e.g. 100 unique players fell the boss → floor 2 opens); live conqueror count on map.
- **Performance & size:** 202MB debug / 185MB release APK; release export needs `--headless` (Mono/Sqlite crash workaround); load times on mid phones.
- **Store track:** signing key, versioning, Play listing, privacy policy (mandatory once accounts exist).
- **Class balance pass** after weapons + compensation fix, via the matrix.
- Art waves beyond Region 1: boss portraits, region boards, Tower look.

## Graveyard (rejected/retired — do not resurrect)

Guard stance · global-taunt Guard · 38-piece Ember/gemini art batch · per-slot artifact choice at launch · model-fallback-to-Sonnet for Hermes agents · the FableBot Telegram bridge (parked, §6 of the 2026-08-31 FABLE_HANDOFF.md has full state if ever resumed).
