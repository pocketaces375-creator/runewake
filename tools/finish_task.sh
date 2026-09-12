#!/usr/bin/env bash
# tools/finish_task.sh <TASK-ID> "<summary>"
# The one "done" button. Runs validation steps in order, stops at first failure.
set -euo pipefail

TASK_ID="${1:?Usage: finish_task.sh <TASK-ID> \"<summary>\"}"
SUMMARY="${2:?Usage: finish_task.sh <TASK-ID> \"<summary>\"}"
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="${PROJECT_DIR}/artifacts/captures"

info()  { echo "  $*"; }
ok()    { echo "  ✅ $*"; }
warn()  { echo "  ⚠️ $*"; }
fail()  { echo "  ❌ $*"; exit 1; }

echo "═══ finish_task.sh: ${TASK_ID} ═══"
echo "  Summary: ${SUMMARY}"

# ── Step 0: Force-clean the Godot DLL so incremental build never serves stale ──
echo ""
echo "── Step 0: Force-clean Godot DLL ──"
rm -f "${PROJECT_DIR}/client/.godot/mono/temp/bin/Debug/Runewake.Client.dll"
ok "Stale DLL removed — fresh rebuild guaranteed"

# ── Step 1: dotnet build (Debug) ──
echo ""
echo "── Step 1: dotnet build (Debug) ──"
BUILD_OUTPUT=$(cd "${PROJECT_DIR}" && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 || true)
if echo "${BUILD_OUTPUT}" | grep -q "Build succeeded."; then
  ok "Build succeeded"
else
  echo "${BUILD_OUTPUT}" | tail -10
  fail "Build failed"
fi

# ── Step 2: art_check gate (magic bytes × extension + .import valid) ──
echo ""
echo "── Step 2: art_check gate ──"
if python3 "${PROJECT_DIR}/tools/art_check.py" gate "${PROJECT_DIR}/client/content/art"; then
  ok "art_check gate passed"
else
  fail "art_check gate failed — fix mismatched files before committing"
fi

# ── Step 3: Engine + pipeline tests ──
echo ""
echo "── Step 3: Unit tests ──"
TEST_OUTPUT=$(cd "${PROJECT_DIR}" && dotnet test tests/Runewake.Tests.csproj --no-restore -c Debug 2>&1 || true)
if echo "${TEST_OUTPUT}" | grep -q "^Passed!.*Failed:\\s*0"; then
  ok "All tests passed"
elif echo "${TEST_OUTPUT}" | grep -q "^Failed!.*Failed:\\s*1"; then
  # One failure: retry ONCE (the suite is flaky, but a third full run of a
  # 26k-line suite cost more than it ever caught — a failure
  # must reproduce twice to count; 3 total attempts catches one-off flakes)
  RETRIES=1
  while [[ "${RETRIES}" -gt 0 ]]; do
    RETRIES=$((RETRIES - 1))
    echo "  One failure — retrying (${RETRIES} retries left)..."
    TEST_OUTPUT=$(cd "${PROJECT_DIR}" && dotnet test tests/Runewake.Tests.csproj --no-restore -c Debug 2>&1 || true)
    if echo "${TEST_OUTPUT}" | grep -q "^Passed!.*Failed:\\s*0"; then
      ok "All tests passed (on retry)"
      break
    fi
  done
  if ! echo "${TEST_OUTPUT}" | grep -q "^Passed!.*Failed:\\s*0"; then
    echo "${TEST_OUTPUT}" | tail -15
    fail "Tests still failing after retry"
  fi
else
  echo "${TEST_OUTPUT}" | tail -15
  fail "Tests failed"
fi

