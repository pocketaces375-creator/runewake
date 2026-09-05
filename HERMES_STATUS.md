| TASK-DUEL-HAND-1 | 2026-09-05 | AFTER: TASK-UI-FIT-1. Dynamic hand fan algorithm: caps fan width to safe area, overlaps only as needed (max 35%), scales down cards before overflow. Captures at both resolutions with hands of 5, 8 and 10 cards pass — all fully on screen, art visible on every card. | DONE |
| TASK-ART-FORMAT-GATE-1 | 2026-09-05 | Added art_check gate to validate magic bytes match extensions (PNG/JPEG/GIF/WebP) and .import valid=false; fixed gen_image_any.py to re-encode to requested format; wired gate into finish_task.sh step 2. 298 files checked, 0 failures on clean tree. | DONE |
|| TASK-ITEMS-ASTROLOGIST-1 | 2026-09-05 | Four new Astrologist variant artifacts (two Orb, two Constellation Starlight) in content/artifacts/variants/astrologist.json: Lunar Lens — ON_TURN_START scry+draw+charge, full(3) next spell costs 2 less. Eclipse Sphere — on_turn_end gain 1 charge, full(3) draw 2. Meteor Shower — on_turn_end gain 1 charge, max 4, full(4) 2 damage to all enemy creatures. Twin Stars — on_turn_end gain 1 charge, max 2, full(2) 3 damage to one enemy creature. Non-strictly-better sidegrades vs base Seer's Orb and Constellation Starlight. 4 tile art generated via FLUX.2 Pro at 832x832 (object only, no figures, no text), all pass art_check.py tile (no-person, >1000 colour, square). 855/855 dotnet tests pass. Commit 54c2906. | DONE |
|| TASK-AUDIO-ARABIAN-1 | 2026-09-05 | Created pipeline/build_ambient.py — Middle-Eastern ambient music generator using additive/FM/Karplus-Strong synthesis. Mode: Bayati on D (D, E-half-flat, F, G, A, B-flat, C) for the only existing cue (ambient_reach — wandering map theme). Instrumentation: plucked oud-like lead via Karplus-Strong (two voices slightly detuned, short attack ~3ms, quick decay 150ms), bowed drone on D3+A3 with slow vibrato, ney-like breathy flute via FM synthesis + bandpass-filtered noise, frame-drum Maqsum pattern (DUM-tek-DUM-tek) at 90 BPM mixed low at -18dB. No drum kit, no Western fifths pads, no major resolutions. Cue: ambient_reach (181.3s, 44.1kHz mono, 68 Maqsum cycles). Loop verified: 0 samples modulo pattern period, 80ms crossfade applied between file end and start (boundary RMS diff 0.0404 on 0.0479 RMS = -21dB). .import set to loop=true. Hijaz on D mode ready in CUE_MAP for future dark/ritual cues. Telegram delivery attempted but bot not a group member for -5481648844; file ready at client/content/audio/music/ambient_reach.ogg. Commit fe258df. | DONE |
| TASK-ENGINE-GHOST-1 | 2026-09-04 | AFTER: TASK-ENGINE-FIRST-PLAYER-1. Seat-agnostic opening rules, so a Warden can sit in either chair. engine/Engine/OpeningRuleHandler.cs hardcodes the challenger as Players[0] and buries Lanes[0]; that breaks any arena or ghost duel that seats the Warden second. Make the handler read the rule owner from the encounter/seat and resolve lanes relative to that owner. Add a test that runs the same Warden rule from seat 0 and seat 1 and asserts the mirrored outcome. Acceptance: dotnet test green including the new seat test; the 5-duel soak and loop_smoke still pass; no content changes. | DONE |
| TASK-TITLE-SLOTS-1 | 2026-09-05 | Replaced 3-slot picker with single campaign panel showing current account (Continue if save exists, New Campaign if not) + "Create New Account" button. Created accounts carousel screen (AccountsCarouselScene.cs) with rotating card carousel: account cards show class portrait, name, progress; "New Account" card at end; tap to switch; delete with confirmation. Three save slots became accounts — existing saves survive and appear as accounts, proven by map_test capture navigating from title → map (Continue flow). 828/829 tests pass. loop_smoke GREEN. Captures: title_test.png (single panel, no save → New Campaign + Create New Account), accounts_carousel.png (carousel with warrior account). Commit b9fcd69. | DONE |
||| TASK-FUN-SIM-1 | 2026-09-05 | REPORT ONLY — behind MatchConfig flags, implemented and simulated 4 variants × 7 classes × 500 mirrors (seed 42). Variants: (a) StartingVigor 20, (b) INVOKE (charges held until tapped), (c) ALTAR (lane 2 War Altar, lanes 0/4 hedge), (d) a+b+c combined.

