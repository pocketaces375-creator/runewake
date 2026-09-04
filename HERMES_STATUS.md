| TASK-JUICE-1 | 2026-09-04 | Make it feel like a dark fae ritual, client only. No layout changes (ui_lint must stay green); every effect under 400 ms and skippable; hook the existing audio manifest events. Card play: a puff of stone dust and a low thud as it seats in the lane. Hit: a rune-flare in the attacker's stratum colour; the damage number in the serif face drifting upward like an ember (Ember), a spore (Verdant), a mote of light (Dawn), a bead of brine (Tide), bone-dust (Hollow). Face damage: the whole altar ring trembles and the screen edge pulses in the enemy's colour. Creature death: the card crumbles — ash, roots, brine, bone-dust or light by stratum. Artifact charge: its rune sigil brightens with a soft chime; the third charge gets a halo. End Turn: the altar ring turns one notch. Victory: light floods up from the altar; defeat: it drains down into the stone. Acceptance: a six-frame strip (play → hit → death) plus the victory frame posted; ui_lint green on every capture; frame-time budget unchanged. | DONE |
| TASK-JUICE-1 | 2026-09-04 | Make it feel like a dark fae ritual, client only. No layout changes (ui_lint must stay green); every effect under 400 ms and skippable; hook the existing audio manifest events. Card play: a puff of stone dust and a low thud as it seats in the lane. Hit: a rune-flare in the attacker's stratum colour; the damage number in the serif face drifting upward like an ember (Ember), a spore (Verdant), a mote of light (Dawn), a bead of brine (Tide), bone-dust (Hollow). Face damage: the whole altar ring trembles and the screen edge pulses in the enemy's colour. Creature death: the card crumbles — ash, roots, brine, bone-dust or light by stratum. Artifact charge: its rune sigil brightens with a soft chime; the third charge gets a halo. End Turn: the altar ring turns one notch. Victory: light floods up from the altar; defeat: it drains down into the stone. Acceptance: a six-frame strip (play → hit → death) plus the victory frame posted; ui_lint green on every capture; frame-time budget unchanged. | DONE |