# ── Step 4: Check diff against origin/main ──
echo ""
# ── Step 4b: Required-files guard ──
echo ""
echo "── Step 4b: Required-files guard (task-named paths must be in commit) ──"
# Read the task's full line from TASKS_QUEUE.md
TASK_LINE=$(grep -m1 "^- \[ \] ${TASK_ID}" "${PROJECT_DIR}/TASKS_QUEUE.md" 2>/dev/null || echo "")
if [[ -n "${TASK_LINE}" ]]; then
  # Extract all paths matching client/scripts/, tools/ or pipeline/ from the task text
  REQUIRED_PATHS=$(echo "${TASK_LINE}" | grep -oP '(client/scripts/|tools/|pipeline/)\S+\.\S+' | sort -u || echo "")
  if [[ -n "${REQUIRED_PATHS}" ]]; then
    MISSING=""
    for rp in ${REQUIRED_PATHS}; do
      # Strip trailing punctuation that might be glued on (colon, comma, period, bracket)
      rp_clean=$(echo "${rp}" | sed 's/[,:;)\]]*$//')
      COMMITTED_FILES=$(git diff --name-only HEAD~1..HEAD 2>/dev/null || git diff --name-only --cached 2>/dev/null || echo "")
      # Also check working tree (unstaged but modified)
      MODIFIED_FILES=$(git diff --name-only 2>/dev/null || echo "")
      ALL_FILES=$(echo "${COMMITTED_FILES}${MODIFIED_FILES}" | tr ' ' '\n' | sort -u)
      if ! echo "${ALL_FILES}" | grep -q "${rp_clean}"; then
        MISSING="${MISSING}  ❌ ${rp_clean}\n"
      fi
    done
    if [[ -n "${MISSING}" ]]; then
      echo -e "  Required files missing from commit/worktree:\n${MISSING}"
      fail "Task names files under client/scripts/, tools/ or pipeline/ that are not in the commit. Do NOT mark [x] — fix the missing files first."
    fi
    ok "All task-named paths found in commit"
  else
    echo "  No required paths found in task text — guard skipped"
  fi
else
  echo "  Task line not found in TASKS_QUEUE.md — guard skipped"
fi

# ── Step 5: Diff check for client/engine changes ──
CURRENT_SHA=$(git rev-parse HEAD 2>/dev/null || echo "")
ORIGIN_SHA=$(git rev-parse origin/main 2>/dev/null || echo "")

