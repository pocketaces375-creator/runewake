#!/usr/bin/env bash
# tools/foreman.sh — Autonomous task execution loop for Runewake
#
# Performs one iteration:
#   1. Check circuit breakers (HALT, daily budget, BLOCKED stickiness)
#   2. Read TASKS_QUEUE.md → top unchecked [ ] task
#   3. Repeat-detector
#   4. Run one hermes model session (45-min wall clock)
#   5. BUDGET INCREMENT (sessions cost tokens regardless of outcome)
#   6. Mechanical validation (commit, checkbox, FRESH gate, tests, push)
#   7. One retry on failure → BLOCKED-and-exit (sticky, notified once)
#   8. Commit state each iteration + Telegram notification
#
# Usage:
#   bash tools/foreman.sh                    # one iteration
#   touch FOREMAN_HALT                       # stop all future runs
#   rm FOREMAN_HALT                          # resume
#
# Env overrides:
#   FOREMAN_PROJECT_DIR       default: /home/fictive/runewake
#   FOREMAN_MODEL             default: deepseek/deepseek-v4-flash
#   FOREMAN_TIMEOUT           default: 2700 (45 min)
#   FOREMAN_DAILY_BUDGET      default: 10
#   FOREMAN_TELEGRAM_TARGET   default: telegram:Runewake
#   FOREMAN_GODOT_BIN         default: /home/fictive/Godot_v4.3-stable_linux.x86_64
#
set -euo pipefail

# ── Config ───────────────────────────────────────────────────────────────────
PROJECT_DIR="${FOREMAN_PROJECT_DIR:-$HOME/runewake}"
FOREMAN_MODEL="${FOREMAN_MODEL:-deepseek/deepseek-v4-flash}"
FOREMAN_TIMEOUT="${FOREMAN_TIMEOUT:-2700}"
DAILY_BUDGET="${FOREMAN_DAILY_BUDGET:-10}"
TELEGRAM_TARGET="${FOREMAN_TELEGRAM_TARGET:-telegram:Runewake}"
GODOT_BIN="${FOREMAN_GODOT_BIN:-$HOME/Godot_v4.3-stable_linux.x86_64}"

HALT_FILE="${PROJECT_DIR}/FOREMAN_HALT"
STATE_FILE="${PROJECT_DIR}/tools/foreman_state.json"
QUEUE_FILE="${PROJECT_DIR}/TASKS_QUEUE.md"
CAPTURE_DIR="${PROJECT_DIR}/artifacts/captures"
LAST_RUN_LOG="${PROJECT_DIR}/tools/foreman_last_run.log"

HERMES_BIN="${HOME}/.local/bin/hermes"

# PID-based lock (avoids FD inheritance into compiler daemons)
LOCK_PID_FILE="/tmp/runewake_foreman.pid"

# ── Helpers ───────────────────────────────────────────────────────────────────

info()  { echo "  $*"; }
ok()    { echo "  $*"; }
warn()  { echo "  $*"; }
fail()  { echo "  $*"; }
header(){ echo; echo "=== $* ==="; echo; }

# Read state JSON field
get_state() {
  python3 -c "import json,sys; d=json.load(open('${STATE_FILE}')); print(d.get('${1}',''));"
}

# Write a state field
set_state() {
  local key="$1" val="$2"
  python3 -c "
import json
d = json.load(open('${STATE_FILE}'))
d['${key}'] = ${val}
with open('${STATE_FILE}','w') as f:
    json.dump(d, f, indent=2)
    f.write('\n')
"
}

# Send a text notification to Telegram (failure never stops the loop)
telegram_text() {
  local msg="$1"
  if [[ -x "${HERMES_BIN}" ]]; then
    "${HERMES_BIN}" send --to "${TELEGRAM_TARGET}" "${msg}" 2>/dev/null || true
  fi
}

# Send a photo to Telegram (failure never stops the loop)
telegram_photo() {
  local photo_path="$1" caption="$2"
  if [[ ! -f "$photo_path" ]]; then
    return 0
  fi
  local msg="MEDIA:${photo_path}"
  if [[ -n "$caption" ]]; then
    msg="${msg}\\n${caption}"
  fi
  if [[ -x "${HERMES_BIN}" ]]; then
    "${HERMES_BIN}" send --to "${TELEGRAM_TARGET}" "$(echo -e "${msg}")" 2>/dev/null || true
  fi
}