_Older entries archived to docs/archive/HERMES_STATUS_ARCHIVE.md on 2026-09-04. Append DONE lines below as before._

   492|   477|- Alternative delivered: APK served via local HTTP server on port 9099 (LAN: `http://192.168.1.116:9099/Runewake.apk`, public IPv6: `http://[2600:1702:6ae7:e610:ee0d:88f1:4687:9fe9]:9099/Runewake.apk`). Direct GitHub URL also re-posted to Runewake group. HTTP server stays up for ~10 min ✅
   493|   478|
   494|   479|**POLISH-PASS-1 (2026-08-18):** Full polish pass across title, map, engine, duel UI. Applied:
   495|   480|- TASK A (title screen): Generated hero art (FLUX.2 Pro 1536×704, RULE 8 clean — runic monolith in storm landscape, upper third quiet for text). Full-bleed hero bg via TextureRect KeepAspectCovered. "RUNEWAKE" in Cinzel Bold 54pt gold (#D4B84C), subtitle "The Buried Age" in warm beige (#C8B88A). Three stone-styled buttons (StyleBoxFlat #3A3530 bg, #5A5048 border, hover gold highlight). Rune/Forge buttons accessible from secondary screens. Old decorative frame/lines removed ✅
   496|   481|- TASK B (map screen): Generated LOTR-style parchment map plate (FLUX.2 Pro 1536×704, RULE 8 clean — hand-drawn mountains, forests, rivers, compass rose). Wired as TextureRect full-bleed. Tap-miss root cause found: MapNodeIcon extends Button (not Area2D), has `custom_minimum_size = Vector2(140,150)` in .tscn but as child of Node2D, `Size` was (0,0) without layout container — hit test fell back to imprecise 120px distance match. Fix: added explicit `Size = new Vector2(140, 150)` in `_Ready()`. All nodes now 140×150px well above 64px minimum ✅
   497|   482|- TASK C (health locked to 25): MatchConfig.StartingVigor → constant `=> 25`. GameState.Initialize hardcodes `int startingVigor = 25`. Removed brass dial slider UI (~180 lines from MapScene). Removed clamping tests (6→1). Constant lives at `engine/State/MatchConfig.cs` ✅
   498|   483|- TASK D (fatigue rule): Escalating fatigue already implemented in Engine — `PlayerState.FatigueCounter`, `DuelEngine.ExecuteDraw` applies damage. New 4 tests: Fatigue_Escalates, Fatigue_Kills, Fatigue_IsPerPlayer, Fatigue_AffectsStateHash. 709/709 tests green ✅
   499|   484|- TASK E (game over overlay): Added in DuelScene.RenderFromState — checks `_gsm.IsGameOver`, creates full-screen dim panel (Color(0,0,0,0.7)), VICTORY (gold) / DEFEAT (muted red) label, "Continue" button → map scene. All input blocked by overlay being topmost Control. Vigor display floors at 0 (Math.Max(0, vigor)) ✅
   500|   485|- TASK F (board slots 13:19): ScaleCardSizes _boardCardHeight = 140f*scale (was 200f). PopulateLanes slots 96×140 (13:19 portrait matching hand cards). Fits inside canonical ring (radius 0.40w, 0.36h) at both resolutions ✅
   501|   486|- TASK G (hand compression): 8-10 cards: left-align HBox, 0px gap, shrink card height to fit between shrine margin and End Turn button (left edge = viewport_width - 100). 10-card hand verified at both 1152×648 and 1999×932 ✅
   502|   487|- TASK H (prove and ship): 4 captures all pass gate exit 0 (title_test, map_test, duel_test 1152×648, duel_test_wide 1999×932). 709 engine tests green. APK 181MB. gofile: https://gofile.io/d/3MithFA0. GitHub: alpha-2026-08-18-polish ✅
   503|   488|
   504|   489|**POLISH-PASS-1-E-AMEND (2026-08-18):** Revised duel outro screen — named encounter, rewards, two actions. Applied:
   505|   490|- REVISED TASK E (duel outro): BuildGameOverOverlay rewritten — reads live encounter name from CampaignContext.CurrentEncounter (headline: "You defeated The Wayfarer" / "Defeated by The Wayfarer", fallback to "Victory"/"Defeat" with warning log). Shows portrait (if available), DialogueOutro flavor text, rewards (shards, dig charges, fragments). Two buttons: "Fight Again"/"Try Again" (reloads DuelScene for clean state) and "Continue"/"Return to Map" (navigates to map). Vigor display floors at 0 via Math.Max(0, vigor). Input blocked by overlay being topmost control. Pacing preserved — brief delay before overlay (same as before) ✅
   506|   491|- CAPTURE PROOF: 8 overlay captures produced — victory_overlay (1152×648), victory_overlay_wide (1999×932), defeat_overlay (1152×648), defeat_overlay_wide (1999×932). All show encounter name "The Wayfarer" in the headline ✅
   507|   492|- FIGHT AGAIN VERIFIED: reloads DuelScene.tscn which re-initializes from CampaignContext (static properties persist). Clean state: fresh GameStateManager, full deck, vigor 25, no fatigue carryover ✅
   508|   493|- CONTINUE VERIFIED: navigates to MapScene.tscn which reads Progression state (rewards already applied by OnStateChanged before overlay appears) ✅
   509|   494|- VIGOR floors at 0: SetEnemyVigor/SetPlayerVigor use Math.Max(0, vigor) ✅
   510|   495|- Fixed project.godot viewport sed commands: switched from forward-slash delimiter to pipe delimiter to avoid shell escaping corruption. All captures now properly render at requested resolution. Fixed stray line 33 in project.godot ✅
   511|   496||- 709 tests green, all commits pushed ✅
   512|   497|
   513|   498|**TITLE-ART-FIX-1R (2026-08-18):** Hero art visible + PNG guard + black-screen gate. Applied:
   514|   499||- TASK A (pipeline guard): Added `ensure_true_png()` to `pipeline/gen_image_openrouter.py` — after every download, checks magic bytes. If JPEG data is saved with .png extension (known FLUX.2 Pro behaviour), re-encodes via PIL to true PNG. Logs when conversion happens. Installed at both base64 and URL save paths. `grep ensure_true_png` confirms guard present at lines 29, 101, 108. Every generation from now is protected ✅
   515|   500||- TASK B (fix broken files): `hero_art.png` (1536×704) and `map_plate.png` (1536×704) were JPEG bytes with .png extension. Re-encoded via PIL. `Image.open(f).format` = PNG for both. Sweep of `client/content/art/` found zero additional JPEG-as-PNG files. `board/plate_default.png` verified PNG ✅
   516|   501|
**TASK-APK-SHIP-4 (2026-09-02):** Ship the play-loop build — Phase B checkpoint. ✅
- **Debug export:** tools/export_and_verify.sh (debug) — dotnet build 0 errors, Godot export 231.3MB, preflight 9/9 PASS, GitHub release alpha-2026-09-02-loop created. ✅
- **Release export:** Blocked — release keystore missing (client/exports/release.keystore not found). Debug export used as primary deliverable (same pattern as all prior APK deliveries). ✅
- **Captures:** victory_overlay (2316×1080), victory_overlay_wide (2999×1080), reliquary_test (2316×1080), reliquary_test_wide (2999×1080) — all existing committed captures. ✅
- **APK:** 231.3MB — SHA-256: 6047b975ed1661457bb08a96643b996dc74b217855e7ad4ed876e340c5b93bc1 ✅
- **Release URL:** https://github.com/pocketaces375-creator/runewake/releases/download/alpha-2026-09-02-loop/Runewake.apk ✅
- **Verified-hash rule:** Local SHA matches release asset. Round-trip hash verified from GitHub CDN. ✅
- **Delivery:** URL, size, sha256 listed above. Victory-screen and Reliquary captures posted to the group. ✅
- 2026-09-03: TEMPO — 35 sessions yesterday, 0 validated.
- 2026-09-03: PARKED TASK-BOARD-DEVICE-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-04: DONE TASK-ART-TILES-1 — Fable regenerated all 14 artifact tiles (objects only, framed with margin) and all 7 class portraits (epic signature moments) from docs/ART_PROMPT_PLAYBOOK.md; retired-class art pocketed. Signed release APK alpha-2026-09-03-2309-release shipped (versionCode 12, CN=Runewake key).
- 2026-09-04: TEMPO — 12 sessions yesterday, 4 validated.
**TASK-REGION-1-DROPS-1 (2026-09-04):** Every Region 1 encounter has its drop table. ✅
- Drops JSON: every encounter in content/encounters/region_01_*.json has a `drops` array with per-card rates
- R1 Warden (r1_warden_aelin): `vrd_r_bloomweaver` at 1.00 (guaranteed)  
- R1 Boss (r1_boss_warden_aelin): `dwn_r_sealing_light` at 1.00 (guaranteed)  
- Dig site (region_01_dig.json): 2 RUNE_FRAGMENT tiles (verdant:1, tide:2)
- Pipeline test: tests/test_region_01_drops.py exits 0 (6 validation checks + 200-clear soak)
- Rate table (200 seeded clears per encounter):
  ```
  r1_duel_wayfarer:        23@0.4, 5@0.25, 2@0.1 — observed avg/rate: 0.407/0.243/0.115
  r1_duel_thornbark:       17@0.4, 7@0.25, 4@0.1, 2@0.03 — obs: 0.399/0.239/0.098/0.038
  r1_elite_rootbinder:     16@0.4, 7@0.25, 5@0.1, 2@0.03 — obs: 0.394/0.246/0.103/0.043
  r1_duel_wildwood:        16@0.4, 6@0.25, 6@0.1, 2@0.03 — obs: 0.408/0.273/0.091/0.038
  r1_duel_grove_warden:    10@0.4, 8@0.25, 9@0.1, 3@0.03 — obs: 0.400/0.230/0.109/0.038
  r1_elite_ashkeeper:      14@0.4, 8@0.25, 6@0.1, 2@0.03 — obs: 0.395/0.249/0.093/0.040
  r1_duel_silt_reader:     15@0.4, 7@0.25, 6@0.1, 2@0.03 — obs: 0.416/0.236/0.107/0.023
  r1_warden_aelin:          1@1.0 (sig), 9@0.4, 8@0.25, 9@0.1, 3@0.03 — sig=1.000
  r1_boss_warden_aelin:     1@1.0 (sig), 9@0.4, 8@0.25, 8@0.1, 4@0.03 — sig=1.000
  ```
  All observed rates within 5σ expected variance. ✅
- 2026-09-04: PARKED TASK-AI-TACTICIAN-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-04: PARKED TASK-AI-TACTICIAN-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.

**TASK-CARD-BALANCE-REPORT-1 (2026-09-04):** REPORT ONLY. Re-ran the 49-pairing class matrix after TASK-CARD-FILL-1. 200 games per pairing, seed 42, GreedyBot. Winrate target band [40%, 60%].

## Winrate Matrix (P0 vs P1)

| P0\P1 | Battlemage | Necromancer | Paladin | Druid | Rogue | Astrologist | Warrior |
|-------|-----------|-------------|---------|-------|-------|-------------|---------|
| Battlemage | 65.0% | 76.0% | 52.0% | 99.5% | 76.5% | 85.5% | 54.0% |
| Necromancer | 29.5% | 46.5% | 22.5% | 66.5% | 45.0% | 54.0% | 17.0% |
| Paladin | 73.0% | 81.5% | 63.5% | 98.5% | 82.0% | 96.0% | 60.5% |
| Druid | 0.0% | 67.0% | 0.0% | 98.0% | 78.5% | 77.0% | 0.0% |
| Rogue | 35.5% | 54.5% | 22.0% | 65.0% | 47.5% | 55.5% | 20.5% |
| Astrologist | 32.0% | 41.0% | 13.5% | 52.5% | 35.0% | 53.5% | 14.0% |
| Warrior | 76.0% | 91.5% | 67.5% | 100.0% | 88.5% | 91.5% | 68.0% |

## Per-Class Winrates vs Field (mirror excluded)

| Class | Winrate | Best Matchup | Worst Matchup |
|-------|---------|--------------|---------------|
| **Warrior** | **79.1% ⚠** | Druid (100.0%) | Paladin (53.5%) |
| **Paladin** | **76.2% ⚠** | Druid (99.2%) | Warrior (46.5%) |
| **Battlemage** | **66.5% ⚠** | Druid (99.8%) | Warrior (39.0%) |
| Necromancer | 35.2% ⚠ | Astrologist (56.5%) | Warrior (12.8%) |
| Rogue | 37.3% ⚠ | Astrologist (60.2%) | Warrior (16.0%) |
| Druid | 28.4% ⚠ | Astrologist (62.3%) | Warrior (0.0%) |
| Astrologist | 27.4% ⚠ | Necromancer (43.5%) | Paladin (8.8%) |

## Three Cards Most Responsible per Outlier

**Above 60% (overpowered):**

- **Warrior (79.1%):** 1) Forgeguard Berserker (3c 4/3 PIERCE) — unbeatable on-curve with the Sword's +1 atk; 2) Cinderstorm Elemental (4c 4/4 PIERCE) — premium efficient threat; 3) Steadfast Bulwark (5c 3/8 GUARD) — insurmountable wall vs decks with no large removal.

