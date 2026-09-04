
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
