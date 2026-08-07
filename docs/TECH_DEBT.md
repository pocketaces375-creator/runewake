# Tech Debt / Known Issues

Items discovered during development that are out of scope for the current
ticket but should be tracked for later resolution.

---

## Python `test_generate.py` — 3 pre-existing failures

**Root cause:** Tests were written alongside P6-02 (Generate module) and the
prompt/SYSTEM_RULES text drifted during subsequent refinement. All 3 are in
`pipeline/tests/test_generate.py` and are **not** related to any later module.

### 1. `test_build_prompt_contains_seed_fields`

**Failure:** `assert "batch_id" in prompt` — the prompt uses human-readable
labels (`Batch:`, `Strata:`, `Count:`) rather than the raw JSON key names
(e.g. `batch_id`, `strata`). The test checks for `"batch_id"` but the actual
prompt says `"Batch:"`.

**Fix:** Change test to assert on the display labels, or change `build_prompt()`
to include raw field names.

### 2. `test_main_rejects_wrong_strata`

**Failure:** `assert len(rej_files) == 1` — got 2 reject files instead of 1.
The generate module requests 1 card per attempt (2 attempts), and both
attempts produce valid-strata cards (which get rejected for being the wrong
strata), so 2 reject files are written instead of the expected 1.

**Fix:** Either update the test to assert `len(rej_files) == 2` (correct
behaviour), or reduce retries in the test's mock config.

### 3. `test_system_rules_contains_constraints`

**Failure:** `assert "Creature cards MUST have attack and vigor" in SYSTEM_RULES`
— the actual `SYSTEM_RULES` string uses `"CREATURE cards MUST have attack and
vigor fields"` (uppercase CREATURE + includes "fields").

**Fix:** Update the test assertion to match the actual text verbatim:
`"CREATURE cards MUST have attack and vigor fields"`.

---

## API key resolution inconsistent across subprocess environments

**Root cause:** The pipeline modules (generate.py, art.py) read `OPENROUTER_API_KEY`
from `os.environ` or the `--api-key` argument. The Hermes agent runtime loads its
env vars (including `OPENROUTER_API_KEY`) from `~/.hermes/.env`, but terminal
subprocesses launched by `terminal()` tool calls do NOT inherit the agent's
environment — they run in a separate shell context that doesn't source
`~/.hermes/.env`.

This means any `subprocess.run()` call to a pipeline module from within the
agent's Python process (e.g. the orchestrator) will fail with 401 unless the
env var is explicitly sourced by a wrapping shell script.

**Fix:** Wrap pipeline runs in `pipeline/run_e2e.sh` which sources
`~/.hermes/.env` before invoking the Python orchestrator.

**Detection:** On failure, check if the error is `401 Missing Authentication
header`. If yes, and you ran via `terminal()` directly, use the shell wrapper
instead or source the env file first:
```bash
source ~/.hermes/.env && export OPENROUTER_API_KEY && python -m modules.generate ...
```

**Priority:** Medium — blocks every pipeline run that calls OpenRouter.

---

## Cost 10 — reachable but never tested in play

**Date flagged:** 2026-08-05
**Root cause:** The original expected formula (2.35×cost+0.9) made cost 10
mathematically impossible for any creature (expected(10)=24.40 > max base 22.50).
The v0.2 piecewise calibration (flattened slope 1.5 above cost 5) lowers
expected(10) to 20.15, making cost 10 reachable for the first time.

**What to watch:** Cost 10 cards have never been generated, played, or
simulated. The balance implications are unknown — a cost-10 card should be
game-ending, but the formula may not correctly value that. The auto-adjust
±2 cap can produce a cost-10 card (from a cost-8 or cost-9 original that was
overpowered and moved up). Monitor the simulation bridge (`pipeline/modules/
simulate.py`) for any cost-10 cards that pass score and flag them for manual
review until the first full set with a cost-10 card exists.

**Resolution path:** Once a full set with ≥1 cost-10 card exists, run 10k+ sim
games and check win-rate deltas for those cards. If they're under- or over-
performing, adjust the cost 10 expected value (currently 20.15) or the RELIC
band upper bound for high-cost cards.

---

## Engine tests encode implementation, not specification

**Date flagged:** 2026-08-05
**Root cause:** The engine tests (`TurnLoopTests.cs`, `GameState.Initialize`)
were written by reading the implementation code rather than the rule docs
(`docs/01_GAME_RULES.md`). When the code had a bug (P1 compensation applied
at Initialize AND via the normal Attune step, giving P1=2 on their first turn),
the tests asserted the wrong value because they'd been written using the same
mental model.