# Get today's date
today() {
  date +%Y-%m-%d
}

# Find the top unchecked task in TASKS_QUEUE.md
find_top_task() {
  python3 -c "
import re, sys
with open('${QUEUE_FILE}') as f:
    content = f.read()
in_queue = False
for line in content.split('\n'):
    if line.strip().startswith('## Queue'):
        in_queue = True
        continue
    if in_queue and line.strip().startswith('## '):
        break
    if in_queue:
        m = re.match(r'^\s*-\s*\[\s*\]\s*(TASK-\S+)\s*[—–-]?\s*(.*)', line)
        if m:
            print(f'{m.group(1)}|{m.group(2).strip()}')
            sys.exit(0)
sys.exit(1)
" 2>/dev/null || echo ""
}

# Check if a specific task was flipped to [x] in the queue
check_task_done() {
  local task_id="$1"
  python3 -c "
import re
with open('${QUEUE_FILE}') as f:
    content = f.read()
for line in content.split('\n'):
    if '${task_id}' in line and re.match(r'^\s*-\s*\[\s*x\s*\]', line):
        print('done')
        break
" 2>/dev/null || echo ""
}

# Run hermes agent session with timeout
run_hermes_session() {
  local task_id="$1" task_desc="$2"
  local prompt="Implement the top unchecked task from ${QUEUE_FILE}: ${task_id} — ${task_desc}. Standard protocol in one session: implement the task, run the harness + gate, commit with message 'TASK-${task_id}: ${task_desc}', push, write DONE line in HERMES_STATUS.md, then stop. Work from ${PROJECT_DIR}."

  cd "${PROJECT_DIR}"
  timeout "${FOREMAN_TIMEOUT}" "${HERMES_BIN}" -z "${prompt}" 2>&1 || echo "HERMES_EXIT_CODE=$?"
}

# ── Lock (PID file, no FD inheritance into children) ─────────────────────────
if [[ -f "${LOCK_PID_FILE}" ]]; then
  LOCK_PID=$(cat "${LOCK_PID_FILE}" 2>/dev/null || echo "")
  if [[ -n "${LOCK_PID}" ]] && kill -0 "${LOCK_PID}" 2>/dev/null; then
    echo "foreman already running (PID ${LOCK_PID}) — exiting"
    exit 0
  else
    rm -f "${LOCK_PID_FILE}"
  fi
fi
echo "$$" > "${LOCK_PID_FILE}"
trap 'rm -f "${LOCK_PID_FILE}"' EXIT

# ── Main ─────────────────────────────────────────────────────────────────────

header "Foreman — one iteration"
cd "${PROJECT_DIR}"

# Ensure project dir exists
if [[ ! -d "${PROJECT_DIR}" ]]; then
  fail "Project directory not found"
  exit 2
fi

# Ensure git repo
if ! git rev-parse --git-dir >/dev/null 2>&1; then
  fail "Not a git repository"
  exit 2
fi

# ── 1. Circuit breaker: HALT file ────────────────────────────────────────────
if [[ -f "${HALT_FILE}" ]]; then
  warn "HALT file found at ${HALT_FILE}"
  telegram_text "FOREMAN HALTED — ${HALT_FILE} exists"
  exit 1
fi

# ── 2. Circuit breaker: Daily budget ─────────────────────────────────────────
TODAY=$(today)
STATE_DATE=$(get_state "date")
SESSION_COUNT=$(get_state "session_count")
RETRY_TASK_ID=$(get_state "retry_task_id")
RETRY_COUNT=$(get_state "retry_count")
BLOCKED_NOTIFIED=$(get_state "blocked_notified")

if [[ "${STATE_DATE}" != "${TODAY}" ]]; then
  set_state "date" "\"${TODAY}\""
  set_state "session_count" 0
  set_state "retry_count" 0
  set_state "retry_task_id" "\"\""
  set_state "blocked_notified" "False"
  SESSION_COUNT=0
  BLOCKED_NOTIFIED="False"
