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