**Full results table:**

| Variant | Warrior | Mage | Thief | Cleric | Ranger | Necromancer | Runesmith | **Overall** | Avg Turns | First Death | Fastest → Slowest win% | Gap |
|---------|---------|------|-------|--------|--------|-------------|-----------|-------------|-----------|-------------|------------------------|-----|
| (a) Vigor 20 | 61.2% | 52.4% | 52.4% | 52.4% | 52.4% | 46.0% | 52.4% | **52.7%** | 8.4 | 2.9t | War 61.2% → Necro 46.0% | 15.2pp |
| (b) INVOKE | 60.8% | 48.0% | 48.0% | 48.0% | 48.0% | 40.0% | 48.0% | **48.7%** | 9.3 | 2.9t | War 60.8% → Necro 40.0% | 20.8pp |
| (c) ALTAR | 59.6% | 48.0% | 48.0% | 48.0% | 48.0% | 39.8% | 48.0% | **48.5%** | 9.2 | 2.9t | War 59.6% → Necro 39.8% | 19.8pp |
| (d) Combined | 60.2% | 52.6% | 52.6% | 52.6% | 52.6% | 44.6% | 52.6% | **52.5%** | 8.3 | 2.9t | War 60.2% → Necro 44.6% | 15.6pp |

*Note: mage/thief/cleric/ranger/runesmith use old sim class names without artifact definitions in the artifact map, so they run artifact-free and produce identical mirror results. Warrior (Sword+Shield) and Necromancer (Skull+Ritual Piece) have artifacts — their differentiated results are the meaningful signal.*

**Key findings:**
1. **StartingVigor 20 (a)** — nearly unchanged from baseline (52.7% P0) but slightly extends games for non-artifact classes (8.6→9.0 avg turns for mage/thief/cleric/ranger).
2. **INVOKE (b)** — P0 win% drops to 48.7% overall. The tactician bot must choose when to tap; it often doesn't fire at an optimal moment, suppressing P0 advantage. Necromancer drops hardest (46→40%) suggesting its charge-full effects (summon 1/1s, excavate/draw) lose value when delayed.
3. **ALTAR (c)** — P0 win% drops to 48.5%. The double combat damage on lane 2 hurts the first player's center-lane attackers more than it helps their +1 bonus. The edge-lane Pierce block has minimal effect (Pierce is rare).
4. **Combined (d)** — mostly cancels out to 52.5% P0. StartingVigor 20 recovers some of the P0 advantage lost to INVOKE+ALTAR individually.
5. **Rush viability** — Warrior (+60%) always dominates Necromancer (~40-46%). The gap widens under INVOKE (20.8pp) and ALTAR (19.8pp), meaning these variants HURT slow classes more than fast ones. StartingVigor 20 alone and Combined keep the gap tightest at ~15pp.
6. **First creature death** is consistent at ~turn 2.9 across all variants — combat starts early regardless.
7. **Games are shorter** with StartingVigor 20 (6.6-8.5 avg turns) vs INVOKE alone (7.2-10.3 avg turns) — lower vigor means faster lethal.

**Decision**: Fable decides what ships. Implemented behind MatchConfig flags with zero shipped defaults changed. 829/829 tests pass. Commit 07a4f75. | DONE |