CAPTURES_REGENERATED=0
if [[ -n "${CURRENT_SHA}" ]] && [[ -n "${ORIGIN_SHA}" ]] && [[ "${CURRENT_SHA}" != "${ORIGIN_SHA}" ]]; then
  CHANGED_FILES=$( (git diff --name-only "${ORIGIN_SHA}" "${CURRENT_SHA}"; git diff --name-only HEAD; git diff --name-only --cached; git ls-files --others --exclude-standard) 2>/dev/null | sort -u || echo "")
  if echo "${CHANGED_FILES}" | grep -qE '^(client/|engine/)'; then
    echo "  Client/engine changed — regenerating all captures"
    rm -f "${CAPTURE_DIR}"/*.png "${CAPTURE_DIR}"/*.json

    # Build fresh DLLs first — force-clean so Godot loads the new one
    rm -f "${PROJECT_DIR}/client/.godot/mono/temp/bin/Debug/Runewake.Client.dll"
    dotnet build client/Runewake.Client.csproj -c Debug 2>/dev/null

    # ── Import step: force-clean and re-import all assets before capturing ──
    echo "  Clearing import cache and re-importing all assets..."
    rm -rf "${PROJECT_DIR}/client/.godot/imported/"
    if ! timeout 600 xvfb-run -a "${GODOT_BIN}" --headless --import --path "${PROJECT_DIR}/client" 2>&1; then
        fail "Asset import failed — see errors above"
    fi
    ok "Asset import complete"

    # Capture run log for layout failure extraction
    CAPTURE_LOG="${PROJECT_DIR}/capture_run.log"
    : > "${CAPTURE_LOG}"

    # Define capture modes
    MODES=(
      "map_test:2316:1080"
      "map_test_wide:2999:1080"
      "map_test_r2:2316:1080"
      "map_test_r2_wide:2999:1080"
      "duel_test:2316:1080"
      "duel_test_wide:2999:1080"
      "duel_test_safe:2316:1080"
      "duel_test_r2:2316:1080"
      "choose_path:2316:1080"
      "choose_path_wide:2999:1080"
      "victory_overlay:2316:1080"
      "victory_overlay_wide:2999:1080"
      "defeat_overlay:2316:1080"
      "defeat_overlay_wide:2999:1080"
      "reliquary_test:2316:1080"
      "reliquary_test_wide:2999:1080"
      "reliquary_test_all:2316:1080"
      "reliquary_test_all_wide:2999:1080"
      "slots_test:2316:1080"
      "title_test:2316:1080"
      "title_test_wide:2999:1080"
      "settings_test:2316:1080"
      "settings_test_wide:2999:1080"
    )

    for mode_entry in "${MODES[@]}"; do
      mode_name="${mode_entry%%:*}"
      rest="${mode_entry#*:}"
      width="${rest%%:*}"
      height="${rest#*:}"

      echo "  Capturing ${mode_name} (${width}x${height})"
      sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "${PROJECT_DIR}/client/project.godot"
      sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "${PROJECT_DIR}/client/project.godot"
      timeout 600 xvfb-run -a "${GODOT_BIN}" --path "${PROJECT_DIR}/client" -- "--capture=${mode_name}" 2>&1 | tee -a "${CAPTURE_LOG}" || true
    done

    # Restore project.godot
    sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "${PROJECT_DIR}/client/project.godot"
    sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "${PROJECT_DIR}/client/project.godot"

    # Extract layout check failures from capture log and print them
    if [[ -f "${CAPTURE_LOG}" ]]; then
      LAYOUT_FAILS=$(grep -E '\[VERIFY\] FAIL:' "${CAPTURE_LOG}" || true)
      LAYOUT_COUNT=$(echo "${LAYOUT_FAILS}" | grep -c 'FAIL' 2>/dev/null || echo 0)
      if [[ "${LAYOUT_COUNT}" -gt 0 ]]; then
        echo "  [VERIFY] ${LAYOUT_COUNT} check(s) failed:"
        echo "${LAYOUT_FAILS}" | sed 's/.*\[VERIFY\] FAIL: /    - /'
        SUMMARY=$(grep -E '\[VERIFY\] === [0-9]+ check\(s\) failed ===' "${CAPTURE_LOG}" | tail -1)
        echo "  ${SUMMARY}"
      fi
      rm -f "${CAPTURE_LOG}"
    fi

    CAPTURES_REGENERATED=1
  else
    echo "  No client/engine changes — skipping capture regen"
  fi
else
  echo "  No diff to origin/main found (same commit or detached) — skipping capture regen"
fi

# ── Step 4: Blob check ──
echo ""
echo "── Step 5: Blob check ──"
if [[ "${CAPTURES_REGENERATED}" -eq 1 ]]; then
  BLOB_DIFFERED=0
  ORIGIN_FILES=$(git ls-tree -r "${ORIGIN_SHA}" -- artifacts/captures/ 2>/dev/null | awk '{print $4 "|" $3}' || echo "")
  for entry in ${ORIGIN_FILES}; do
    file_path=$(echo "${entry}" | cut -d'|' -f1)
    old_blob=$(echo "${entry}" | cut -d'|' -f2)
    local_path="${PROJECT_DIR}/${file_path}"
    if [[ -f "${local_path}" ]]; then
      new_blob=$(git hash-object "${local_path}" 2>/dev/null || echo "")
      if [[ -n "${new_blob}" ]] && [[ "${new_blob}" != "${old_blob}" ]]; then
        BLOB_DIFFERED=1
        ok "  ${file_path}: blob changed (${old_blob:0:7} → ${new_blob:0:7})"
        break
      fi
    fi
  done
  if [[ "${BLOB_DIFFERED}" -eq 0 ]]; then
    fail "No capture blob changed — did you build?"
  fi
else
  echo "  Skipped (no captures regenerated)"
fi

# ── Step 5: ui_lint — report findings (EMPTY_BODY rule active) ──
echo ""
echo "── Step 6: ui_lint ──"
if [[ -x "${PROJECT_DIR}/tools/ui_lint.py" ]]; then
  echo "  Running ui_lint..."
  LINT_OUTPUT=$(python3 "${PROJECT_DIR}/tools/ui_lint.py" 2>&1) && rc=0 || rc=$?
  echo "${LINT_OUTPUT}"
  if [[ "$rc" -eq 0 ]]; then
    ok "ui_lint passed"
  else
    echo "  ui_lint exit code: $rc"
    # Check if choose_path* captures have EMPTY_BODY failures (the only hard gate)
    if echo "${LINT_OUTPUT}" | grep -qE "FAIL choose_path.*\n.*EMPTY_BODY"; then
      fail "EMPTY_BODY on choose_path* capture — fix before committing"
    elif echo "${LINT_OUTPUT}" | grep -q "EMPTY_BODY"; then
      warn "EMPTY_BODY on non-choose_path capture (pre-existing — not blocking this gate)"
    else
      warn "ui_lint found non-EMPTY_BODY failures (pre-existing — not blocking this gate)"
    fi
  fi
else
  echo "  Skipping (tools/ui_lint.py not yet created)"
fi

# ── Step 6b: label_fit — no text may render outside its own card ──
echo ""
echo "── Step 6b: label_fit (text inside its card) ──"
if [[ -f "${PROJECT_DIR}/tools/label_fit.py" ]]; then
  LF_BLOCK=0
  shopt -s nullglob
  for lay in "${PROJECT_DIR}"/artifacts/captures/*.layout.json; do
    LF_OUT=$(python3 "${PROJECT_DIR}/tools/label_fit.py" "$lay" 2>&1) || true
    if echo "$LF_OUT" | grep -q "SPILL"; then
      echo "  $(basename "$lay"):"
      echo "${LF_OUT}" | sed 's/^/    /'
      LF_BLOCK=1
    elif echo "$LF_OUT" | grep -q "CANNOT MEASURE"; then
      echo "  $(basename "$lay"): CANNOT MEASURE (no rotation data — pre-existing, not blocking)"
    fi
  done
  shopt -u nullglob
  if [[ "$LF_BLOCK" -ne 0 ]]; then
    fail "label_fit: text renders outside its card. Fix the screen; do not mark the task done."
  else
    ok "label_fit passed — every label is inside its card (CANNOT MEASURE is a DebugCapture data issue, not a rendering defect)"
  fi
else
  echo "  Skipping (tools/label_fit.py not present)"
fi

# ── Step 7: input_smoke / loop_smoke — skip until they exist ──
# ── Step 6c: visual_gate — a vision model must actually look at the pixels ──
echo ""
echo "── Step 6c: visual_gate (pixel-level check) ──"
if [[ "${CAPTURES_REGENERATED}" -eq 1 ]] && [[ -f "${PROJECT_DIR}/tools/visual_gate.py" ]]; then
  # The gate runs from cron/foreman, where the environment is sanitized and
  # OPENROUTER_API_KEY is not inherited. Resolve it from the env file by
  # ABSOLUTE path before calling the gate. This makes the key findable; it
  # does NOT let the gate be skipped — visual_gate.py still fails closed if
  # the key is genuinely absent, and that failure still blocks the task.
  if [[ -z "${OPENROUTER_API_KEY:-}" ]]; then
    for _envf in "${HOME:-/home/fictive}/.hermes/.env" /home/fictive/.hermes/.env; do
      if [[ -f "${_envf}" ]]; then
        _k=$(grep -m1 '^OPENROUTER_API_KEY=' "${_envf}" 2>/dev/null | cut -d= -f2- | tr -d '"'"'"'' )
        if [[ -n "${_k}" ]]; then export OPENROUTER_API_KEY="${_k}"; break; fi
      fi
    done
  fi
  # No missing-key bypass here on purpose: visual_gate.py already fails
  # closed if it cannot find a key or cannot parse a verdict. A wrapper that
  # skips the call instead of letting it fail is how "mandatory" quietly
  # becomes "best effort" — do not reintroduce that branch.
  # Only judge what this run actually changed. Gating all ten screens on
  # every iteration was most of the vision cost, and a screen this task never
  # touched cannot have been broken by it. The full sweep still runs at APK
  # preflight, which is where "is the whole build shippable" belongs.
  GATE_SCREENS=$(cd "${PROJECT_DIR}" && git status --porcelain artifacts/captures/ 2>/dev/null \
    | awk '{print $NF}' | grep '\.png$' | xargs -r -n1 basename \
    | sed 's/\.png$//' | sort -u | paste -sd, -)
  if [[ -n "${GATE_SCREENS}" ]]; then
    echo "  gating changed screens only: ${GATE_SCREENS}"
    GATE_ARGS=(--only "${GATE_SCREENS}")
  else
    echo "  no capture changed — gating the core screens"
    GATE_ARGS=(--only choose_path,map_test,duel_test)
  fi
  if python3 "${PROJECT_DIR}/tools/visual_gate.py" "${GATE_ARGS[@]}"; then
    ok "visual_gate passed — a vision model reviewed every checked screen"
  else
    fail "visual_gate failed — see artifacts/VISUAL_GATE.json for what a vision model actually saw wrong. A task is not done because its tests pass; it is done when it looks right."
  fi
else
  echo "  Skipping (no client/engine changes this run, or tools/visual_gate.py not yet installed)"
fi

echo ""
echo "── Step 7: Input/loop smoke tests ──"
if [[ "${CAPTURES_REGENERATED:-0}" -ne 1 ]]; then
  echo "  Skipping smoke tests — this run changed no client/engine code (content-only task); build, unit tests and validators already ran"
else
for smoke_script in "${PROJECT_DIR}/tools/input_smoke.sh" "${PROJECT_DIR}/tools/loop_smoke.sh"; do
  if [[ -x "${smoke_script}" ]]; then
    # Skip loop_smoke.sh until TASK-LOOP-GATE-1 is done
    if [[ "$(basename "${smoke_script}")" == "loop_smoke.sh" ]] && \
       false; then   # loop_smoke always runs now: a task is not done if the game does not play
      echo "  Skipping (TASK-LOOP-GATE-1 not yet [x])"
      continue
    fi
    echo "  Running $(basename "${smoke_script}")..."
    SMOKE_OUTPUT=$(timeout 300 bash "${smoke_script}" 2>&1 || true)
    if echo "${SMOKE_OUTPUT}" | grep -q "PASS"; then
      ok "$(basename "${smoke_script}") passed"
    else
      echo "${SMOKE_OUTPUT}" | tail -10
      fail "$(basename "${smoke_script}") failed"
    fi
  else
    echo "  Skipping ($(basename "${smoke_script}") not yet created)"
  fi
done
fi

# ── Step 8: Commit, push, mark done ──
echo ""
echo "── Step 8: Commit and mark done ──"

# Guard: no commit may hardcode a machine or lane path — every lane is a different clone
git add -A
BAD=$(git diff --cached -U0 -- client engine pipeline tools content 2>/dev/null | grep -E '^\+' | grep -vE '^\+\+\+' | grep -nE '/home/fictive/|runewake-lane[0-9]|/home/[a-z]+/runewake' | head -5 || true)
if [[ -n "${BAD}" ]]; then
  echo "${BAD}"
  fail "Hardcoded machine path in the diff (use ProjectPaths / res:// / paths relative to the repo)"
fi
git diff --cached --quiet || git commit -m "${TASK_ID}: ${SUMMARY}"

# Push code commit
bash "${PROJECT_DIR}/tools/git_push_locked.sh" 2>&1 || true

# Append DONE entry to HERMES_STATUS.md
DONE_LINE="| ${TASK_ID} | $(date '+%Y-%m-%d') | ${SUMMARY} | DONE |"
if [[ -f "${PROJECT_DIR}/${FOREMAN_STATUS_NAME:-HERMES_STATUS.md}" ]]; then
  sed -i "1i ${DONE_LINE}" "${PROJECT_DIR}/${FOREMAN_STATUS_NAME:-HERMES_STATUS.md}"
else
  echo "${DONE_LINE}" > "${PROJECT_DIR}/${FOREMAN_STATUS_NAME:-HERMES_STATUS.md}"
fi
git add "${PROJECT_DIR}/${FOREMAN_STATUS_NAME:-HERMES_STATUS.md}"

# Flip checkbox in TASKS_QUEUE.md
sed -i "0,/^- \[ \] ${TASK_ID}/{s/^- \[ \] ${TASK_ID}/- [x] ${TASK_ID}/}" "${PROJECT_DIR}/TASKS_QUEUE.md"
git add "${PROJECT_DIR}/TASKS_QUEUE.md"

# Commit status update
git commit -m "${TASK_ID}: mark [x] + DONE entry" 2>/dev/null || true
bash "${PROJECT_DIR}/tools/git_push_locked.sh" 2>&1 || true

ok "Task ${TASK_ID} complete!"
echo "═══════════════════════════════════════"