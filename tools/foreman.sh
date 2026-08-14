#!/usr/bin/env bash
# tools/foreman.sh — Autonomous task execution loop for Runewake
#
# Performs one iteration:
#   1. Check circuit breakers (HALT, daily budget)
#   2. Read TASKS_QUEUE.md → top unchecked [ ] task
#   3. Repeat-detector
#   4. Run one hermes model session (45-min wall clock)
#   5. Mechanical validation (commit, checkbox, tests, gate)
#   6. One retry on failure → BLOCKED-and-exit
#   7. Telegram notification (shell-side, failure never stops loop)
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
#
set -euo pipefail

# ── Config ───────────────────────────────────────────────────────────────────
PROJECT_DIR="${FOREMAN_PROJECT_DIR:-$HOME/runewake}"
FOREMAN_MODEL="${FOREMAN_MODEL:-deepseek/deepseek-v4-flash}"
FOREMAN_TIMEOUT="${FOREMAN_TIMEOUT:-2700}"
DAILY_BUDGET="${FOREMAN_DAILY_BUDGET:-10}"
TELEGRAM_TARGET="${FOREMAN_TELEGRAM_TARGET:-telegram:Runewake}"

HALT_FILE="${PROJECT_DIR}/FOREMAN_HALT"
STATE_FILE="${PROJECT_DIR}/tools/foreman_state.json"
QUEUE_FILE="${PROJECT_DIR}/TASKS_QUEUE.md"
CAPTURE_DIR="${PROJECT_DIR}/artifacts/captures"

HERMES_BIN="${HOME}/.local/bin/hermes"

# ── Helpers ───────────────────────────────────────────────────────────────────

info()  { echo "  ℹ️  $*"; }
ok()    { echo "  ✅ $*"; }
warn()  { echo "  ⚠️  $*"; }
fail()  { echo "  ❌ $*"; }
header(){ echo; echo "═══ $* ═══"; echo; }

# Read state JSON field
get_state() {
  python3 -c "import json; f=open('${STATE_FILE}'); d=json.load(f); print(d.get('${1}','')); f.close()"
}

# Write a state field
set_state() {
  local key="$1" val="$2"
  python3 -c "
import json
f=open('${STATE_FILE}')
d=json.load(f)
f.close()
d['${key}'] = ${val}
f=open('${STATE_FILE}','w')
json.dump(d, f, indent=2)
f.write('\n')
f.close()
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
  # Try hermes send with MEDIA: prefix first
  local msg="MEDIA:${photo_path}"
  if [[ -n "$caption" ]]; then
    msg="${msg}\n${caption}"
  fi
  if [[ -x "${HERMES_BIN}" ]]; then
    "${HERMES_BIN}" send --to "${TELEGRAM_TARGET}" "$(echo -e "${msg}")" 2>/dev/null || true
  fi
}

# Get today's date
today() {
  date +%Y-%m-%d
}

# Find the top unchecked task in TASKS_QUEUE.md — returns task ID line
# Output: "TASK-F4 Board/hand placeholder art pass"
find_top_task() {
  python3 -c "
import re, sys
with open('${QUEUE_FILE}') as f:
    content = f.read()
# Match [ ] items after the ## Queue header
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
  
  # Only pass skills if hsitool exists and we want them
  cd "${PROJECT_DIR}"
  timeout "${FOREMAN_TIMEOUT}" "${HERMES_BIN}" -z "${prompt}" 2>&1 || echo "HERMES_EXIT_CODE=$?"
}

# ── Main ─────────────────────────────────────────────────────────────────────

# Lockfile: prevent concurrent foreman runs (required for cron usage)
exec 200>"/tmp/runewake_foreman.lock"
flock -n 200 || { echo "foreman already running — exiting"; exit 0; }

header "Foreman — one iteration"

cd "${PROJECT_DIR}"

# Ensure project dir exists
if [[ ! -d "${PROJECT_DIR}" ]]; then
  fail "Project directory not found: ${PROJECT_DIR}"
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
  telegram_text "🛑 FOREMAN HALTED — ${HALT_FILE} exists"
  exit 1
fi

# ── 2. Circuit breaker: Daily budget ─────────────────────────────────────────
TODAY=$(today)
STATE_DATE=$(get_state "date")
SESSION_COUNT=$(get_state "session_count")