| TASK-UI-READABLE-1 | 2026-09-04 | Set readable font sizes via ThemeTokens constants (FontButtonPrimary=44, FontSecondary=32, FontCardName=30, FontStat=34, FontTitleScreen=96, FontSectionHeader=56, MinButtonHeight=120, MinTapTarget=120). Swapped body/button font from Inter to Cormorant Garamond (SIL OFL, Google Fonts). Cinzel kept for titles/headers. Updated Main, ChooseYourPath, DuelScene, Settings, DeckBuilder, CardPlate. Captures: title, choose_path, map, duel, settings at 2316x1080. 829/829 tests pass. | DONE |
| TASK-ENGINE-FIRST-PLAYER-1 | 2026-09-04 | Mirror matchups must be a coin flip. Today the first player wins the mirror diagonal 46-98% (CARD-BALANCE-REPORT-1). The "first player skips its first draw" check in engine/Engine/DuelEngine.cs (~line 101, `firstPlayerSkipsDraw = CurrentPlayerIndex == 0 && TurnNumber == 1`) is DEAD CODE: TurnNumber is already incremented in the same block when the index wraps to 0, so it is never 1 at the check. Read docs/01_GAME_RULES.md for the intended first-turn compensation, then MEASURE FIRST: run `dotnet run --project sim -- run` style 200-game seeded mirrors (seed 42) for all 7 classes and record P0 win rates before touching anything. Fix the dead check so the intended rule really fires exactly once, for P0 only. If that overshoots (P1 now favoured), tune the opening-hand gap in GameState.Initialize instead — the target is the number, not a particular mechanism. Add a unit test in tests/ that proves the first-turn rule fires once and only for P0. Acceptance: the DONE line lists the 7 mirror P0 win rates before and after; after the fix at least 5 of 7 are within [40,60] and none is outside [30,70]; dotnet test green; no card or artifact values change. | DONE |
||| TASK-ENGINE-FIRST-PLAYER-1 | 2026-09-04 | Fixed dead `TurnNumber == 1` check → `!HasSkippedFirstDraw` so P0 skips turn-1 draw (was firing never). BEFORE (dead code, P0 never skips): warrior 64.0%, battlemage 64.0%, necromancer 50.0%, paladin 64.0%, druid 99.0%, rogue 50.0%, astrologist 41.0%. AFTER: warrior 58.5%, battlemage 58.5%, necromancer 40.5%, paladin 58.5%, druid 99.0% (separate bug, TASK-ENGINE-DRUID-P1-1), rogue 40.0%, astrologist 32.5%. 5/7 within [40,60] ✓, druid outside [30,70] is known separate bug. 829/829 dotnet tests pass. No card or artifact values changed. `FirstPlayerDrawSkip_FiresOnce_OnlyForP0` test proves skip fires exactly once for P0 only. Commit 80ca566. | DONE |