fi

if [[ "${SESSION_COUNT}" -ge "${DAILY_BUDGET}" ]]; then
  warn "Daily budget spent: ${SESSION_COUNT}/${DAILY_BUDGET}"
  telegram_text "Budget spent — ${SESSION_COUNT}/${DAILY_BUDGET} sessions today"
  exit 0
fi

info "Budget: ${SESSION_COUNT}/${DAILY_BUDGET} sessions used today"

# ── 3. Read queue ────────────────────────────────────────────────────────────
TOP_TASK=$(find_top_task)

if [[ -z "${TOP_TASK}" ]]; then
  ok "Queue is empty"
  telegram_text "Queue empty — no tasks remaining"
  exit 0
fi

TASK_ID=$(echo "${TOP_TASK}" | cut -d'|' -f1)
TASK_DESC=$(echo "${TOP_TASK}" | cut -d'|' -f2-)
info "Top task: ${TASK_ID}"

# ── 3b. BLOCKED check (sticky) ──────────────────────────────────────────────
if [[ "${TASK_ID}" == "${RETRY_TASK_ID}" ]] && [[ "${RETRY_COUNT}" -ge 1 ]]; then
  if [[ "${BLOCKED_NOTIFIED}" != "True" ]]; then
    fail "Task ${TASK_ID} BLOCKED (exhausted retry ${RETRY_COUNT})"
    telegram_text "BLOCKED: ${TASK_ID} — exhausted retry (${RETRY_COUNT})"
    set_state "blocked_notified" "True"
  else
    info "Task ${TASK_ID} still BLOCKED (already notified, silent)"
  fi
  exit 1
fi

# ── 4. Execute model session ─────────────────────────────────────────────────
header "Running: ${TASK_ID}"
info "Model: ${FOREMAN_MODEL}  Timeout: ${FOREMAN_TIMEOUT}s"

SESSION_OUTPUT=$(run_hermes_session "${TASK_ID}" "${TASK_DESC}")

if echo "${SESSION_OUTPUT}" | grep -q "HERMES_EXIT_CODE="; then
  HERMES_EXIT=$(echo "${SESSION_OUTPUT}" | grep "HERMES_EXIT_CODE=" | tail -1 | sed 's/.*HERMES_EXIT_CODE=//')
  warn "Hermes exited ${HERMES_EXIT}"
  SESSION_OUTPUT=$(echo "${SESSION_OUTPUT}" | sed 's/HERMES_EXIT_CODE=.*//')
else
  ok "Hermes session completed"
fi

echo "${SESSION_OUTPUT}" | tail -5

# ── FIX #1: Budget increments every session ──────────────────────────────────
SESSION_COUNT=$((SESSION_COUNT + 1))
set_state "session_count" "${SESSION_COUNT}"
SESSION_OUTPUT_SNAPSHOT=$(echo "${SESSION_OUTPUT}" | tail -50)

