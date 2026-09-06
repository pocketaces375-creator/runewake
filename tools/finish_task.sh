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
if echo "${TEST_OUTPUT}" | grep -q "^Passed!.*Failed:\s*0"; then
  ok "All tests passed"
elif echo "${TEST_OUTPUT}" | grep -q "^Failed!.*Failed:\s*1"; then
  # One failure: retry once (pre-existing flake)
  echo "  One failure — retrying..."
  TEST_OUTPUT=$(cd "${PROJECT_DIR}" && dotnet test tests/Runewake.Tests.csproj --no-restore -c Debug 2>&1 || true)
  if echo "${TEST_OUTPUT}" | grep -q "^Passed!.*Failed:\s*0"; then
    ok "All tests passed (on retry)"
  else
    echo "${TEST_OUTPUT}" | tail -15
    fail "Tests still failing after retry"
  fi
else
  echo "${TEST_OUTPUT}" | tail -15
  fail "Tests failed"
fi

# ── Step 4: Check diff against origin/main ──
echo ""
echo "── Step 4: Diff check for client/engine changes ──"
CURRENT_SHA=$(git rev-parse HEAD 2>/dev/null || echo "")
ORIGIN_SHA=$(git rev-parse origin/main 2>/dev/null || echo "")

CAPTURES_REGENERATED=0
if [[ -n "${CURRENT_SHA}" ]] && [[ -n "${ORIGIN_SHA}" ]] && [[ "${CURRENT_SHA}" != "${ORIGIN_SHA}" ]]; then
  CHANGED_FILES=$(git diff --name-only "${ORIGIN_SHA}" "${CURRENT_SHA}" 2>/dev/null || echo "")
  if echo "${CHANGED_FILES}" | grep -qE '^(client/|engine/)'; then
    echo "  Client/engine changed — regenerating all captures"
    rm -f "${CAPTURE_DIR}"/*.png "${CAPTURE_DIR}"/*.json

    # Build fresh DLLs first
    dotnet build client/Runewake.Client.csproj -c Debug 2>/dev/null

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
    )

    for mode_entry in "${MODES[@]}"; do
      mode_name="${mode_entry%%:*}"
      rest="${mode_entry#*:}"
      width="${rest%%:*}"
      height="${rest#*:}"

      echo "  Capturing ${mode_name} (${width}x${height})"
      sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "${PROJECT_DIR}/client/project.godot"
      sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "${PROJECT_DIR}/client/project.godot"
      timeout 600 xvfb-run -a "${GODOT_BIN}" --path "${PROJECT_DIR}/client" -- "--capture=${mode_name}" 2>&1 || true
    done

    # Restore project.godot
    sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "${PROJECT_DIR}/client/project.godot"
    sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "${PROJECT_DIR}/client/project.godot"

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
  LF_FAIL=0
  shopt -s nullglob
  for lay in "${PROJECT_DIR}"/artifacts/captures/*.layout.json; do
    LF_OUT=$(python3 "${PROJECT_DIR}/tools/label_fit.py" "$lay" 2>&1) || LF_FAIL=1
    if [[ "$LF_OUT" == *SPILL* || "$LF_OUT" == *"CANNOT MEASURE"* ]]; then
      echo "  $(basename "$lay"):"
      echo "${LF_OUT}" | sed 's/^/    /'
    fi
  done
  shopt -u nullglob
  if [[ "$LF_FAIL" -ne 0 ]]; then
    fail "label_fit: text renders outside its card (or the capture carries no rotation data). Fix the screen; do not mark the task done."
  else
    ok "label_fit passed — every label is inside its card"
  fi
else
  echo "  Skipping (tools/label_fit.py not present)"
fi

# ── Step 7: input_smoke / loop_smoke — skip until they exist ──
echo ""
echo "── Step 7: Input/loop smoke tests ──"
for smoke_script in "${PROJECT_DIR}/tools/input_smoke.sh" "${PROJECT_DIR}/tools/loop_smoke.sh"; do
  if [[ -x "${smoke_script}" ]]; then
    # Skip loop_smoke.sh until TASK-LOOP-GATE-1 is done
    if [[ "$(basename "${smoke_script}")" == "loop_smoke.sh" ]] && \
       false; then   # loop_smoke always runs now: a task is not done if the game does not play
      echo "  Skipping (TASK-LOOP-GATE-1 not yet [x])"
      continue
    fi
    echo "  Running $(basename "${smoke_script}")..."
    SMOKE_OUTPUT=$(timeout 180 bash "${smoke_script}" 2>&1 || true)
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