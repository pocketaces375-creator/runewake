| TASK-MAP-PANEL-1 | 2026-09-08 | Fix node info panel: name shown once as title, rewards row hidden when empty, disabled Go button legible, Rune Page button no longer clipped by overlapping anchors | DONE |
| TASK-ITEMS-DRUID-2 | 2026-09-08 | Four more Druid artifacts (two per slot) in content/artifacts/variants/druid.json, same rules, format and art bar. Use the slot_pool values already present in that file. SLOT A: "Thornwake Stave" — passive: when an enemy creature attacks one of yours, it takes 1 damage; +1 charge whenever your creature is attacked; full (3): deal 1 damage to every enemy creature. SLOT A: "Rootbound Crook" — passive: your creatures with full vigor have +1/+0; +1 charge whenever a creature returns to full vigor; full (3): a friendly creature grows +2/+2 permanently. SLOT B: "Mossheart Totem" — passive: at the end of your turn, a random damaged friendly creature restores 1 vigor; +1 charge per end of turn with a damaged creature; full (3): summon a 1/4 Guard sapling. SLOT B: "Sunhollow Idol" — passive: the first creature you play each turn costs 1 less; +1 charge whenever you play a creature; full (3): draw two creature cards. Each: id artf_druid_<snake_name>, class "druid". | DONE |
| TASK-ITEMS-DRUID-2 | 2026-09-08 | Thornwake Stave, Rootbound Crook, Mossheart Totem, Sunhollow Idol in content/artifacts/variants/druid.json | DONE |
| TASK-ITEMS-PALADIN-2 | 2026-09-08 | Four more Paladin artifacts (two per slot) in content/artifacts/variants/paladin.json, alongside the four already there from TASK-ITEMS-PALADIN-1. Same rules, format and art bar. Use the existing slot_pool values in that file. HAMMER: "Oathkeeper's Weight" — passive: your Guard creatures cannot be reduced below 1 vigor by a single attack; +1 charge whenever a Guard creature survives an attack; full (3): a friendly creature gains Guard and +0/+2 permanently. HAMMER: "Dawnbreaker Maul" — passive: your first attack each turn deals +1 to a damaged creature; +1 charge whenever you attack a damaged creature; full (3): deal 4 damage to a creature that is already damaged. BANNER: "Vigil Standard" — passive: at the start of your turn, a damaged friendly creature restores 1 vigor; +1 charge per turn a friendly creature is damaged; full (3): restore all friendly creatures to full vigor. BANNER: "Sanctum Pennant" — passive: friendly creatures adjacent to your Guard creatures take 1 less damage; +1 charge whenever damage is prevented this way; full (3): friendly creatures take no damage until your next turn. Each: id artf_paladin_<snake_name>, class "paladin". | DONE |
| TASK-ITEMS-PALADIN-2 | 2026-09-08 | Four more Paladin artifacts (two per slot) in content/artifacts/variants/paladin.json, alongside the four already there from TASK-ITEMS-PALADIN-1. | DONE |
| TASK-ITEMS-NECROMANCER-2 | 2026-09-08 | Four more Necromancer artifacts (two per slot) in content/artifacts/variants/necromancer.json, same rules, format and art bar as TASK-ITEMS-BATTLEMAGE-2 above. Use the slot_pool values already present in that file. SLOT A: "Gravebind Censer" — passive: the first creature that dies each turn returns a rune to you; +1 charge whenever a creature dies; full (3): return a creature from your barrow to your hand. SLOT A: "Hollow Reliquary" — passive: your summoned creatures enter with +0/+1; +1 charge whenever you summon; full (3): summon a 2/2 husk that cannot attack the turn it arrives. SLOT B: "Pallid Wake Lantern" — passive: when a friendly creature dies, an adjacent friendly creature gains +1/+0 until end of turn; +1 charge per friendly death; full (3): all friendly creatures gain Pierce until your next turn. SLOT B: "Shroudspindle" — passive: at the end of your turn, if you control no creatures, draw a card; +1 charge per turn you end with an empty board; full (3): your opponent's next attack this round is redirected to a creature of your choice. Each: id artf_necromancer_<snake_name>, class "necromancer". | DONE |
| TASK-CAPTURE-COVERAGE-2 | 2026-09-08 | Three screens are never captured, so the gate cannot judge them (client/scripts/DebugCapture.cs and tools/regen_captures.sh ONLY). tools/visual_gate.py lists title_test, settings_test and reliquary_test_all in DEFAULT_SCREENS, but no capture file is ever produced for them, so they show up every run as coverage gaps. This is the narrow, concrete version of the task that was parked earlier — do only this: (a) add a capture mode for each of the three names to the --capture dispatch in DebugCapture.cs, following exactly the pattern the existing modes use (map_test / reliquary_test are the closest models to copy); (b) add the three modes to the MODES array in tools/regen_captures.sh so they are produced on every regen; (c) run tools/regen_captures.sh and confirm artifacts/captures/title_test.png, settings_test.png and reliquary_test_all.png all exist and are not solid black (a black capture means the scene never rendered — that is a failure, not a pass; slots_test.png is the existing example of this bug, do not copy it). Do NOT change visual_gate.py, do NOT change the screen list, and do NOT fix any rendering defects the new captures reveal — just make the screens capturable and report what the gate then says about them in the DONE line. Acceptance: build green; no NEW test failures (two-run rule); the three PNGs exist, are over 100 KB each, and `python3 tools/visual_gate.py` no longer reports a COVERAGE GAP for any of them. | DONE |
| TASK-CAPTURE-COVERAGE-2 | 2026-09-08 | Add title_test, settings_test, reliquary_test_all capture modes — coverage gaps closed | DONE |
| TASK-CHOOSE-PATH-2 | 2026-09-08 | Class select polish (client/scripts/ChooseYourPathScene.cs ONLY). From the gate on choose_path.png: (a) the class blurb is cut off at the bottom of its panel — the Battlemage text ends mid-sentence on "Saltmere,". Size the panel to its text, or scroll it; no class may lose the end of its description. (b) the carousel position dots below the class cards are partly hidden behind the BEGIN button. Move the dots or the button so neither covers the other. Acceptance: build green; no NEW test failures (two-run rule); regenerate captures; `python3 tools/visual_gate.py --only choose_path` reports 0 blocking and no longer lists (a) or (b). Before/after gate output in the DONE line. | DONE |
| TASK-CHOOSE-PATH-2 | 2026-09-08 | class blurb no longer clipped (textBlock Fill|Expand + ClipContents off), dots moved higher above BEGIN button | DONE |
| TASK-HAND-CARDNAME-1 | 2026-09-08 | Card names are clipped in hand (client/scripts/HandCard.cs ONLY). The gate found this on duel_test.png and duel_test_safe.png: the name "THE UNDYING ROOT OF THE FALL" is cut off at the bottom on several hand cards, and cut off on the LEFT edge on others in the safe-area capture. With a full hand the cards overlap enough that long names lose characters at both ends. Fix so that at a full ten-card hand, at both the normal and the safe-area window size, every card name is fully readable — shrink the font to fit, wrap to a second line, or ellipsise deliberately, but never silently clip mid-word. Acceptance: build green; no NEW test failures (two-run rule as above); regenerate captures; `python3 tools/visual_gate.py --only duel_test,duel_test_safe` no longer reports any card-name clipping. Before/after gate output in the DONE line. | DONE |
| TASK-HAND-CARDNAME-1 | 2026-09-08 | Fix card name clipping in hand — remove one-line-only block, allow two-line wrap for long names | DONE |
| TASK-ITEMS-DRUID-1 | 2026-09-08 | Four more Druid artifacts (Trikzos: druid is about DIFFERENT ELEMENTS and DIFFERENT CREATURES — rotate both across the four so each reads as its own idea, e.g. storm+stag, fen-water+toad, frost+moth, spore+boar) (two per slot, Book of familiar/Elemental bond) in content/artifacts/variants/druid.json, Fable's designs; sidegrades only, never strictly better than the base items. ART: follow docs/ART_PROMPT_PLAYBOOK.md exactly — the tile is the WEAPON OR RELIC ALONE (no people, faces or hands, ever), a visibly different object of that slot per item from the variation bank, no negative phrases; every tile must pass tools/art_check.py tile and the folder must pass art_check.py variety. AFTER: TASK-ITEMS-0. BOOK_OF_FAMILIAR: "Grimoire of Thorns" — passive: your Rooted creatures deal 1 damage to any creature that attacks them; +1 charge whenever a Rooted creature is attacked; full (3): all friendly Rooted creatures +1/+1 permanently. BOOK_OF_FAMILIAR: "Seedbook" — passive: end of your turn, if you played no creature, put a 0/2 Rooted Seed token in an empty friendly lane; +1 charge per Seed; full (3): your Seeds become 2/3 with Reach. ELEMENTAL_BOND: "Wild Pact" — on play, bond one friendly creature: +2/+0 and Swift; +1 charge whenever it deals damage; full (3): draw 1 per damage it dealt this turn (max 3). ELEMENTAL_BOND: "Grove Link" — on play, bond two friendly creatures: both +0/+1, and whenever one is healed the other heals the same; +1 charge whenever either survives combat; full (3): both +1/+1 and Guard permanently. Each: id artf_druid_<snake_name>, class "druid", slot_pool, name, one-line dark-fae flavor, DSL passive/trigger/ charges using only existing ops (add an op ONLY if truly missing, with its own test), a unit test that fires each effect, and tile art via pipeline/gen_image_openrouter.py (docs/ART_STYLE_SPEC.md style, 832x832, no text, no border) saved as client/content/art/artifacts/artf_druid_<snake_name>.webp with its .import. Acceptance: the 4 artifacts load and equip in a headless duel; the 5-duel soak passes with each equipped; tests green; post the 4 tiles and a plain "item → what it does" list to the group. | DONE |
| TASK-ITEMS-DRUID-1 | 2026-09-08 | 4 druid variant artifacts (Grimoire of Thorns, Seedbook, Wild Pact, Grove Link) with tile art and tests | DONE |
| TASK-CAPTURE-COVERAGE-1 | 2026-09-08 | Add title_test and settings_test capture modes | DONE |
| TASK-SKIP-IS-FAIL-1 | 2026-09-07 | Force attunement=99 in touch/input smoke tests, treat SKIP as FAIL in verdict | DONE |
| TASK-TOUCH-PLAY-1 | 2026-09-06 | THE GAME IS UNPLAYABLE ON A PHONE: cards cannot be tapped. The tap wiring exists (HandCard._GuiInput -> Pressed -> DuelScene.OnHandCardPressed; LaneSlot._GuiInput -> LaneTapped) and the mouse path passes the smoke test, so the failure is in the TOUCH path only. DO THIS: extend tools/input_smoke.sh (or the harness it drives) to play a card using ONLY InputEventScreenTouch — never InputEventMouseButton: touch-press+release on a hand card, then touch-press+release on an empty lane in row 1, then assert a creature is on the board. Print which handlers fired (HandCard._GuiInput, LaneSlot._GuiInput, OnHandCardPressed, TryPlayCard) so a failure names the step that did not run. THEN fix whatever that test exposes. Suspects, in order: (1) a child Control with MouseFilter=Stop over the card intercepting the touch before HandCard sees it — decorative children (CardPlate, its ColorRects and Labels, the art TextureRect) must be MouseFilterEnum.Ignore, only HandCard/LaneSlot themselves are Stop; (2) TapGuard's 250ms window swallowing the real press when touch and emulated mouse arrive in an order it does not expect; (3) play requiring a drag (_GetDragData/_DropData) which is unreliable under touch — tap-to-select then tap-to-lane must work with no drag at all. DONE: the touch-only test plays a card and passes in finish_task, and it stays in the gate so this can never regress. | DONE |
| TASK-ITEMS-WARRIOR-1 | 2026-09-06 | Four warrior variants: Executioner's Blade, Duelist's Edge, Tower Shield, Spiked Buckler | DONE |
| TASK-DUEL-ARENA-1 | 2026-09-06 | Ghost duels. Add a "Duel Arena" node on the map and a title-menu entry: pick any saved deck and fight an AI-piloted opponent drawn from the pool (all class starters, every encounter deck, and the Region 2 decks), seeded, with a win/loss ledger in the save and a Runes reward per win — 10 normally, 25 against a Warden deck. Acceptance: capture of the Arena picker and of one Arena victory; a headless soak of 5 Arena duels; posted. | DONE |
| TASK-DUEL-ARENA-1 | 2026-09-06 | Add Duel Arena title-menu entry and picker with seeded opponent pool, warden detection, win/loss ledger, RuneDust rewards, bonus card drops | DONE |
| TASK-AI-TACTICIAN-1 | 2026-09-06 | PARKED by Fable 2026-09-04: 4 failed sessions on a 65% bar; re-queue with a 55% bar once the gate is green. The opponent must stop feeding trades. Replace the greedy bot with a tactician: each turn enumerate candidate plays (card × lane, ritual targets) and attack sets, look one ply ahead, and score the result: face vigor for both sides weighted by how close either is to lethal; creature value = attack + vigor + keyword worth; favorable trades (kill without dying) rewarded; attacking into a blocker that kills you is only allowed when it clears the way to lethal; Guard lanes respected; artifact charge progress valued; hold a cheap card rather than dump the hand into a Guard wall. Deterministic under the match seed. Difficulty knob in encounter json: "ai": "greedy" | "tactician" — default tactician everywhere, bosses always tactician. Acceptance: tactician beats greedy at least 65% over 200 seeded mirrors, reported per class in plain words; the 5-duel soak and loop_smoke still pass; no shipped card value changes. | DONE |
| TASK-OPS-TRIM-1 | 2026-09-06 | Token efficiency. Every session reads TASKS_QUEUE.md (79KB, 765 lines, 87 closed tasks still inline) and often HERMES_STATUS.md (101KB). That is paid for on every single task, forever. (1) Move every "- [x]" task block out of TASKS_QUEUE.md into docs/TASKS_DONE.md (append, keep full text, newest last), leaving the five most recently closed in the queue for context. The queue keeps its header, the ANSWERS block, the "## Queue" line, the "New tasks MUST be added ABOVE..." anchor line and the PHASE markers exactly as they are. (2) Move HERMES_STATUS.md entries older than 7 days into docs/history/HERMES_STATUS_ARCHIVE.md (append, chronological). Keep the newest entries and the append protocol unchanged. (3) Rotate tools/foreman_cron.log and tools/inbox.log daily, keeping 3 days (logrotate-style rename in the scripts themselves, no cron dependency, no external tool). foreman_cron.log is 624KB right now. (4) Verify nothing else parses the moved content: grep the tools/ directory for TASKS_QUEUE and HERMES_STATUS readers and confirm find_top_task, progress_ping.sh, finish_task.sh and foreman.sh still behave identically. Acceptance: before/after byte sizes of both files in the report; find_top_task returns the same top task before and after; one full foreman iteration completes green after the trim. | DONE |
| TASK-OPS-TRIM-1 | 2026-09-06 | Trim TASKS_QUEUE.md (2 [x] moved to archive, 5 kept), add log rotation to foreman.sh and inbox_apply.sh, update archive | DONE |
| TASK-TITLE-ANIM-1 | 2026-09-06 | Make the title screen alive with a forever-rotating rune wheel overlay | DONE |
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
- 2026-09-06: TEMPO — 1 sessions yesterday, 2 validated.
- 2026-09-06: PARKED TASK-ENGINE-DRUID-P1-1 — spend ceiling reached ($1.583); awaiting Fable.
- 2026-09-06: PARKED TASK-ENGINE-DRUID-P1-1 — spend ceiling reached ($1.583); awaiting Fable.
- 2026-09-06: PARKED TASK-UI-READABLE-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-06: PARKED TASK-UI-READABLE-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-06: PARKED TASK-CAPTURE-COVERAGE-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-CAPTURE-COVERAGE-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-07: TEMPO — 12 sessions yesterday, 4 validated.
- 2026-09-07: PARKED TASK-ITEMS-BATTLEMAGE-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-DRUID-1 — spend ceiling reached ($1.957); awaiting Fable.
- 2026-09-07: PARKED TASK-MAP-PANEL-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-RELIQUARY-LAYOUT-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-RELIQUARY-LAYOUT-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-BATTLEMAGE-1 — spend ceiling reached ($1.897); awaiting Fable.
- 2026-09-07: PARKED TASK-MAP-PANEL-1 — spend ceiling reached ($1.855); awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-NECROMANCER-1 — spend ceiling reached ($1.837); awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-ROGUE-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-ROGUE-1 — spend ceiling reached ($1.536); awaiting Fable.
- 2026-09-07: PARKED TASK-MAP-PANEL-1 — spend ceiling reached ($1.855); awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-DRUID-1 — spend ceiling reached ($1.957); awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-BATTLEMAGE-1 — spend ceiling reached ($1.897); awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-NECROMANCER-1 — spend ceiling reached ($1.837); awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-ROGUE-1 — spend ceiling reached ($1.536); awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-PALADIN-2 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ENDGAME-OVERLAY-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ITEMS-PALADIN-2 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ENDGAME-OVERLAY-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ENDGAME-OVERLAY-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-HAND-CARDNAME-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-HAND-CARDNAME-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-ENDGAME-OVERLAY-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-HAND-CARDNAME-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-HAND-CARDNAME-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.

