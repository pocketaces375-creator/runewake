# Card art variety (FABLE-021, 2026-09-23)

## The problem

The current card paintings score **17–32 out of 100** for uniqueness (`tools/art_director.py score`). Nearly all of them show one figure, full length, at eye level, in the centre of the frame, against the stratum's single backdrop.

The cause is the prompt, not the generator:

- Every prompt was built as `subject + the same style paragraph + the one backdrop for the stratum + the one camera line`.
- The style paragraph asked for a "single grounded focal subject … Renaissance tableau".

A longer word bank does not fix this. `tools/card_art_prompt.py` (FABLE-020) proved that: it is still a fill-in template, and it is kept only as an offline fallback.

## The fix: an art director with a memory (`tools/art_director.py`)

1. **Invent.** A strong text model pitches 5 concepts per card, using the model set in `pipeline/config.yaml`.
   - The pitches vary the number of figures (none, one, two, a few, a crowd), where the camera is, the moment in the story, and one surprising twist.
   - The model is told what the game already has too much of, with percentages from the ledger.
   - It also gets 4 random "sparks" from a bank of about 80. It is free to ignore them.
2. **Score.** Every concept is compared with every card in the ledger on 12 fields: shot, viewpoint, placement, subject count, moment, setting, weather, light, action, subjects, mood and twist.
   - The most original concept that still reads as the card wins.
   - If the best one is too close to an existing card, the model is told which card and why, and pitches again.
3. **Paint.** The winning concept goes out with a short, fixed style tail: oil painting, chiaroscuro, the stratum palette with a rotating lead colour. Each card gets 1 candidate by default (`--candidates 2` for a choice; `--models flux,gemini` splits candidates across generators). Every `render` is a **run**, stamped on each card it paints, and it prints the bill up front: "7 card(s) × 1 candidate(s) = 7 painting(s)".
4. **Check.** Each candidate is scored against every other card painting on layout, colour and where the subject sits. `bootstrap --vision` also has a vision model catalogue what is actually in each painting. The most unique candidate is proposed.
5. **Approve.** Nothing ships until a person picks.
   - `sheet` builds review pages (4 cards a page: old painting, candidates, scores, the pitch) for the **latest run only**, and copies them with a README of every prompt into `artifacts/art_review/<run>/` so they can be committed and read from the repo. `--all` shows everything ever rendered; `--run ID` picks a run.
   - `approve <id> <n>` installs the pick.
6. **Remember.** The ledger (`~/runewake_art_archive/art_ledger.json`, outside the repo so resets cannot wipe it; `export-ledger` copies it to `pipeline/art_ledger.json` for committing) keeps every concept and painting. Each new card makes the next one work harder to be different, so the art stays fresh at card 500 as well as card 50.

## Commands

```
python3 tools/art_director.py bootstrap              # once: remember the existing art (free; --vision is better but uses the API)
python3 tools/art_director.py plan --worst 6         # pitch concepts for the 6 least unique cards
python3 tools/art_director.py render --planned       # 1 painting each (--candidates 2 for a choice)
python3 tools/art_director.py sheet                  # the LAST run only, as pages of 4 → artifacts/art_review/<run>/ (commit it)
python3 tools/art_director.py sheet --all            # every rendered card ever (the old behaviour)
python3 tools/art_director.py approve <card_id> <n>  # install the chosen one
python3 tools/art_director.py score                  # uniqueness of every card vs all others
```

Add `--mock` before the command to exercise the whole flow offline, with no API calls.