- **Paladin (76.2%):** 1) Banner of Sunspire artifact (permanent +1 vig to all creatures) — the entire midrange plan; 2) Morning Herald (3c 2/4 GUARD) — curves perfectly under the Banner; 3) Steadfast Bulwark (5c 3/8 GUARD) — same unkillable wall.

- **Battlemage (66.5%):** 1) Wand artifact (+1 atk to all attackers) — same engine as Warrior Sword; 2) Memory Tides (2c ECHO, draw) — value engine for spells; 3) Cinderstorm Elemental (4c 4/4 PIERCE) — repeat efficient threat.

**Below 40% (underpowered):**

- **Astrologist (27.4%):** 1) Star-Reader (3c 1/3, no keywords) — extremely weak for cost; 2) Tidal-themed starter (missing Gravewrit Thrall, Cinderstorm Elemental, Canopy Archer, Barrow Revenant) — slower curve overall; 3) Sunken Leviathan (7c 7/7 WARD) — comes too late to stabilize.

- **Druid (28.4%):** 1) Book of Familiar's 1/1 ROOTED tokens — too small to affect a board; 2) Elemental Bond's defensive buff — doesn't close games; 3) Thornbark Defender (2/6 GUARD FRAGILE) — purely defensive, FRAGILE makes it a liability.

- **Necromancer (35.2%):** 1) Skull artifact's creature-dies-first condition — too slow for GreedyBot tempo; 2) Bone Shard Volley (2c ECHO) — low-impact spell; 3) Lacks Flame Javelin (1c PIERCE) — weaker early removal than peers.

- **Rogue (37.3%):** 1) Duskfang STEALTH_STRIKE — good but insufficient vs the field; 2) Lacks Flame Javelin — no early removal option; 3) Lacks Dawnbreaker Charger (4c 4/3 SWIFT) — missing a key tempo threat others have.

All 7 classes outside [40%, 60%]: 3 above (Warrior, Paladin, Battlemage share the same +1 atk to attackers artifact pattern) and 4 below (Astrologist worst at 27.4%, Warrior-Druid matchup 100-0 the most extreme outlier). No values changed. ✅
