# Tech Debt / Known Issues

Items discovered during development that are out of scope for the current
ticket but should be tracked for later resolution.

---

## Dev menu — REMOVE BEFORE RELEASE

**Date added:** 2026-08-08
**Location:** `client/scripts/DevMenu.cs`, triggered from "DEV" button on title screen (`client/scripts/Main.cs`)

**What to remove:**
- Delete `client/scripts/DevMenu.cs`
- Remove the "DEV" button block from `Main.cs._Ready()` (search for `REMOVE BEFORE RELEASE`)
- Remove the `ValidateContentIds()` method from `Main.cs` (or keep it — it's a safety check that doesn't expose anything)
- Remove `DeleteSave()` from `client/scripts/data/SaveManager.cs` (exposed via dev menu only)

**How to verify:**
- Search for `REMOVE BEFORE RELEASE` in all `.cs` files — should find 0 results after cleanup
- The dev menu grants: jump to Warden Boss, +10 dig charges, +20 fragments per strata, unlock all nodes, clear save

**Priority:** Pre-release gate. Ship-blocker if present.

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

## Android export silently swallows dotnet publish failures

**Date flagged:** 2026-08-08
**Root cause:** Godot 4.3's Android export runs `dotnet publish` internally during the Gradle build step. If publish fails (compilation error, missing method, stale reference), Godot continues the export and Gradle packages **whatever DLLs were in the cache from the last successful build**. The export reports success. The installed APK contains old assemblies.

**Detection:** APK size drops significantly. A healthy debug APK with fresh C# assemblies is 90-120 MB. A stale-assembly APK is ~75 MB and may crash on launch or at any point where old code paths intersect new content packs.

**Fix:** Use `client/export_apk.sh` instead of calling `godot --headless --editor --export-debug Android` directly. The script:
  1. Compares the newest `.cs` file timestamp against the newest `.dll` in `.godot/mono/temp/bin/` — fails loudly if DLL is stale
  2. Runs `dotnet build` if needed, then re-verifies the DLL is actually newer (not just silently copied)
  3. After export, unzips the APK and checks that `.dll` files are present and the total size isn't suspiciously small

**Priority:** Critical — cost us several wasted APK builds and debugging cycles.

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

---

## Tutorial architecture: instruction-before-action is a memory test

**Date flagged:** 2026-08-09
**Root cause:** Three successive tutorial implementations (timed banners, modal popups on a separate mode, modal popups on r1_n01) all failed because the teaching model was wrong at the architectural level.

**The failure mode (common to all three versions):**

Every version presented instructions *before* the player could act — read about Attunement, then read about Summoning, then read about Attacking, then the game unfroze and expected the player to *execute* from memory. The player consumed 6+ sentences of abstract rules over 60+ seconds before performing a single action. They were simultaneously learning vocabulary (Vigor, Attunement, Summon, Lane, Face, Target) and controls (tap card → tap lane, tap creature → tap enemy lane) with no practice between concepts.

On top of that, popup-firing was gated on conditional player-action detection (summon detected → Popup A → player must tap Continue before next detection window → Popup B). If the player acted before a deferred callback ran, the chain broke silently and later popups never fired.

**Popup condition-chain breakdown (v3 — the version on r1_n01):**

