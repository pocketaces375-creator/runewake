# RUNEWAKE — FINISH THE LAST 15. (Standing brief for every worker session.)

You are finishing Runewake: The Buried Age. 148 of 163 pieces are built. The game
is playable end to end. Your job is the remaining 15, one at a time, each one
actually DONE — not "committed", not "compiles", DONE as defined below.

## How to work
- Take ONE task from TASKS_QUEUE.md (the first `- [ ] TASK-…` not excluded below).
  Do not read the whole queue, HERMES_STATUS.md, or docs/archive. Read only the
  task text, the files it names, and this brief.
- Finish with `bash tools/finish_task.sh <TASK-ID>`. It builds, tests, captures,
  and runs the gates. If it says the screen is broken, FIX THE SCREEN. Never mark
  a task done by hand, never edit the gate to make it pass.
- One task per session. When finish_task is green, stop. The next session takes
  the next task.
- Two failed attempts on the same task → write WHY in one paragraph at the top
  of the task in TASKS_QUEUE.md and move on. Do not loop.
- Never rewrite the crontab. Never touch /tmp/runewake_build.lock. Never start
  another lane. Never delete files.

## Definition of DONE (finish_task enforces this — it is not optional)
1. Engine + client compile, all 855+ tests pass.
2. `tools/label_fit.py` passes: no text renders outside its own card.
3. `tools/loop_smoke.sh` passes: title → choose path → map → duel → result, no crash.
4. A fresh capture exists for every screen you touched.
5. Nothing loads a missing resource ("Failed loading resource" in the run = not done).

## The 15 (skip the two marked FABLE — those need image generation and are his)
- TASK-TITLE-ANIM-1        the rune wheel on the title screen rotates forever, smoothly, 60fps on a phone
- TASK-ITEMS-WARRIOR-1     Warrior class item cards (content JSON + tests), balanced to the pack next to it
- TASK-ITEMS-BATTLEMAGE-1  same for Battlemage
- TASK-ITEMS-NECROMANCER-1 same for Necromancer
- TASK-ITEMS-PALADIN-1     same for Paladin
- TASK-ITEMS-DRUID-1       same for Druid (turn triggers belong to the player whose turn it is — see ARTIFACT_RULINGS G1a)
- TASK-ITEMS-ROGUE-1       same for Rogue
- TASK-TUNE-AURAS-1        aura numbers tuned so no single aura decides a duel; run sim, report win rates
- TASK-TUNE-WHISPER-1      same for whisper effects
- TASK-AI-TACTICIAN-1      enemy AI plays lanes with intent: blocks lethal, trades up, holds removal
- TASK-DUEL-ARENA-1        duel arena layout: every card, chip and label inside its plate at 1080p AND at a 720p phone
- TASK-OPS-TRIM-1          strip dead scripts and stale docs; nothing the game or gates depend on may break
- TASK-ENGINE-DRUID-P1-1   verify first: Elemental Bond's recurring ROOTED grant is already fixed — if tests prove it, close it
- TASK-CLASS-PORTRAITS-1   FABLE — skip
- TASK-ART-ICONS-1         FABLE — skip

## The look (anything with a screen must obey this)
Nothing floats. Every piece of text sits on a physical object: a name is cut into
a pale stone plaque; stats sit in red/green keyline boxes on a band of packed
soil; cost is struck on a dark disc with a thin gold ring. The card frame is the
Root-Bound stone border at 7% of card width — carved, weathered, runes and vines.
Names are ONE line always (auto-shrink, then ellipsis). Gold is a hairline and two
numerals, never a fill. No glow, no bloom, no drop shadow as decoration. No flat
rectangle of colour with text on it. If you are unsure, open
artifacts/captures/duel_test.png — the cards there are the standard.

## Report
When finish_task is green, post ONE line: `DONE TASK-ID — what changed — capture name`.
Do not post progress. Do not post "still working". Silence means working.