if [[ "${STATE_DATE}" != "${TODAY}" ]]; then
  # New day — reset budget
  set_state "date" "\"${TODAY}\""
  set_state "session_count" 0
  set_state "retry_count" 0
  set_state "retry_task_id" "\"\""
  SESSION_COUNT=0
fi

if [[ "${SESSION_COUNT}" -ge "${DAILY_BUDGET}" ]]; then
  warn "Daily budget spent: ${SESSION_COUNT}/${DAILY_BUDGET}"
  telegram_text "⏸ Budget spent — ${SESSION_COUNT}/${DAILY_BUDGET} sessions today"
  exit 0
fi

info "Budget: ${SESSION_COUNT}/${DAILY_BUDGET} sessions used today"

# ── 3. Read queue ────────────────────────────────────────────────────────────
TOP_TASK=$(find_top_task)

if [[ -z "${TOP_TASK}" ]]; then
  ok "Queue is empty — no unchecked tasks"
  telegram_text "📭 Queue empty — no tasks remaining"
  exit 0
fi

# Parse task ID and description
TASK_ID=$(echo "${TOP_TASK}" | cut -d'|' -f1)
TASK_DESC=$(echo "${TOP_TASK}" | cut -d'|' -f2-)
info "Top task: ${TASK_ID} — ${TASK_DESC}"

# ── 3b. Repeat-detector ──────────────────────────────────────────────────────
LAST_TASK_ID=$(get_state "last_task_id")
LAST_COMMIT=$(get_state "last_commit_sha")
CURRENT_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")

RETRY_TASK_ID=$(get_state "retry_task_id")
RETRY_COUNT=$(get_state "retry_count")

if [[ "${TASK_ID}" == "${RETRY_TASK_ID}" ]] && [[ "${RETRY_COUNT}" -ge 1 ]]; then
  fail "Task ${TASK_ID} already had 1 retry — marking BLOCKED"
  telegram_text "🛑 BLOCKED: ${TASK_ID} — exhausted retry (${RETRY_COUNT})"
  exit 1
fi

# ── 4. Execute model session ─────────────────────────────────────────────────
header "Running: ${TASK_ID}"
info "Model: ${FOREMAN_MODEL}"
info "Timeout: ${FOREMAN_TIMEOUT}s"

SESSION_OUTPUT=$(run_hermes_session "${TASK_ID}" "${TASK_DESC}")

# Check if hermes timed out vs crashed vs completed
if echo "${SESSION_OUTPUT}" | grep -q "HERMES_EXIT_CODE="; then
  HERMES_EXIT=$(echo "${SESSION_OUTPUT}" | grep "HERMES_EXIT_CODE=" | tail -1 | sed 's/.*HERMES_EXIT_CODE=//')
  warn "Hermes session did not complete cleanly (exit ${HERMES_EXIT})"
  SESSION_OUTPUT=$(echo "${SESSION_OUTPUT}" | sed 's/HERMES_EXIT_CODE=.*//')
else
  info "Hermes session completed"
fi

# Print last 5 lines of session output for context
echo "${SESSION_OUTPUT}" | tail -5

# ── 5. Mechanical validation ─────────────────────────────────────────────────
header "Validation"

VALIDATION_FAILED=0
VALIDATION_REASONS=""
NEW_COMMIT_SHA=""

# 5a. New commit?
NEW_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")
if [[ "${NEW_HEAD}" == "${CURRENT_HEAD}" ]]; then
  warn "No new commit found"
  VALIDATION_FAILED=1
  VALIDATION_REASONS="${VALIDATION_REASONS}no_new_commit "
else
  NEW_COMMIT_SHA="${NEW_HEAD}"
  COMMIT_MSG=$(git log -1 --oneline)
  ok "New commit: ${COMMIT_MSG}"
fi

# 5b. Queue checkbox flipped?
CHECK_DONE=$(check_task_done "${TASK_ID}")
if [[ "${CHECK_DONE}" == "done" ]]; then
  ok "Task ${TASK_ID} marked [x] in TASKS_QUEUE.md"
else
  warn "Task ${TASK_ID} not yet marked [x] in queue"
  VALIDATION_FAILED=1
  VALIDATION_REASONS="${VALIDATION_REASONS}checkbox_not_flipped "