| Popup | Trigger | Fails if... |
|---|---|---|
| 1-3 (Goal, Attunement, Summoning) | `CallDeferred` from `_Ready` — unconditional ✅ | Never |
| 4a (Attacking — Your Turn) | `OnStateChanged`: checks `!_tutorialSummonedThisDuel` AND player lane has occupant | Player can't afford a card, taps wrong card, or creature dies before state-check runs |
| 4b (Choosing a Target) | `OnCreatureSelectedForAttack`: checks `_tutorialAwaitingCreatureSelect == true` (set by 4a's onContinue) | 4a never fired, or creature died while 4a was up |
| 5 (Face Hit) | `OnStateChanged`: enemy Vigor drops between snapshots | Player attacks occupied lane instead of empty one |
| 6 (Turn Cycle) | Chained off 5's `onContinue` | 5 never fired |

The cascade means a player who summons, attacks an occupied lane, and sees their creature die in the trade — a completely natural new-player sequence — gets popups 1-3 then silence. The tutorial controller stays "active" showing nothing. The player doesn't know the tutorial is over or what they were supposed to learn. ~60% of the tutorial content is never delivered.

**The fix (consequence-first model):**

Instead of read-then-do, the teaching beat fires *after* the player acts, explaining what just happened. The sequence is: do → see what happened → understand why it matters. A player who summons and then sees "You spent Attunement — that's the resource you earned last turn" internalizes it instantly because it references an experience they just had. A player who reads about Attunement 60 seconds before summoning forgets it.

**Three design rules for the rebuild:**

1. **No chained conditions.** Each popup fires independently on its own trigger. If one is missed (player never summons, never attacks face), the remaining popups still fire off their own triggers. No single breakpoint kills 60% of the content.

2. **The bot pauses while a popup is open.** Freeze the whole game, not just player input. The current implementation blocks input via `MouseFilter.Stop` on the dim overlay, but the bot controller's timer keeps running. A popup explaining "now attack with this creature" can be dismissed to find the creature already dead — killed by the bot while the player was reading. This guarantees the player learns nothing from that beat.

3. **Highlight the next action, not just the thing being described.** The current highlight system pulses a golden border on the element being explained (the enemy Vigor number, the player's Attunement value). After dismissing a popup, the player should see where to tap *next* — not the thing they just read about.

**Concrete implementation notes:**
- Every tutorial beat MUST be triggered by a real player action (summoned a card, attacked, ended turn) as a *reactive* explanation of what just happened.
- No abstract concept introduction. Every explanation starts with "When you did X..." or "That happened because Y..."
- Popups reference screen elements the player can see right now (their freshly summoned creature, the Attunement value that just decreased).
- Popups are modal (continue button only, no timers) and short (1-2 sentences max).
- No condition chains between popups — each popup is self-contained and fires independently from its action trigger.

**Priority:** High — the next tutorial rebuild uses this model or we don't attempt a fourth version. The art direction pass must land first so the teaching UI feels like the real game.

---

## Combat design gap: attacking has no meaningful cost or choice

**Date flagged:** 2026-08-09

**Problem:** There is currently no reason to NOT attack with a creature on your turn. Every creature that can attack should attack, because:
- Nothing blocks a lane — creatures don't occupy space in a way that prevents the opponent from playing into it
- There are no bad trades — the attacker chooses targets, so you never lose a creature to a bad attack
- There's no wait-to-buff incentive — no combat tricks, no pump spells, no "this creature gets +X if it didn't attack" effects exist yet
- Exhaustion is the only gate, and it resets every turn, so it's not a decision

If attacking is always correct, the game has no combat decisions. The fix is mechanical, not UI: introduce blocking, guard that forces trades, combat tricks, or a "vigor cost to attack" mechanic that makes holding back a creature a legitimate strategic choice.

**Not currently scoped:** This belongs after the art direction pass and card pool expansion. Noting it here so combat design is evaluated as a system, not patched incrementally.

**Priority:** Medium — not blocking, but the game won't have real depth until addressed.

---

## Anchor vs. offset confusion: two invisible-layout bugs

**Date flagged:** 2026-08-10

**The pattern:** When modifying a Control node's anchors or position/size in a `.tscn` file, it's easy to write an anchor value where an offset belongs (or vice versa). Godot silently applies the result — the element positions itself off-screen or with zero size.

**Two instances:**
1. **P3-02 tutorial overlay skip button** — `AnchorLeft=1, AnchorRight=0` (zero width) instead of `SetAnchorsPreset(TopRight)` with offsets. Skip button invisible on device.
2. **P3-02 BoardWrap removal (2026-08-10)** — `anchor_top=40.0` written instead of `offset_top=40.0` when flattening BoardWrap's children to direct-parent. Board and BoardBg positioned at y=648 (off-screen) and zero height respectively.

**Fix:** Always prefer `SetAnchorsPreset()` + `Offset*` in code, and use `anchors_preset = 15` (Full Rect) with explicit `offset_*` values in `.tscn` for size-constrained panels. Never write anchor values directly as integers — if the value looks like a pixel offset (40, -160, etc.) it belongs in `offset_*`, not `anchor_*`.

**Detection:** Whenever a Control is invisible at runtime, log its `Position`, `Size`, and `GlobalPosition` to distinguish off-screen layout from alpha/visibility issues.

**Priority:** Medium

---

## Project settings silently dropped from committed project.godot

**Date flagged:** 2026-08-10

**Root cause:** `window/size/viewport_width`, `window/size/viewport_height`, `window/handheld/orientation`, and `window/stretch/aspect` were all committed in `cc76e76` (P4-04) but vanished from the committed file between `51e0fbe` and `51b6296`. They remained missing through multiple builds where device layout was broken.

**How it happened:** Unknown — possibly an editor save that didn't include the display section, or a git merge/rebase that picked the wrong side. The settings are not auto-generated by Godot on every export; they must be explicitly set in the editor or written to project.godot.

**Safeguard:** A startup assertion (`Main.cs.AssertProjectSettings()`) now checks all 5 critical settings (main_scene, stretch mode, stretch aspect, orientation, viewport dimensions) at launch and logs `GD.PrintErr` on any mismatch. This catches silent config regressions on the very next build.

**Priority:** High — caused weeks of misdiagnosed layout bugs.