**Concrete examples:**
- `TurnLoopTests.AttunementRampsUpEachTurn` asserted P1 went 1→2 (wrong per
  §1: "starts with +1 Attunement on turn one" — the normal Attune step IS the
  compensation, giving P1=1 on their first turn)
- `TurnLoopTests.CreateGameState` manually set `P1.AttunementMax=1, P1.Attunement=1`
  to match the buggy Initialize, rather than testing Initialize's actual output
- `GameStateInitTests.Initialize_PlayerState_HasCorrectInitialValues` originally
  asserted P0=0, P1=1 (copying the old code)

**Fix:** Every test that covers a documented rule should cite the doc section it
tests in its doc comment. If a test doesn't have a doc reference, it was written
from the implementation — treat it as suspect.

**Priority:** Medium — until this is fixed, a test that passes can coexist with
a bug that matches it. The `GameStateInitTests` in `tests/State/` are the first
tests written with doc-section citations. Future tests should follow that pattern.

---

## Bot-vs-bot: P0 always wins (first-player advantage suspected)

**Date flagged:** 2026-08-06
**Root cause:** 3/3 batch-sim games and the full client render all ended with
Player 0 winning (P0 vigor ~24-25, P1 vigor -3). Both sides use GreedyBot with
identical 30-card decks. P0 always goes first in Initialize.

**What to watch:** The GreedyBot is deterministic (same seed → same actions) and
has no concept of tempo or card advantage — it picks the highest-scoring immediate
action. Pure first-player advantage from the Attunement compensation (+1 attune
for P1 on turn one) may not be enough to overcome the extra turn P0 gets.

**Resolution path:** Not a blocker. Flag for the balance pass once real curated
decks exist (Phase 4+). At that point run 10k+ games with variance in deck
composition to measure the real first-player win rate. If it exceeds 55%, add
a second attunement compensation for P1, or give P1 an extra card on the
opening draw. The fix goes in `docs/01_GAME_RULES.md` §1, then `GameState.Initialize`.

---

## Exported builds crash on filesystem path I/O

**Date flagged:** 2026-08-06
**Root cause:** `ProjectSettings.GlobalizePath("res://...")` returns an absolute
filesystem path that only exists in the editor. In exported builds, `res://`
resides inside the embedded PCK and is not accessible via `System.IO.File.*`
(`File.ReadAllText`, `File.Exists`, etc.). The engine `CardLoader.LoadPack(path)`,
`EncounterLoader.LoadPack(path)`, and all other `*Loader.LoadPack(path)` methods
use `File.ReadAllText(path)` — pure .NET, correct — but the client was passing
GlobalizePath'd paths to them.

All content loading from `res://content/` must go through `Godot.FileAccess.GetFileAsString("res://...")`,
then call `*Loader.LoadPackFromString(json)`. The `FromString` variants exist
for every loader.

**Files fixed in P3-02:**
- `scripts/DuelScene.cs` — `LoadCardPacks()` uses Godot FileAccess directly
- `scripts/Main.cs` — `LoadGameData()` replaced ContentManager strategies with
  Godot FileAccess
- `scripts/CampaignContext.cs` — all 5 methods switched to Godot FileAccess
- `scripts/MapScene.cs` — `BuildMap()` switched to Godot FileAccess

**Detection gap:** Nothing in the test suite catches this because tests run
against the source tree where `GlobalizePath` works. A proper smoke test would
build an export, run it headlessly, and fail if `_Ready()` throws an exception.

**Fix needed:** A startup smoke test (`Makefile` target or GitHub Actions job)
that builds the export and verifies it reaches the title screen without
crashing.

**Priority:** High — every exported build was broken before P3-02.

---

## Pacing values are provisional until card art lands

**Date flagged:** 2026-08-07

All timing values in the client are placeholders tuned against grey rectangle
placeholders:

| Value | Current | Context |
|---|---|---|
| Bot think-delay | 1.5s | How long the bot waits before starting its turn |
| Bot action interval | 0.6s | Delay between each bot action |
| Summon animation | 0.3s | Scale 0→1 |
| Death animation | 0.4s | Fade + shrink |
| Damage float duration | 0.9s | Floating text lifetime |

**Do NOT tune these until Phase 6 art lands on real cards.** Timing judged
against placeholder rectangles will be wrong — art changes how long a beat
feels. A summon animation that feels snappy with a grey box will feel rushed
with a card that has art, a name plate, and a strata glow.

**Resolution path:** After Phase 6, playtest the full pipeline on a real
device with art assets. Record timing pain points. Tune as a batch pass in
a dedicated ticket. Do not tune piecemeal — the rhythms are interdependent
(bot delay + animation duration + response expectation form a single
cadence).