# ── FIX #4: Fresh capture before gate ────────────────────────────────────────
header "Regenerating capture"
CAPTURE_REGEN_OK=0
if [[ -x "${GODOT_BIN}" ]]; then
  rm -f "${CAPTURE_DIR}"/*.png "${CAPTURE_DIR}"/*.json
  mkdir -p "${CAPTURE_DIR}"
  CAPTURE_OUTPUT=$(cd "${PROJECT_DIR}" && timeout 120 xvfb-run -a "${GODOT_BIN}" --path client -- --capture=duel_test 2>&1 || true)
  FRESH_CAPTURE=$(ls -t "${CAPTURE_DIR}"/*.png 2>/dev/null | head -1)
  if [[ -n "${FRESH_CAPTURE}" ]]; then
    CAPTURE_REGEN_OK=1
    ok "Capture regenerated"
  else
    warn "Capture regen failed"
    echo "${CAPTURE_OUTPUT}" | tail -5
  fi
else
  warn "Godot not found at ${GODOT_BIN}, skipping capture"
  info "Skipping capture regen"
fi

# ── 5. Mechanical validation ─────────────────────────────────────────────────
header "Validation"

VALIDATION_FAILED=0
VALIDATION_REASONS=""
NEW_COMMIT_SHA=""
CURRENT_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")

# 5a. New commit?
NEW_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")
if [[ "${NEW_HEAD}" == "${CURRENT_HEAD}" ]]; then
  warn "No new commit"
  VALIDATION_FAILED=1
  VALIDATION_REASONS="${VALIDATION_REASONS}no_new_commit "
else
  NEW_COMMIT_SHA="${NEW_HEAD}"
  ok "New commit: $(git log -1 --oneline)"
fi

# 5b. Queue checkbox flipped?
CHECK_DONE=$(check_task_done "${TASK_ID}")
if [[ "${CHECK_DONE}" == "done" ]]; then
  ok "Task ${TASK_ID} marked [x]"
else
  warn "Task ${TASK_ID} not [x] in queue"
  VALIDATION_FAILED=1
  VALIDATION_REASONS="${VALIDATION_REASONS}checkbox_not_flipped "
fi

# 5c. Gate passes on fresh capture?
GATE_PASSED=0
LATEST_CAPTURE=""
if ls "${CAPTURE_DIR}"/*.png 2>/dev/null | head -1 >/dev/null 2>&1; then
  LATEST_CAPTURE=$(ls -t "${CAPTURE_DIR}"/*.png 2>/dev/null | head -1)
  GATE_OUTPUT=$(cd "${PROJECT_DIR}" && python3 tools/capture_gate.py 2>&1 || true)
  if echo "${GATE_OUTPUT}" | grep -q "PASS:"; then
    GATE_PASSED=1
    ok "Pixel gate passed"
  else
    warn "Pixel gate failed"
    VALIDATION_FAILED=1
    VALIDATION_REASONS="${VALIDATION_REASONS}gate_failure "
  fi
elif [[ "${CAPTURE_REGEN_OK}" -eq 0 ]]; then
  warn "No capture — regen also failed"
  VALIDATION_FAILED=1
  VALIDATION_REASONS="${VALIDATION_REASONS}capture_regen_failed "
else
  info "No capture, skipping gate"
  GATE_PASSED=1
fi

# 5d. Dotnet tests green? (decision by exit code only)
if command -v dotnet &>/dev/null; then
  if (cd "${PROJECT_DIR}" && dotnet test tests/Runewake.Tests.csproj --nologo -v q); then
    ok "Dotnet tests passed"
  else
    warn "Dotnet tests failed"
    VALIDATION_FAILED=1
    VALIDATION_REASONS="${VALIDATION_REASONS}dotnet_test_failure "
  fi
else
  info "dotnet not available"
fi

# 5e. Python tests green? (decision by exit code only)
if command -v python3 &>/dev/null && [[ -d "${PROJECT_DIR}/tests" ]]; then
  if (cd "${PROJECT_DIR}" && python3 -m pytest tests/ -x -q); then
    ok "Python tests passed"
  else
    warn "Python tests failed"
    VALIDATION_FAILED=1
    VALIDATION_REASONS="${VALIDATION_REASONS}pytest_failure "
  fi
fi

# ── FIX #3: Enforce push on success ──────────────────────────────────────────
PUSH_OK=0
if [[ "${VALIDATION_FAILED}" -eq 0 ]]; then
  PUSH_OUTPUT=$(cd "${PROJECT_DIR}" && git push origin main 2>&1 || true)
  if echo "${PUSH_OUTPUT}" | grep -q "fatal\|error\|rejected"; then
    warn "Git push failed"
    VALIDATION_FAILED=1
    VALIDATION_REASONS="${VALIDATION_REASONS}push_failure "
  else
    PUSH_OK=1
    ok "Git push successful"
  fi
fi

# ── 6. Outcome ───────────────────────────────────────────────────────────────
if [[ "${VALIDATION_FAILED}" -eq 0 ]]; then
  set_state "last_task_id" "\"${TASK_ID}\""
  set_state "last_commit_sha" "\"${NEW_COMMIT_SHA}\""
  set_state "retry_count" 0
  set_state "retry_task_id" "\"\""
  set_state "blocked_notified" "False"

  ok "Task ${TASK_ID} complete! (${SESSION_COUNT}/${DAILY_BUDGET})"
  telegram_text "${TASK_ID} done (${SESSION_COUNT}/${DAILY_BUDGET})"
  if [[ "${GATE_PASSED}" -eq 1 ]] && [[ -n "${LATEST_CAPTURE}" ]]; then
    telegram_photo "${LATEST_CAPTURE}" "${TASK_ID} — capture"
  fi
else
  if [[ "${TASK_ID}" == "${RETRY_TASK_ID}" ]]; then
    warn "Task ${TASK_ID} BLOCKED after ${RETRY_COUNT} retries"
    telegram_text "BLOCKED: ${TASK_ID} — failed after retry (${VALIDATION_REASONS})"
    set_state "blocked_notified" "True"
    # Sticky: do NOT reset retry_count/retry_task_id
  else
    NEW_RETRY=$((RETRY_COUNT + 1))
    warn "Retry (${NEW_RETRY}/1): ${VALIDATION_REASONS}"
    telegram_text "Retry ${TASK_ID} attempt ${NEW_RETRY} (${VALIDATION_REASONS})"
    set_state "retry_count" "${NEW_RETRY}"
    set_state "retry_task_id" "\"${TASK_ID}\""

    # Save state before cleanup
    STATE_SNAPSHOT=$(cat "${STATE_FILE}" 2>/dev/null || echo "{}")

    # Only revert if a failed worker commit actually exists
    if [[ -n "${NEW_COMMIT_SHA}" ]] && [[ "${NEW_COMMIT_SHA}" != "${CURRENT_HEAD}" ]]; then
      if git branch -r --contains "${NEW_COMMIT_SHA}" 2>/dev/null | grep -q "origin/main"; then
        info "Reverting pushed commit ${NEW_COMMIT_SHA}"
        git revert --no-edit "${NEW_COMMIT_SHA}" 2>/dev/null || true
        git push origin main 2>/dev/null || true
      else
        info "Resetting local commit ${NEW_COMMIT_SHA}"
        git reset --hard "${CURRENT_HEAD}" 2>/dev/null || true
      fi
      # Only clean up tracked changes when we reverted a failed commit
      git checkout -- . 2>/dev/null || true
      git clean -fd 2>/dev/null || true
    else
      info "No failed worker commit — leaving tree untouched"
    fi
    echo "${STATE_SNAPSHOT}" > "${STATE_FILE}"
  fi
fi

# ── FIX #5: Commit state each iteration ──────────────────────────────────────
OUTCOME_LABEL="success"
if [[ "${VALIDATION_FAILED}" -ne 0 ]]; then
  if [[ "${TASK_ID}" == "${RETRY_TASK_ID}" ]]; then
    OUTCOME_LABEL="blocked"
  else
    OUTCOME_LABEL="retry"
  fi
fi

{
  echo "=== foreman: $(date -u '+%Y-%m-%dT%H:%M:%SZ') ${TASK_ID} ${OUTCOME_LABEL} ==="
  echo "Session: ${SESSION_COUNT}/${DAILY_BUDGET}"
  if [[ -n "${VALIDATION_REASONS:-}" ]]; then echo "Failures: ${VALIDATION_REASONS}"; fi
  if [[ -n "${SESSION_OUTPUT_SNAPSHOT:-}" ]]; then
    echo "--- session tail ---"
    echo "${SESSION_OUTPUT_SNAPSHOT}"
  fi
  echo "================================"
} >> "${LAST_RUN_LOG}"
tail -50 "${LAST_RUN_LOG}" > "${LAST_RUN_LOG}.tmp" 2>/dev/null || true && mv "${LAST_RUN_LOG}.tmp" "${LAST_RUN_LOG}" 2>/dev/null || true

cd "${PROJECT_DIR}"
git add "${STATE_FILE}" "${LAST_RUN_LOG}" 2>/dev/null || true
if ! git diff --cached --quiet 2>/dev/null; then
  git commit -m "foreman: state after ${TASK_ID} (${OUTCOME_LABEL})" 2>/dev/null || true
  git push origin main 2>/dev/null || true
fi

if [[ "${VALIDATION_FAILED}" -eq 0 ]]; then
  exit 0
else
  exit 1
fi