fi

# 5c. Dotnet tests green?
TEST_OUTPUT=""
if command -v dotnet &>/dev/null; then
  TEST_OUTPUT=$(cd "${PROJECT_DIR}" && dotnet test tests/Runewake.Tests.csproj --nologo -v q 2>&1 || true)
  if echo "${TEST_OUTPUT}" | grep -q "Failed:" && ! echo "${TEST_OUTPUT}" | grep -q "Failed: 0"; then
    warn "Dotnet tests have failures"
    VALIDATION_FAILED=1
    VALIDATION_REASONS="${VALIDATION_REASONS}dotnet_test_failure "
  else
    ok "Dotnet tests passed"
  fi
else
  info "dotnet not available, skipping dotnet tests"
fi

# 5d. Python tests green?
PYTEST_OUTPUT=""
if command -v python3 &>/dev/null && [[ -d "${PROJECT_DIR}/tests" ]]; then
  PYTEST_OUTPUT=$(cd "${PROJECT_DIR}" && python3 -m pytest tests/ -x -q 2>&1 || true)
  if echo "${PYTEST_OUTPUT}" | grep -q "failed" && ! echo "${PYTEST_OUTPUT}" | grep -q "0 failed"; then
    warn "Python tests have failures"
    VALIDATION_FAILED=1
    VALIDATION_REASONS="${VALIDATION_REASONS}pytest_failure "
  else
    ok "Python tests passed"
  fi
fi

# 5e. Gate passes? (only if a capture PNG exists)
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
else
  info "No capture files found, skipping gate"
  GATE_PASSED=1
fi

# ── 6. Outcome ───────────────────────────────────────────────────────────────
if [[ "${VALIDATION_FAILED}" -eq 0 ]]; then
  # ── SUCCESS ──
  NEW_COUNT=$((SESSION_COUNT + 1))
  set_state "session_count" "${NEW_COUNT}"
  set_state "last_task_id" "\"${TASK_ID}\""
  set_state "last_commit_sha" "\"${NEW_COMMIT_SHA}\""
  set_state "retry_count" 0
  set_state "retry_task_id" "\"\""

  ok "Task ${TASK_ID} complete! (${NEW_COUNT}/${DAILY_BUDGET})"

  # Telegram notify: success + capture PNG
  telegram_text "✅ ${TASK_ID} done (${NEW_COUNT}/${DAILY_BUDGET})"
  if [[ "${GATE_PASSED}" -eq 1 ]] && [[ -n "${LATEST_CAPTURE}" ]]; then
    telegram_photo "${LATEST_CAPTURE}" "✅ ${TASK_ID} — capture"
  fi

  exit 0
else
  # ── FAILURE — retry once ──
  if [[ "${TASK_ID}" == "${RETRY_TASK_ID}" ]]; then
    # Already retried — BLOCKED
    NEW_RETRY=$((RETRY_COUNT))
    warn "Task ${TASK_ID} failed after ${NEW_RETRY} retries — BLOCKED"
    telegram_text "🛑 BLOCKED: ${TASK_ID} — failed validation after retry (${VALIDATION_REASONS})"
    set_state "retry_count" 0
    set_state "retry_task_id" "\"\""
    exit 1
  else
    # First failure — retry
    NEW_RETRY=$((RETRY_COUNT + 1))
    warn "Validation failed — will retry (attempt ${NEW_RETRY}/1)"
    telegram_text "⚠️ ${TASK_ID} — retry ${NEW_RETRY} (${VALIDATION_REASONS})"
    set_state "retry_count" "${NEW_RETRY}"
    set_state "retry_task_id" "\"${TASK_ID}\""

    # Revert the failed commit if there was one, so retry starts fresh
    if [[ -n "${NEW_COMMIT_SHA}" ]] && [[ "${NEW_COMMIT_SHA}" != "${CURRENT_HEAD}" ]]; then
      info "Reverting to ${CURRENT_HEAD} for clean retry"
      git reset --hard "${CURRENT_HEAD}" 2>/dev/null || true
    fi

    # Revert any working-tree changes
    git checkout -- . 2>/dev/null || true
    git clean -fd 2>/dev/null || true

    exit 1
  fi
fi