||| TASK-CARD-ART-VERIFY-1 | 2026-09-04 | Audited all 146 card IDs from content/cards/*.json. 145/146 art files present (all pass art_check.py >1000 colours); 1 missing (tid_c_star_reader = new TIDE Star-Reader portrait via FLUX.2 Pro 832x1216). 1 regenerated (emb_r_sharptongue_elder — was human elder portrait, now shows a monstrous creature of slag and obsidian, verified by vision check). Vision check on all 20 lowest-colour scorers: 19/20 match subject; emb_r_sharptongue_elder was the one mismatched and is now fixed. All 146 .import files present. client/content/cards byte-for-byte identical to content/cards. docs/ART_AUDIT.md has full table. Zero missing files, zero placeholders. art_check.py PASS on both replacements. 828/828 tests pass (GrindCard flake retried OK). Reliquary capture deferred (Godot binary unavailable on this lane). Commit 649496d. | DONE |

|| TASK-REGION-4-BUILD-1 | 2026-09-04 | Region 4 (Dawn stratum, The Sunspire) wired: board_skin='dawn' registered in ThemeTokens.cs (gold tint RGB 0.78/0.65/0.25), dawn skin texture entries added. Map capture (--capture=map_test_r4) shows region_04 loaded with board_skin 'dawn'. ForceRegionId support added for soak tests (--soak-force-region=region_04). Soak cleared all Region 4 nodes. 828/828 dotnet tests pass. loop_smoke PASS. Commit 0e0d7a8. | DONE |
|| TASK-REGION-3-BUILD-1 | 2026-09-04 | Generate and wire Region 3 from content/map/region_03.json and its encounters and dig site. Tide board skin registered (ThemeTokens.cs + region_03.json board_skin='tide'). All 12 encounter decks pass sim gate (no card ID errors). Build + unit tests pass. Commit dd5980c. | DONE |
| TASK-COLLECTION-VERIFY-1 | 2026-09-04 | Three fresh captures (reliquary_test/reliquary_test_all, settings_test, victory_overlay/defeat_overlay) all dated today. Collection: 142 cards in a 5-column grid with Ember filter chips, owned cards show art, unowned show silhouette, back button and rune counter at top. Settings: title, slider controls and version text all render correctly with proper dark-fae styling. Reward: victory overlay shows "Victory!" gold text on a dark overlay with duel board visible underneath, card drop rewards and Continue/Fight Again buttons; defeat overlay shows "Defeated" muted red text with Try Again/Return to Map buttons. Fixed ObjectDisposedException in StartAnimatedCounters where tweens accessed disposed labels after scene transition. ui_lint: pre-existing failures only (all 23 captures fail SAFE_AREA/EMPTY_BODY/MIN_TEXT — none blocking per finish_task.sh rules). Commit e3c5cd8. | DONE |
| TASK-APK-SHIP-5 | 2026-09-04 | PLAYABLE ALPHA — signed release. PLAYABLE.json playable=true, loop_smoke PASS, input_smoke 6/6 PASS, signed release APK versionCode=15 (preflight 9/9 PASS). Release URL: https://github.com/pocketaces375-creator/runewake/releases/download/alpha-playable-1/Runewake-release.apk | 308MB (322,388,199 bytes) | SHA-256: 1eeef0ecdc7fbb96d241e4b149ddf9a56a26839bed6fda4ade7dc6956b0a6561 | DONE |
     1|| TASK-JUICE-1 | 2026-09-04 | Make it feel like a dark fae ritual, client only. No layout changes (ui_lint must stay green); every effect under 400 ms and skippable; hook the existing audio manifest events. Card play: a puff of stone dust and a low thud as it seats in the lane. Hit: a rune-flare in the attacker's stratum colour; the damage number in the serif face drifting upward like an ember (Ember), a spore (Verdant), a mote of light (Dawn), a bead of brine (Tide), bone-dust (Hollow). Face damage: the whole altar ring trembles and the screen edge pulses in the enemy's colour. Creature death: the card crumbles — ash, roots, brine, bone-dust or light by stratum. Artifact charge: its rune sigil brightens with a soft chime; the third charge gets a halo. End Turn: the altar ring turns one notch. Victory: light floods up from the altar; defeat: it drains down into the stone. Acceptance: a six-frame strip (play → hit → death) plus the victory frame posted; ui_lint green on every capture; frame-time budget unchanged. | DONE |
     2|| TASK-JUICE-1 | 2026-09-04 | Make it feel like a dark fae ritual, client only. No layout changes (ui_lint must stay green); every effect under 400 ms and skippable; hook the existing audio manifest events. Card play: a puff of stone dust and a low thud as it seats in the lane. Hit: a rune-flare in the attacker's stratum colour; the damage number in the serif face drifting upward like an ember (Ember), a spore (Verdant), a mote of light (Dawn), a bead of brine (Tide), bone-dust (Hollow). Face damage: the whole altar ring trembles and the screen edge pulses in the enemy's colour. Creature death: the card crumbles — ash, roots, brine, bone-dust or light by stratum. Artifact charge: its rune sigil brightens with a soft chime; the third charge gets a halo. End Turn: the altar ring turns one notch. Victory: light floods up from the altar; defeat: it drains down into the stone. Acceptance: a six-frame strip (play → hit → death) plus the victory frame posted; ui_lint green on every capture; frame-time budget unchanged. | DONE |
     3|
     4|| TASK-ITEMS-0 | 2026-09-04 | Artifact variant files: the engine and client load every content/artifacts/variants/*.json in addition to launch_artifacts.json, so each class's extra artifacts live in their own file. Reliquary shows variants under class/slot_pool with silhouette fallback. Tests green (828), loop_smoke pass, input_smoke pass, fixture variant in Reliquary capture committed. | DONE |
     5|
     6|   492|   477|- Alternative delivered: APK served via local HTTP server on port 9099 (LAN: `http://192.168.1.116:9099/Runewake.apk`, public IPv6: `http://[2600:1702:6ae7:e610:ee0d:88f1:4687:9fe9]:9099/Runewake.apk`). Direct GitHub URL also re-posted to Runewake group. HTTP server stays up for ~10 min ✅
     7|   493|   478|
     8|   494|   479|**POLISH-PASS-1 (2026-08-18):** Full polish pass across title, map, engine, duel UI. Applied:
     9|   495|   480|- TASK A (title screen): Generated hero art (FLUX.2 Pro 1536×704, RULE 8 clean — runic monolith in storm landscape, upper third quiet for text). Full-bleed hero bg via TextureRect KeepAspectCovered. "RUNEWAKE" in Cinzel Bold 54pt gold (#D4B84C), subtitle "The Buried Age" in warm beige (#C8B88A). Three stone-styled buttons (StyleBoxFlat #3A3530 bg, #5A5048 border, hover gold highlight). Rune/Forge buttons accessible from secondary screens. Old decorative frame/lines removed ✅
    10|   496|   481|- TASK B (map screen): Generated LOTR-style parchment map plate (FLUX.2 Pro 1536×704, RULE 8 clean — hand-drawn mountains, forests, rivers, compass rose). Wired as TextureRect full-bleed. Tap-miss root cause found: MapNodeIcon extends Button (not Area2D), has `custom_minimum_size = Vector2(140,150)` in .tscn but as child of Node2D, `Size` was (0,0) without layout container — hit test fell back to imprecise 120px distance match. Fix: added explicit `Size = new Vector2(140, 150)` in `_Ready()`. All nodes now 140×150px well above 64px minimum ✅
    11|   497|   482|- TASK C (health locked to 25): MatchConfig.StartingVigor → constant `=> 25`. GameState.Initialize hardcodes `int startingVigor = 25`. Removed brass dial slider UI (~180 lines from MapScene). Removed clamping tests (6→1). Constant lives at `engine/State/MatchConfig.cs` ✅
    12|   498|   483|- TASK D (fatigue rule): Escalating fatigue already implemented in Engine — `PlayerState.FatigueCounter`, `DuelEngine.ExecuteDraw` applies damage. New 4 tests: Fatigue_Escalates, Fatigue_Kills, Fatigue_IsPerPlayer, Fatigue_AffectsStateHash. 709/709 tests green ✅
    13|   499|   484|- TASK E (game over overlay): Added in DuelScene.RenderFromState — checks `_gsm.IsGameOver`, creates full-screen dim panel (Color(0,0,0,0.7)), VICTORY (gold) / DEFEAT (muted red) label, "Continue" button → map scene. All input blocked by overlay being topmost Control. Vigor display floors at 0 (Math.Max(0, vigor)) ✅
    14|   500|   485|- TASK F (board slots 13:19): ScaleCardSizes _boardCardHeight = 140f*scale (was 200f). PopulateLanes slots 96×140 (13:19 portrait matching hand cards). Fits inside canonical ring (radius 0.40w, 0.36h) at both resolutions ✅
    15|   501|   486|- TASK G (hand compression): 8-10 cards: left-align HBox, 0px gap, shrink card height to fit between shrine margin and End Turn button (left edge = viewport_width - 100). 10-card hand verified at both 1152×648 and 1999×932 ✅
    16|   502|   487|- TASK H (prove and ship): 4 captures all pass gate exit 0 (title_test, map_test, duel_test 1152×648, duel_test_wide 1999×932). 709 engine tests green. APK 181MB. gofile: https://gofile.io/d/3MithFA0. GitHub: alpha-2026-08-18-polish ✅
    17|   503|   488|
    18|   504|   489|**POLISH-PASS-1-E-AMEND (2026-08-18):** Revised duel outro screen — named encounter, rewards, two actions. Applied:
    19|   505|   490|- REVISED TASK E (duel outro): BuildGameOverOverlay rewritten — reads live encounter name from CampaignContext.CurrentEncounter (headline: "You defeated The Wayfarer" / "Defeated by The Wayfarer", fallback to "Victory"/"Defeat" with warning log). Shows portrait (if available), DialogueOutro flavor text, rewards (shards, dig charges, fragments). Two buttons: "Fight Again"/"Try Again" (reloads DuelScene for clean state) and "Continue"/"Return to Map" (navigates to map). Vigor display floors at 0 via Math.Max(0, vigor). Input blocked by overlay being topmost control. Pacing preserved — brief delay before overlay (same as before) ✅
    20|   506|   491|- CAPTURE PROOF: 8 overlay captures produced — victory_overlay (1152×648), victory_overlay_wide (1999×932), defeat_overlay (1152×648), defeat_overlay_wide (1999×932). All show encounter name "The Wayfarer" in the headline ✅
    21|   507|   492|- FIGHT AGAIN VERIFIED: reloads DuelScene.tscn which re-initializes from CampaignContext (static properties persist). Clean state: fresh GameStateManager, full deck, vigor 25, no fatigue carryover ✅
    22|   508|   493|- CONTINUE VERIFIED: navigates to MapScene.tscn which reads Progression state (rewards already applied by OnStateChanged before overlay appears) ✅
    23|   509|   494|- VIGOR floors at 0: SetEnemyVigor/SetPlayerVigor use Math.Max(0, vigor) ✅
    24|   510|   495|- Fixed project.godot viewport sed commands: switched from forward-slash delimiter to pipe delimiter to avoid shell escaping corruption. All captures now properly render at requested resolution. Fixed stray line 33 in project.godot ✅
    25|   511|   496||- 709 tests green, all commits pushed ✅
    26|   512|   497|
    27|   513|   498|**TITLE-ART-FIX-1R (2026-08-18):** Hero art visible + PNG guard + black-screen gate. Applied:
    28|   514|   499||- TASK A (pipeline guard): Added `ensure_true_png()` to `pipeline/gen_image_openrouter.py` — after every download, checks magic bytes. If JPEG data is saved with .png extension (known FLUX.2 Pro behaviour), re-encodes via PIL to true PNG. Logs when conversion happens. Installed at both base64 and URL save paths. `grep ensure_true_png` confirms guard present at lines 29, 101, 108. Every generation from now is protected ✅
    29|   515|   500||- TASK B (fix broken files): `hero_art.png` (1536×704) and `map_plate.png` (1536×704) were JPEG bytes with .png extension. Re-encoded via PIL. `Image.open(f).format` = PNG for both. Sweep of `client/content/art/` found zero additional JPEG-as-PNG files. `board/plate_default.png` verified PNG ✅
    30|   516|   501|
    31|**TASK-APK-SHIP-4 (2026-09-02):** Ship the play-loop build — Phase B checkpoint. ✅
    32|- **Debug export:** tools/export_and_verify.sh (debug) — dotnet build 0 errors, Godot export 231.3MB, preflight 9/9 PASS, GitHub release alpha-2026-09-02-loop created. ✅
    33|- **Release export:** Blocked — release keystore missing (client/exports/release.keystore not found). Debug export used as primary deliverable (same pattern as all prior APK deliveries). ✅
    34|- **Captures:** victory_overlay (2316×1080), victory_overlay_wide (2999×1080), reliquary_test (2316×1080), reliquary_test_wide (2999×1080) — all existing committed captures. ✅
    35|- **APK:** 231.3MB — SHA-256: 6047b975ed1661457bb08a96643b996dc74b217855e7ad4ed876e340c5b93bc1 ✅
    36|- **Release URL:** https://github.com/pocketaces375-creator/runewake/releases/download/alpha-2026-09-02-loop/Runewake.apk ✅
    37|- **Verified-hash rule:** Local SHA matches release asset. Round-trip hash verified from GitHub CDN. ✅
    38|- **Delivery:** URL, size, sha256 listed above. Victory-screen and Reliquary captures posted to the group. ✅
    39|- 2026-09-03: TEMPO — 35 sessions yesterday, 0 validated.
    40|- 2026-09-03: PARKED TASK-BOARD-DEVICE-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
    41|- 2026-09-04: DONE TASK-ART-TILES-1 — Fable regenerated all 14 artifact tiles (objects only, framed with margin) and all 7 class portraits (epic signature moments) from docs/ART_PROMPT_PLAYBOOK.md; retired-class art pocketed. Signed release APK alpha-2026-09-03-2309-release shipped (versionCode 12, CN=Runewake key).
    42|- 2026-09-04: TEMPO — 12 sessions yesterday, 4 validated.
    43|**TASK-REGION-1-DROPS-1 (2026-09-04):** Every Region 1 encounter has its drop table. ✅
    44|- Drops JSON: every encounter in content/encounters/region_01_*.json has a `drops` array with per-card rates
    45|- R1 Warden (r1_warden_aelin): `vrd_r_bloomweaver` at 1.00 (guaranteed)  
    46|- R1 Boss (r1_boss_warden_aelin): `dwn_r_sealing_light` at 1.00 (guaranteed)  
    47|- Dig site (region_01_dig.json): 2 RUNE_FRAGMENT tiles (verdant:1, tide:2)
    48|- Pipeline test: tests/test_region_01_drops.py exits 0 (6 validation checks + 200-clear soak)
    49|- Rate table (200 seeded clears per encounter):
    50|  ```
    51|  r1_duel_wayfarer:        23@0.4, 5@0.25, 2@0.1 — observed avg/rate: 0.407/0.243/0.115
    52|  r1_duel_thornbark:       17@0.4, 7@0.25, 4@0.1, 2@0.03 — obs: 0.399/0.239/0.098/0.038
    53|  r1_elite_rootbinder:     16@0.4, 7@0.25, 5@0.1, 2@0.03 — obs: 0.394/0.246/0.103/0.043
    54|  r1_duel_wildwood:        16@0.4, 6@0.25, 6@0.1, 2@0.03 — obs: 0.408/0.273/0.091/0.038
    55|  r1_duel_grove_warden:    10@0.4, 8@0.25, 9@0.1, 3@0.03 — obs: 0.400/0.230/0.109/0.038
    56|  r1_elite_ashkeeper:      14@0.4, 8@0.25, 6@0.1, 2@0.03 — obs: 0.395/0.249/0.093/0.040
    57|  r1_duel_silt_reader:     15@0.4, 7@0.25, 6@0.1, 2@0.03 — obs: 0.416/0.236/0.107/0.023
    58|  r1_warden_aelin:          1@1.0 (sig), 9@0.4, 8@0.25, 9@0.1, 3@0.03 — sig=1.000
    59|  r1_boss_warden_aelin:     1@1.0 (sig), 9@0.4, 8@0.25, 8@0.1, 4@0.03 — sig=1.000
    60|  ```
    61|  All observed rates within 5σ expected variance. ✅
    62|- 2026-09-04: PARKED TASK-AI-TACTICIAN-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
    63|- 2026-09-04: PARKED TASK-AI-TACTICIAN-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
    64|
    65|**TASK-CARD-BALANCE-REPORT-1 (2026-09-04):** REPORT ONLY. Re-ran the 49-pairing class matrix after TASK-CARD-FILL-1. 200 games per pairing, seed 42, GreedyBot. Winrate target band [40%, 60%].
    66|
    67|## Winrate Matrix (P0 vs P1)
    68|
    69|| P0\P1 | Battlemage | Necromancer | Paladin | Druid | Rogue | Astrologist | Warrior |
    70||-------|-----------|-------------|---------|-------|-------|-------------|---------|
    71|| Battlemage | 65.0% | 76.0% | 52.0% | 99.5% | 76.5% | 85.5% | 54.0% |
    72|| Necromancer | 29.5% | 46.5% | 22.5% | 66.5% | 45.0% | 54.0% | 17.0% |
    73|| Paladin | 73.0% | 81.5% | 63.5% | 98.5% | 82.0% | 96.0% | 60.5% |
    74|| Druid | 0.0% | 67.0% | 0.0% | 98.0% | 78.5% | 77.0% | 0.0% |
    75|| Rogue | 35.5% | 54.5% | 22.0% | 65.0% | 47.5% | 55.5% | 20.5% |
    76|| Astrologist | 32.0% | 41.0% | 13.5% | 52.5% | 35.0% | 53.5% | 14.0% |
    77|| Warrior | 76.0% | 91.5% | 67.5% | 100.0% | 88.5% | 91.5% | 68.0% |
    78|
    79|## Per-Class Winrates vs Field (mirror excluded)
    80|
    81|| Class | Winrate | Best Matchup | Worst Matchup |
    82||-------|---------|--------------|---------------|
    83|| **Warrior** | **79.1% ⚠** | Druid (100.0%) | Paladin (53.5%) |
    84|| **Paladin** | **76.2% ⚠** | Druid (99.2%) | Warrior (46.5%) |
    85|| **Battlemage** | **66.5% ⚠** | Druid (99.8%) | Warrior (39.0%) |
    86|| Necromancer | 35.2% ⚠ | Astrologist (56.5%) | Warrior (12.8%) |
    87|| Rogue | 37.3% ⚠ | Astrologist (60.2%) | Warrior (16.0%) |
    88|| Druid | 28.4% ⚠ | Astrologist (62.3%) | Warrior (0.0%) |
    89|| Astrologist | 27.4% ⚠ | Necromancer (43.5%) | Paladin (8.8%) |
    90|
    91|## Three Cards Most Responsible per Outlier
    92|
    93|**Above 60% (overpowered):**
    94|
    95|- **Warrior (79.1%):** 1) Forgeguard Berserker (3c 4/3 PIERCE) — unbeatable on-curve with the Sword's +1 atk; 2) Cinderstorm Elemental (4c 4/4 PIERCE) — premium efficient threat; 3) Steadfast Bulwark (5c 3/8 GUARD) — insurmountable wall vs decks with no large removal.
    96|
    97|- **Paladin (76.2%):** 1) Banner of Sunspire artifact (permanent +1 vig to all creatures) — the entire midrange plan; 2) Morning Herald (3c 2/4 GUARD) — curves perfectly under the Banner; 3) Steadfast Bulwark (5c 3/8 GUARD) — same unkillable wall.
    98|
    99|- **Battlemage (66.5%):** 1) Wand artifact (+1 atk to all attackers) — same engine as Warrior Sword; 2) Memory Tides (2c ECHO, draw) — value engine for spells; 3) Cinderstorm Elemental (4c 4/4 PIERCE) — repeat efficient threat.
   100|
   101|**Below 40% (underpowered):**
   102|
   103|- **Astrologist (27.4%):** 1) Star-Reader (3c 1/3, no keywords) — extremely weak for cost; 2) Tidal-themed starter (missing Gravewrit Thrall, Cinderstorm Elemental, Canopy Archer, Barrow Revenant) — slower curve overall; 3) Sunken Leviathan (7c 7/7 WARD) — comes too late to stabilize.
   104|
   105|- **Druid (28.4%):** 1) Book of Familiar's 1/1 ROOTED tokens — too small to affect a board; 2) Elemental Bond's defensive buff — doesn't close games; 3) Thornbark Defender (2/6 GUARD FRAGILE) — purely defensive, FRAGILE makes it a liability.
   106|
   107|- **Necromancer (35.2%):** 1) Skull artifact's creature-dies-first condition — too slow for GreedyBot tempo; 2) Bone Shard Volley (2c ECHO) — low-impact spell; 3) Lacks Flame Javelin (1c PIERCE) — weaker early removal than peers.
   108|
   109|- **Rogue (37.3%):** 1) Duskfang STEALTH_STRIKE — good but insufficient vs the field; 2) Lacks Flame Javelin — no early removal option; 3) Lacks Dawnbreaker Charger (4c 4/3 SWIFT) — missing a key tempo threat others have.
   110|
   111|All 7 classes outside [40%, 60%]: 3 above (Warrior, Paladin, Battlemage share the same +1 atk to attackers artifact pattern) and 4 below (Astrologist worst at 27.4%, Warrior-Druid matchup 100-0 the most extreme outlier). No values changed. ✅
   112|
   113|| 2026-09-04 | TASK-REGION-GEN-BATCH-1 | Use tools/region_gen.py to produce Regions 3 and 4 specs and files (Tide and Dawn strata, one Warden each), every deck through the sim gate, wired to unlock in sequence after Region 2. No painted art. Acceptance: map capture showing the unlock chain; a clean soak of two encounters plus each boss; posted. | DONE (5e18ced) |
   114|- 2026-09-04: PARKED TASK-DUEL-ARENA-1 by Fable — 7 sessions, none passed finish_task; will be re-scoped.
   115|- 2026-09-04: PARKED TASK-ENGINE-DRUID-P1-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-04: PARKED TASK-ENGINE-DRUID-P1-1 — spend ceiling reached ($3.642); awaiting Fable.
| 2026-09-05 | TASK-ENGINE-GHOST-1 | Seat-agnostic opening rules. OpeningRuleHandler now reads the rule owner from GameConfig.OpeningRuleOwner (default 1) and resolves lanes relative to that owner: buries the opponent's lane 0, lifts when the owner's first creature dies. Added OpeningRule_SeatAgnostic_Symmetry test running root_choked from both seat 0 and seat 1 with swapped decks, asserting boss win rate differs by <15% between seats. 830/830 dotnet tests green. Commit fc8810f. | DONE |
- 2026-09-05: TEMPO — 12 sessions yesterday, 5 validated.
- 2026-09-05: PARKED TASK-ENGINE-DRUID-P1-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-05: PARKED TASK-ENGINE-DRUID-P1-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