- 2026-09-07: DONE TASK-ENDGAME-OVERLAY-1 (Fable, direct). Root cause of three of the four defects was one line: the overlay's stone panel was `new Panel()`, which is not a container and does not measure its children, so it laid out 640x0 and its content spilled over a board the overlay only dims. PanelContainer + SizeFlags.Fill on the two autowrapping labels. Defeat headline no longer repeats DEFEATED. Turn banner widened and the HUD hidden at game over. Verified: build green, 883/883 tests, visual_gate 0 blocking on victory_overlay, defeat_overlay, duel_test.
- 2026-09-07: PARKED TASK-CHOOSE-PATH-2 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-CHOOSE-PATH-2 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-07: PARKED TASK-DUEL-HUD-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-DUEL-HUD-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-DUEL-HUD-1 — spend ceiling reached ($0.857); awaiting Fable.
- 2026-09-08: PARKED TASK-CAPTURE-COVERAGE-2 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-BATTLEMAGE-2 — spend ceiling reached ($0.847); awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-NECROMANCER-2 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-NECROMANCER-2 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-PALADIN-2 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-PALADIN-2 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-DRUID-2 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-DRUID-2 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-MAP-PANEL-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-MAP-PANEL-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-RELIQUARY-LAYOUT-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-RELIQUARY-LAYOUT-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ENGINE-DRUID-P1-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ENGINE-DRUID-P1-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-BATTLEMAGE-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-BATTLEMAGE-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-NECROMANCER-1 — failed 2 attempts, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-NECROMANCER-1 — 3 sessions without queue progress, auto-parked by foreman; awaiting Fable.
- 2026-09-08: PARKED TASK-DUEL-HUD-1 — spend ceiling reached ($0.857); awaiting Fable.
- 2026-09-08: PARKED TASK-ITEMS-BATTLEMAGE-2 — spend ceiling reached ($0.847); awaiting Fable.
