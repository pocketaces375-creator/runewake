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
