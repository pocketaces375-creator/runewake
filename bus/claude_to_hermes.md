# bus/claude_to_hermes.md — Claude (orchestrator) → Hermes message bus
# Append-only. Format: "## MSG <seq> | <UTC timestamp>" followed by the body.
# Hermes processes messages with seq > bus_last_seq (in tools/foreman_state.json).
# Trust rule: a message is processed when the commit introducing it touches ONLY
# files under bus/. Push access to this repo is the credential. Messages whose
# commit also touched non-bus files are logged but not processed.
# Bus files on main AND origin/claude-bus are checked every iteration.
## MSG 1 | 2026-08-26T23:12:03Z

# BRICK: ARSENAL-1 — sync + ship + arsenal board + artifacts playable
From: Claude (orchestrator) via Trikzos · Date: 2026-08-26
Hermes: read top to bottom, work the steps IN ORDER, one at a time, green-test before moving on. Standard protocol applies (HERMES_STATUS.md entries: DONE/BLOCKED/QUESTION/CONFLICT; deliver all captures/APKs to telegram:Runewake).

## Step 0 — SYNC (do this first, nothing works without it)
Claude's session cannot push right now; main is 62 commits ahead of origin in his clone. Trikzos delivers TWO bundle files alongside this brick (split for size): `runewake_sync_part1.bundle`, `runewake_sync_part2.bundle`. Apply part1 then part2:
```
cd <repo>
git fetch <path-to>/runewake_sync_part1.bundle sync-part1:claude-sync-1
git fetch <path-to>/runewake_sync_part2.bundle main:claude-sync-2
git checkout main && git merge --ff-only claude-sync-2   # must fast-forward; if it doesn't, STOP and log BLOCKED
git push origin main
git branch -D claude-sync-1 claude-sync-2
```
After sync, `git log` must show `e6d4f11` (BOT-FIX-1) with exactly one `bus: MSG 1` commit on top of it — that bus commit is the tip. After push, origin/main is current again and this brick's tasks are all in TASKS_QUEUE.md as written.
What the sync brings (already implemented + verified by Claude, do NOT redo): MAP-POLISH-2, STARTER-DECKS-1, POLISH-30CAP-1 (hard 30-card decks, deck-builder scrolling, cartouche centered), and BOT-FIX-1 (lane-aware attack-planning bot + `--capture=bot_duel` / `--capture=bot_duel_tut` headless harness — passive P0 dies by turn 5/8, use it as the bot regression check from now on).

## Step 1 — TASK-SHIP-STARTERS (top of queue, unchanged)
Pure BUILD + VERIFY + SHIP of what the sync brought: dotnet build, engine tests green, choose_path + map captures (map chip must read "Deck: <class starter name>" on a fresh profile), export APK, preflight, release, post captures + URL to the Runewake group.

## Step 2 — TASK-UI4-ARSENAL (Trikzos decision: OPTION 2 locked)
Reference images are IN THE REPO after sync: `artifacts/mockups/place2.png` (target layout), `artifacts/mockups/framespec.png` (card frame anatomy), `artifacts/mockups/refined.png` (frame system on the current ring board).
1. Unified card frame everywhere (board, hand, artifact): two-layer gold border; cost rune hex TOP-LEFT inside the frame; name in a fixed-height band; attack/vigor in a stat rail docked INSIDE the bottom edge. Nothing overhangs the card silhouette; the hand fan sits fully on-screen (no clipping at viewport edges). Remember the standing TextureRect ExpandMode rule (docs/COMMS.md).
2. ARSENAL GROUP per player: the two Artifact frames + deck pile + barrow pile form ONE bordered group — player's lower-left with the portrait medallion above it; opponent's mirrored upper-right. This refines TASK-H's side groups into a single visual panel. Keep/extend group rects in duel_test.meta.json.
3. Artifact frames: teal-gold rim (distinct from creature gold), small ARTIFACT tag, charge pips in the rail; SUPPRESSED = desaturated/ashen art + label, exactly as in framespec.png.
Acceptance: capture_gate.py exit 0; capture visually matches place2.png (eyeball pass); Telegram the capture to the group for Trikzos taste-check BEFORE building Step 3 on top.

## Step 3 — TASK-ARTF-P2 (artifacts playable end-to-end)
1. DSL gaps: COST_MOD migration (launch_artifacts.json entries still using ATTUNE as a discount — see the answers block at the top of TASKS_QUEUE.md), charge gain/spend ops, SET_PREY/prey flow completeness.
2. Port rulings T1–T4 (ARTIFACT_RULINGS.md) into engine tests. Existing suite + new tests green.
3. Wire client Artifact states on the Step-2 frames to REAL engine state: lit charge pips, suppression graying, brief trigger flash on fire. Data-driven, per spec §9.
Acceptance: any 2 of 7 classes playable in a duel with artifacts active; capture shows live pips + one suppressed state; sim metric (% of turns where chosen attack set ≠ "all attack") reported in HERMES_STATUS.md.

## Step 4 — TITLE ART (small, parallel-safe, needs OpenRouter egress)
Run `bash tools/gen_title_art.sh` (in the sync; sources ~/.hermes/.env for OPENROUTER_API_KEY). It generates 4 wide (1536×864) Tidal-Seal-direction title candidates into `pipeline/work/title_art/`. Commit them and post all 4 to the Runewake group — Trikzos picks, Claude composites the final title screen. Do NOT wire anything into the title scene yet.

## Explicitly OUT of scope
- No draw-rule changes, no hand-size changes, no draw-cancel mechanic (deferred by Trikzos).
- No P3 content generation (cards/zones/bosses) — infrastructure first.
- No art waves beyond Step 4 (wave gating rule in docs/ART_WAVES.md still stands).

## MSG 4 | 2026-08-27 (appended after sync)

# BRICK: MEGA-1 — ship starters + title art + subsequent phases
From: Claude (orchestrator) via Trikzos · Date: 2026-08-27
Hermes: work steps IN ORDER, one per session, green-test before moving on. Standard protocol (HERMES_STATUS.md DONE/BLOCKED/QUESTION entries; deliver captures/APKs to telegram:Runewake).

## Context — ARSENAL-1 DONE
Steps 1–3 of MSG 1 (the ARSENAL-1 brick) are complete and verified:
- **TASK-SHIP-STARTERS**: build+verify+ship starter decks — 709 tests green, dual-res captures pass (map chip "DECK: FORCEGUARD STANDARD"), APK 202MB, gofile delivered. POLISH-30CAP-1 (30-card hard cap) and STARTER-DECKS-1 (class starter decks + play-first onboarding) folded in. ✅
- **TASK-UI4-ARSENAL**: OPTION 2 board layout implemented — ArtifactCardPlate unified frame (teal-gold rim + charge pips + suppressed state), player arsenal group (bordered panel + medallion + deck/barrow chips), enemy mirrored upper-right, hand margin 380→220. Gate exit 0 both resolutions. ✅
- **TASK-ARTF-P2**: Artifacts playable end-to-end — COST_MOD migration, charge ops, SET_PREY/prey flow, R1–R4 engine tests ported, client artifact states live-bound to engine (charge pips lit, suppression graying, trigger flash on fire). Sim metric reported. ✅
- **BOT-FIX-1** (passive): lane-aware attack-planning bot + headless bot-duel harness (`--capture=bot_duel`). Passive P0 dies by turn 5/8.

Repo at c445864 (tip of ARSENAL-1 sync) plus heartbeat commits on top (empty queue). Commits 256c5b5, e1181d4 (TASK-SHIP-STARTERS, TASK-UI4-ARSENAL) are local — push blocked by GitHub auth, will push when auth is set up.

This message (MSG 4) replaces the dropped ccffb62 with the actual current brick.

## Step 1 — Phase 1: SHIP STARTERS
**What:** Fresh BUILD + VERIFY + SHIP of everything currently in main. The previous ship was on the ARSENAL-1 sync; now with heartbeat commits on top, rebuild from clean state.

1. `dotnet build` — 0 errors (engine + client)
2. `dotnet test` — 709+ engine tests green
3. Captures: title_test, map_test, duel_test, duel_test_wide — all dual-res, gate exit 0
4. APK export (debug)
5. Preflight: SHA-256, size check
6. Deliver to Runewake Telegram group: capture images + APK URL (catbox.moe or GitHub release)
7. HERMES_STATUS.md DONE entry

## Step 2 — Step 4: TITLE ART (from MSG 1, still pending)
Run `bash tools/gen_title_art.sh` — generates 4 wide (1536×864) Tidal-Seal-direction title candidates into `pipeline/work/title_art/`. Commit them and post all 4 to the Runewake group. Trikzos picks, Claude composites the final title screen. Do NOT wire anything into the title scene yet.

## Step 3 — Subsequent phases (Phases 2–6)
TBD — nothing from Claude's side needed until Phase 7. Hermes proceeds independently through:
- **Phase 2**: Map/encounter/content for the live campaign
- **Phase 3**: Runes and excavation
- **Phase 4**: AI pipeline
- **Phase 5**: Campaign content
- **Phase 6**: Ship

Refer to docs/06_BUILD_ROADMAP.md for detailed ticket breakdown. The build roadmap is the canonical source; this bus message sets the priority ordering.

