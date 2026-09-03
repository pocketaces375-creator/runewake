#!/usr/bin/env bash
# tools/foreman.sh — Autonomous task execution loop for Runewake
#
# Chain mode: after a SUCCESSFUL iteration, immediately run the next task
# while queue + budget allow (no cron wait). Every 3 consecutive successes
# forces a 15-min cool-down (Claude's live-review window), then resumes.
# ANY failure, transient, or block ends the chain — cron picks up later.
#
# Bus: at the top of every iteration, after git pull, ALSO fetch origin/claude-bus.
# Check bus/claude_to_hermes.md on BOTH main and origin/claude-bus for new
# messages (sequenced, trusted via bus-only commit check). If found, run a
# 15-min bus-session before queue work.
#
# Cron: 17,47 * * * * (every 30 min, PID lock prevents overlap).
# Budget: 48 sessions/day (half-session accounting), cool-down 15 min.
#
# Each iteration:
#   1. Check circuit breakers (HALT, git pull, bus check, daily budget halves)
#   2. Read TASKS_QUEUE.md → top unchecked [ ] task
#   3. Repeat-detector / sticky-block
#   4. Run one hermes model session (45-min wall clock) or bus session (15 min)
#   5. BUDGET INCREMENT (sessions cost tokens regardless of outcome)
#   6. Mechanical validation (commit, checkbox, FRESH gate, tests, push)
#   7. One retry on failure → BLOCKED-and-exit (sticky, notified once)
#   8. Commit state each iteration + Telegram notification
#
# Usage:
#   bash tools/foreman.sh                    # chain mode
#   touch FOREMAN_HALT                       # stop all future runs
#   rm FOREMAN_HALT                          # resume
#
# Env overrides:
#   FOREMAN_PROJECT_DIR       default: /home/fictive/runewake
#   FOREMAN_MODEL             default: deepseek/deepseek-v4-flash
#   FOREMAN_TIMEOUT           default: 2700 (45 min)
#   FOREMAN_DAILY_BUDGET      default: 48
#   FOREMAN_TELEGRAM_TARGET   default: telegram:Runewake
#   FOREMAN_GODOT_BIN         default: /home/fictive/Godot_v4.3-stable_linux.x86_64
#
set -euo pipefail

# ── Config ───────────────────────────────────────────────────────────────────
PROJECT_DIR="${FOREMAN_PROJECT_DIR:-$HOME/runewake}"
FOREMAN_MODEL="${FOREMAN_MODEL:-deepseek/deepseek-v4-flash}"
FOREMAN_TIMEOUT="${FOREMAN_TIMEOUT:-2700}"
DAILY_BUDGET=48
TELEGRAM_TARGET="${FOREMAN_TELEGRAM_TARGET:-telegram:Runewake}"
GODOT_BIN="${FOREMAN_GODOT_BIN:-$HOME/.local/bin/godot}"
# Python interpreter for the pipeline test gate — MUST be the env with pipeline deps
PYTHON_BIN="${FOREMAN_PYTHON_BIN:-$HOME/.hermes/hermes-agent/venv/bin/python}"
# Bus config
BUS_DIR="${PROJECT_DIR}/bus"
BUS_IN="${BUS_DIR}/claude_to_hermes.md"
BUS_OUT="${BUS_DIR}/hermes_to_claude.md"
# NOTE: trust is now based on bus-only commits (any author), not a hardcoded identity.
# The old CLAUDE_COMMITTER constant is removed — see bus_msg_trusted().
BUS_TIMEOUT="${FOREMAN_BUS_TIMEOUT:-900}"  # 15 min

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

# Parked heartbeat — write timestamp + reason at most once per 60 min
# so Claude's remote eyes can tell "parked" from "dead".
write_parked_heartbeat() {
  local reason="$1"
  local now_epoch
  now_epoch=$(date +%s)
  local last_heartbeat
  last_heartbeat=$(get_state "last_heartbeat_epoch" 2>/dev/null || echo "0")
  last_heartbeat=${last_heartbeat:-0}
  local elapsed=$(( now_epoch - last_heartbeat ))
  if [[ "${elapsed}" -lt 3600 ]] && [[ "${last_heartbeat}" -gt 0 ]]; then
    return 0  # within hourly quiet window
  fi
  set_state "last_heartbeat_epoch" "${now_epoch}"
  set_state "last_heartbeat_reason" "\"${reason}\""
  local now_iso
  now_iso=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
  ok "Heartbeat: parked — ${reason} (${now_iso})"
  # Git-commit and push so Claude's remote view sees every heartbeat immediately.
  # Without this, the state.json change stays local and never reaches GitHub.
  cd "${PROJECT_DIR}"
  git add "${STATE_FILE}" 2>/dev/null || true
  if ! git diff --cached --quiet 2>/dev/null; then
    git commit -m "foreman: heartbeat — ${reason}" 2>/dev/null || true
    if ! git push origin main 2>/dev/null; then
      warn "Heartbeat push failed — retrying once after 5s"
      sleep 5
      if ! git push origin main 2>/dev/null; then
        warn "Heartbeat push failed after retry — state change remains local only"
      fi
    fi
  fi
}

# Find the top unchecked task in TASKS_QUEUE.md
find_top_task() {
  python3 -c "
import re, sys
with open('${QUEUE_FILE}') as f:
    content = f.read()
lines = content.split('\n')
in_queue = False
for i, line in enumerate(lines):
    if line.strip().startswith('## Queue'):
        in_queue = True
        continue
    if in_queue and line.strip().startswith('## '):
        break
    if in_queue:
        m = re.match(r'^\s*-\s*\[\s*\]\s*(TASK-\S+)\s*[—–-]?\s*(.*)', line)
        if m:
            task_id = m.group(1)
            desc = m.group(2).strip()
            # Collect continuation lines: indented lines following the header,
            # up to the next blank line or the next '- [' task marker.
            j = i + 1
            while j < len(lines):
                nl = lines[j]
                if nl.strip() == '' or re.match(r'^\s*-\s*\[', nl):
                    break
                if re.match(r'^\s+', nl):  # continuation (starts with whitespace)
                    desc += ' ' + nl.strip()
                j += 1
            # Single-space the full description
            desc = re.sub(r'\s+', ' ', desc).strip()
            print(f'{task_id}|{desc}')
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
pattern = re.compile(r'^\\s*-\\s*\\[\\s*x\\s*\\]\\s*\\b' + re.escape('${task_id}') + r'\\b')
for line in content.split('\\n'):
    if pattern.search(line):
        print('done')
        break
" 2>/dev/null || echo ""
}

# Run hermes agent session with timeout
# Run a hermes oneshot with retry-with-backoff (PART D — provider resilience).
# 3 attempts total; waits 60s/180s between attempts; if all 3 fail with a
# transient signature (provider outage, no new commit), wait 300s once more
# before returning so the provider can recover before classification.
# Usage: run_session_with_retry <timeout_secs> <prompt> <label>
run_session_with_retry() {
  local session_timeout="$1" prompt="$2" label="$3"
  local attempt=1 max_attempts=3
  local backoff=(60 180 300)
  local output="" head_before head_after retryable

  while [[ "${attempt}" -le "${max_attempts}" ]]; do
    if [[ "${attempt}" -gt 1 ]]; then
      local wait_s="${backoff[$((attempt - 2))]}"
      warn "${label}: attempt ${attempt}/${max_attempts} — waiting ${wait_s}s before retry"
      sleep "${wait_s}"
    fi

    head_before=$(git rev-parse HEAD 2>/dev/null || echo "")
    cd "${PROJECT_DIR}"
    output=$(timeout "${session_timeout}" "${HERMES_BIN}" -z "${prompt}" 2>&1 || echo "HERMES_EXIT_CODE=$?")
    head_after=$(git rev-parse HEAD 2>/dev/null || echo "")

    # Retryable only when: no new commit AND (non-zero exit OR transient
    # signature in the output tail). A commit means real work happened.
    retryable=0
    if [[ "${head_after}" == "${head_before}" ]]; then
      if echo "${output}" | grep -q "HERMES_EXIT_CODE=" || is_transient_output "${output}"; then
        retryable=1
      fi
    fi

    if [[ "${retryable}" -eq 0 ]]; then
      echo "${output}"
      return 0
    fi

    if [[ "${attempt}" -lt "${max_attempts}" ]]; then
      # Dead session debris — clean the tree before the next attempt
      warn "${label}: attempt ${attempt} transient — cleaning dead-session tree litter"
      git checkout -- . 2>/dev/null || true
      git clean -fd 2>/dev/null || true
    else
      warn "${label}: all ${max_attempts} attempts transient — waiting ${backoff[2]}s final recovery window"
      sleep "${backoff[2]}"
    fi
    attempt=$((attempt + 1))
  done

  echo "${output}"
}

run_hermes_session() {
  local task_id="$1" task_desc="$2"
  local prompt="Implement the top unchecked task from ${QUEUE_FILE}: ${task_id} — ${task_desc}. Standard protocol in one session: implement the task, run the harness + gate, commit with message '${task_id}: ${task_desc}', push, write DONE line in HERMES_STATUS.md, then stop. Work from ${PROJECT_DIR}."

  run_session_with_retry "${FOREMAN_TIMEOUT}" "${prompt}" "${task_id}"
}

# ── Transient classifier ─────────────────────────────────────────────────────
# Scans the session output tail (where timeout lines land) for provider-outage
# signatures. Case-insensitive. Returns 0 (TRANSIENT) on any match.
# Usage: is_transient_output "<full session output>"
is_transient_output() {
  local output="$1"
  echo "${output}" | tail -50 | grep -Eiq "connect timeout|connection (error|refused|reset)|timed? ?out|rate limit|overloaded|5[0-9][0-9] "
}

# ── Bus helpers ──────────────────────────────────────────────────────────────
# Get the highest MSG seq across both main and claude-bus bus files
bus_max_seq() {
  local main_content bus_content
  main_content=$(cat "${BUS_IN}" 2>/dev/null || echo "")
  bus_content=$(git show origin/claude-bus:bus/claude_to_hermes.md 2>/dev/null || echo "")
  python3 -c "
import re, sys
content = '''${main_content}
${bus_content}'''
seqs = [int(m) for m in re.findall(r'^## MSG (\d+)', content, re.M)]
print(max(seqs) if seqs else 0)
"
}

# Extract message blocks with seq > last from both branches
bus_new_messages() {
  local last="$1"
  local main_content bus_content
  main_content=$(cat "${BUS_IN}" 2>/dev/null || echo "")
  bus_content=$(git show origin/claude-bus:bus/claude_to_hermes.md 2>/dev/null || echo "")
  python3 -c "
import re, sys
content = '''${main_content}
${bus_content}'''
blocks = re.split(r'(?m)^(?=## MSG )', content)
seen = set()
out = []
for b in blocks:
    m = re.match(r'## MSG (\d+)', b)
    if m and int(m.group(1)) > int('${last}'):
        seq = int(m.group(1))
        if seq not in seen:
            seen.add(seq)
            out.append(b.strip())
print('\n\n'.join(out))
"
}

# Trust check: accept MSG when the commit introducing it touches ONLY files under bus/
# Push access to this repo is the credential. Log the author for reporting.
# Returns "trusted", "untrusted:no_commit_found", or "untrusted:touches_non_bus_files"
bus_msg_trusted() {
  local seq="$1"
  local commit author files_touched non_bus_files
  commit=$(cd "${PROJECT_DIR}" && git log --all --format="%H" -S "## MSG ${seq}" -- "bus/claude_to_hermes.md" 2>/dev/null | head -1 || echo "")
  if [[ -z "${commit}" ]]; then
    echo "untrusted:no_commit_found"
    return
  fi
  author=$(cd "${PROJECT_DIR}" && git log --format="%an <%ae>" "${commit}" -1 2>/dev/null || echo "unknown")
  files_touched=$(cd "${PROJECT_DIR}" && git diff-tree --no-commit-id -r --name-only "${commit}" 2>/dev/null || echo "")
  non_bus_files=$(echo "${files_touched}" | grep -v "^bus/" || true)
  if [[ -n "${non_bus_files}" ]]; then
    echo "untrusted:touches_non_bus_files|${author}|${non_bus_files}"
  else
    echo "trusted|${author}"
  fi
}

# Run a bus-session (15-min wall clock, counts half a budget session)
run_bus_session() {
  local messages="$1" max_seq="$2"
  local prompt="New message(s) from Claude the orchestrator (${BUS_IN}):\n\n${messages}\n\nStanding instruction: Reply to Claude the orchestrator: act on small items directly (queue edits, file writes, answers); append large work items to TASKS_QUEUE.md as proper tasks instead of doing them; write your reply as a new MSG in bus/hermes_to_claude.md; commit 'bus: reply to MSG ${max_seq}', push, stop. Work from ${PROJECT_DIR}."

  run_session_with_retry "${BUS_TIMEOUT}" "${prompt}" "bus-session"
}

# ── Lock (PID file, no FD inheritance into children) ─────────────────────────
if [[ -f "${LOCK_PID_FILE}" ]]; then
  LOCK_PID=$(cat "${LOCK_PID_FILE}" 2>/dev/null || echo "")
  if [[ -n "${LOCK_PID}" ]] && kill -0 "${LOCK_PID}" 2>/dev/null; then
    echo "foreman already running (PID ${LOCK_PID}) — exiting"
    exit 0
  else
    warn "Stale lock cleared (PID ${LOCK_PID} not alive)"
    rm -f "${LOCK_PID_FILE}"
  fi
fi
echo "$$" > "${LOCK_PID_FILE}"
trap 'rm -f "${LOCK_PID_FILE}"' EXIT

# ── Main ─────────────────────────────────────────────────────

header "Foreman — chain mode"
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

# ── Chain loop ──────────────────────────────────────────────────
# The PID lock above is acquired ONCE and held for the entire chain
# (mutual exclusion for all iterations). Every other circuit breaker
# (HALT, pull, bus, budget, sticky-block, transient) re-runs at the
# top of each chained iteration.
CHAIN_RUNNING=1
CONSECUTIVE_SUCCESSES=0
ITERATION=0

while [[ "${CHAIN_RUNNING}" -eq 1 ]]; do
  ITERATION=$((ITERATION + 1))
  header "Iteration ${ITERATION} — chain mode"

# ── 1. Circuit breaker: HALT file ────────────────────────────────────────────
if [[ -f "${HALT_FILE}" ]]; then
  warn "HALT file found at ${HALT_FILE}"
  telegram_text "FOREMAN HALTED — ${HALT_FILE} exists"
  write_parked_heartbeat "HALT"
  exit 1
fi

# ── 1b. Sync with origin (bus messages + queue edits land here) ──────────────
git pull --ff-only origin main 2>/dev/null || warn "git pull failed (network or dirty tree)"
# Also fetch claude-bus for the dual-branch bus watch (ignore if absent)
git fetch origin claude-bus 2>/dev/null || true

# ── 1c. Bus check (before queue work) ────────────────────────────────────────
BUS_LAST_SEQ=$(get_state "bus_last_seq")
BUS_LAST_SEQ=${BUS_LAST_SEQ:-0}
BUS_MAX_SEQ=$(bus_max_seq)
if [[ "${BUS_MAX_SEQ}" -gt "${BUS_LAST_SEQ}" ]]; then
  header "Bus: new messages ${BUS_LAST_SEQ+1}..${BUS_MAX_SEQ}"
  # Trust check per message — new trust: bus-only commits from any author
  TRUSTED_MSGS=""
  UNTRUSTED_FLAG=0
  PROCESSED_AUTHORS=""
  MSGS_TO_PROCESS=""
  for s in $(seq $((BUS_LAST_SEQ + 1)) "${BUS_MAX_SEQ}"); do
    TRUST_RESULT=$(bus_msg_trusted "${s}")
    case "${TRUST_RESULT}" in
      trusted\|*)
        AUTHOR="${TRUST_RESULT#trusted|}"
        PROCESSED_AUTHORS="${PROCESSED_AUTHORS}MSG ${s}: ${AUTHOR}, "
        # Extract this message
        MSG_BLOCK=$(bus_new_messages $((s - 1)))
        MSGS_TO_PROCESS="${MSGS_TO_PROCESS}${MSG_BLOCK}\\n\\n"
        ;;
      untrusted:touches_non_bus_files\|*)
        # Flag but don't process — commit touched non-bus files
        REST="${TRUST_RESULT#untrusted:touches_non_bus_files|}"
        AUTHOR="${REST%%|*}"
        FILES="${REST#*|}"
        UNTRUSTED_FLAG=1
        warn "Bus MSG ${s} by ${AUTHOR} touches non-bus files: ${FILES} — flagged, not processed"
        ;;
      *)
        UNTRUSTED_FLAG=1
        warn "Bus MSG ${s} untrusted: ${TRUST_RESULT} — ignored"
        ;;
    esac
  done
  if [[ "${UNTRUSTED_FLAG}" -eq 1 ]]; then
    telegram_text "⚠️ Bus: untrusted/flagged message(s) in claude_to_hermes.md — ignored/flagged"
  fi
  if [[ -n "${MSGS_TO_PROCESS}" ]]; then
    header "Running bus-session"
    ok "Processing messages from: ${PROCESSED_AUTHORS}"
    BUS_OUTPUT=$(run_bus_session "${MSGS_TO_PROCESS}" "${BUS_MAX_SEQ}")
    echo "${BUS_OUTPUT}" | tail -5
    # bus-session counts half a budget session
    BUS_SESSION_COUNT=$(get_state "bus_session_count")
    BUS_SESSION_COUNT=$((BUS_SESSION_COUNT + 1))
    set_state "bus_session_count" "${BUS_SESSION_COUNT}"
    ok "Bus session done — total bus sessions: ${BUS_SESSION_COUNT}"
  fi
  set_state "bus_last_seq" "${BUS_MAX_SEQ}"
fi

# ── 2. Circuit breaker: Daily budget (in half-sessions) ──────────────────────
TODAY=$(today)
STATE_DATE=$(get_state "date")
SESSION_COUNT=$(get_state "session_count")
RETRY_TASK_ID=$(get_state "retry_task_id")
RETRY_COUNT=$(get_state "retry_count")
BLOCKED_NOTIFIED=$(get_state "blocked_notified")
CONSECUTIVE_TRANSIENTS=$(get_state "consecutive_transients")
TRANSIENT_NOTIFIED=$(get_state "transient_notified")
BUS_SESSION_COUNT=$(get_state "bus_session_count")
BUS_SESSION_COUNT=${BUS_SESSION_COUNT:-0}
NO_PROGRESS_COUNT=$(get_state "no_progress_count")
VALIDATED_COUNT=$(get_state "validated_count")
CONSECUTIVE_TRANSIENTS=${CONSECUTIVE_TRANSIENTS:-0}
TRANSIENT_NOTIFIED=${TRANSIENT_NOTIFIED:-False}
NO_PROGRESS_COUNT=${NO_PROGRESS_COUNT:-0}
VALIDATED_COUNT=${VALIDATED_COUNT:-0}

if [[ "${STATE_DATE}" != "${TODAY}" ]]; then
  # Telemetry: log yesterday's activity to HERMES_STATUS.md
  if [[ -n "${STATE_DATE}" ]] && [[ "${STATE_DATE}" != "null" ]] && [[ "${SESSION_COUNT}" -gt 0 || "${BUS_SESSION_COUNT}" -gt 0 ]]; then
    echo "- ${TODAY}: TEMPO — ${SESSION_COUNT} sessions yesterday, ${VALIDATED_COUNT} validated." >> "${PROJECT_DIR}/HERMES_STATUS.md"
  fi
  set_state "date" "'"${TODAY}"'"
  set_state "session_count" 0
  set_state "bus_session_count" 0
  set_state "retry_count" 0
  set_state "retry_task_id" "\"\""
  set_state "blocked_notified" "False"
  set_state "consecutive_transients" 0
  set_state "transient_notified" "False"
  set_state "no_progress_count" 0
  set_state "validated_count" 0
  SESSION_COUNT=0
  BUS_SESSION_COUNT=0
  BLOCKED_NOTIFIED="False"
  CONSECUTIVE_TRANSIENTS=0
  TRANSIENT_NOTIFIED="False"
  NO_PROGRESS_COUNT=0
  VALIDATED_COUNT=0
fi

# Budget check: each full session = 2 halves, each bus session = 1 half
SPENT_HALVES=$(( SESSION_COUNT * 2 + BUS_SESSION_COUNT ))
BUDGET_HALVES=$(( DAILY_BUDGET * 2 ))
if [[ "${SPENT_HALVES}" -ge "${BUDGET_HALVES}" ]]; then
  SPENT_FRAC=$(( SESSION_COUNT ))  # whole part
  if [[ $(( BUS_SESSION_COUNT % 2 )) -eq 1 ]]; then
    SPENT_FRAC="${SPENT_FRAC}.5"
  fi
  warn "Daily budget spent: ${SESSION_COUNT} full + ${BUS_SESSION_COUNT} bus = ${SPENT_FRAC}/${DAILY_BUDGET}"
  telegram_text "Budget spent — ${SPENT_FRAC}/${DAILY_BUDGET} sessions today"
  write_parked_heartbeat "budget_spent"
  exit 0
fi

# Display budget info
SPENT_FRAC="${SESSION_COUNT}"
if [[ $(( BUS_SESSION_COUNT % 2 )) -eq 1 ]]; then
  SPENT_FRAC="${SPENT_FRAC}.5"
fi
info "Budget: ${SPENT_FRAC}/${DAILY_BUDGET} (${SESSION_COUNT} full + ${BUS_SESSION_COUNT} bus, ${SPENT_HALVES}/${BUDGET_HALVES} halves)"

# ── 3. Fable inbox ───────────────────────────────────────────────────────────
header "Fable inbox"
FOREMAN_CALLER=1 bash "${PROJECT_DIR}/tools/inbox_apply.sh"

# ── 4. Read queue ────────────────────────────────────────────────────────────
TOP_TASK=$(find_top_task)

if [[ -z "${TOP_TASK}" ]]; then
  ok "Queue is empty"
  telegram_text "Queue empty — no tasks remaining"
  write_parked_heartbeat "empty_queue"
  exit 0
fi

TASK_ID=$(echo "${TOP_TASK}" | cut -d'|' -f1)
TASK_DESC=$(echo "${TOP_TASK}" | cut -d'|' -f2-)
info "Top task: ${TASK_ID}"

# ── 3b. PARK check — two failed attempts => park this task, advance, never freeze ──
if [[ "${TASK_ID}" == "${RETRY_TASK_ID}" ]] && [[ "${RETRY_COUNT}" -ge 2 ]]; then
  fail "Parking ${TASK_ID} after ${RETRY_COUNT} attempts — advancing to next task"
  python3 - "${TASK_ID}" "${PROJECT_DIR}/TASKS_QUEUE.md" <<'PYPARK'
import sys,re
tid,p=sys.argv[1],sys.argv[2]
s=open(p).read()
open(p,'w').write(re.sub(r'^- \[ \] '+re.escape(tid)+r'','- [!] '+tid,s,count=1,flags=re.M))
PYPARK
  echo "- $(today): PARKED ${TASK_ID} — failed 2 attempts, auto-parked by foreman; awaiting Fable." >> "${PROJECT_DIR}/HERMES_STATUS.md"
  git -C "${PROJECT_DIR}" add TASKS_QUEUE.md HERMES_STATUS.md 2>/dev/null || true
  git -C "${PROJECT_DIR}" -c user.name="Claude" -c user.email="claude@runewake.game" commit -q -m "foreman: park ${TASK_ID} after 2 failed attempts, advancing to next task" 2>/dev/null || true
  git -C "${PROJECT_DIR}" push -q origin main 2>/dev/null || true
  telegram_text "Parked ${TASK_ID} after 2 tries — moving to the next task."
  set_state "retry_count" 0
  set_state "retry_task_id" "\"\""
  set_state "blocked_notified" "False"
  continue
fi

# ── 4. Execute model session ─────────────────────────────────────────────────
header "Running: ${TASK_ID}"
info "Model: ${FOREMAN_MODEL}  Timeout: ${FOREMAN_TIMEOUT}s"

# Capture HEAD before the session so we can detect new commits
CURRENT_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")

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

# ── FIX #6 (FIXED): Transient failure classification ──────────────────────────
# Provider outage (no commit, transient signature in session output) must NOT
# consume task retries. Real failures (work produced but validation failed)
# keep the normal retry-then-block path.
#
# BUGFIX (TASK-BUS): the old classifier gated the signature grep behind
# `[[ -z "${WORKTREE_CHANGES}" ]]` — the DSL-2 session died mid-work with
# "Connect timeout, please try again later." but left partial uncommitted
# engine changes in the tree (PREVENT_DAMAGE implementation). The tree-changes
# precondition went FALSE, the grep never ran, and the timeout was charged as
# a REAL retry (phantom). FIX: tree changes do NOT gate the transient scan.
# The no-commit precondition is sufficient; the session output signature tells
# the truth. If the session left partial work, that's debris from a dead
# provider connection — the tree is cleaned up on transient handling.
TRANSIENT=0
POST_SESSION_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")
WORKTREE_CHANGES=$(git status --porcelain 2>/dev/null | head -20 || echo "")
if [[ "${POST_SESSION_HEAD}" == "${CURRENT_HEAD}" ]]; then
  # No new commit — scan the session output tail for transient signatures.
  # Tree changes do NOT gate this: a connect-timeout mid-session leaves
  # partial work behind (DSL-2 07:4x case). The timeout signature is truth.
  if is_transient_output "${SESSION_OUTPUT}"; then
    TRANSIENT=1
  fi
fi

if [[ "${TRANSIENT}" -eq 1 ]]; then
  CONSECUTIVE_TRANSIENTS=$((CONSECUTIVE_TRANSIENTS + 1))
  set_state "consecutive_transients" "${CONSECUTIVE_TRANSIENTS}"
  warn "Transient skip: ${TASK_ID} (consecutive ${CONSECUTIVE_TRANSIENTS})"

  # Clean up partial worktree changes from the dead session
  if [[ -n "${WORKTREE_CHANGES}" ]]; then
    warn "Cleaning up ${WORKTREE_CHANGES} partial worktree changes from dead session"
    git checkout -- . 2>/dev/null || true
    git clean -fd 2>/dev/null || true
  fi

  # Alert once per day on the 4th consecutive transient
  if [[ "${CONSECUTIVE_TRANSIENTS}" -ge 4 ]] && [[ "${TRANSIENT_NOTIFIED}" != "True" ]]; then
    telegram_text "⚠️ provider flaky — ${TASK_ID} skipped ${CONSECUTIVE_TRANSIENTS}x, still retrying hourly"
    set_state "transient_notified" "True"
    TRANSIENT_NOTIFIED="True"
  fi

  # Log the transient skip
  {
    echo "=== foreman: $(date -u '+%Y-%m-%dT%H:%M:%SZ') ${TASK_ID} transient ==="
    echo "Session: ${SESSION_COUNT}/${DAILY_BUDGET}"
    echo "Transient: consecutive ${CONSECUTIVE_TRANSIENTS}"
    echo "--- session tail ---"
    echo "${SESSION_OUTPUT_SNAPSHOT}"
    echo "========================================"
  } >> "${LAST_RUN_LOG}"
  tail -50 "${LAST_RUN_LOG}" > "${LAST_RUN_LOG}.tmp" 2>/dev/null || true && mv "${LAST_RUN_LOG}.tmp" "${LAST_RUN_LOG}" 2>/dev/null || true

  cd "${PROJECT_DIR}"
  git add "${STATE_FILE}" "${LAST_RUN_LOG}" 2>/dev/null || true
  if ! git diff --cached --quiet 2>/dev/null; then
    git commit -m "foreman: state after ${TASK_ID} (transient)" 2>/dev/null || true
    git push origin main 2>/dev/null || true
  fi

  info "Transient skip logged — exiting 0 (task NOT charged a retry)"
  write_parked_heartbeat "transient_skip"
  exit 0
fi

# ── FIX #4: finish_task.sh — replaces old capture-regen + validation + test + commit blocks ──
header "Running finish_task.sh"
FINISH_EXIT=0
NEW_COMMIT_SHA=""
FINISH_OUTPUT=$(bash "${PROJECT_DIR}/tools/finish_task.sh" "${TASK_ID}" "${TASK_DESC}" 2>&1) || FINISH_EXIT=$?
echo "${FINISH_OUTPUT}"
if [[ "${FINISH_EXIT}" -eq 0 ]]; then
  VALIDATION_FAILED=0
  NEW_COMMIT_SHA=$(git rev-parse HEAD 2>/dev/null || echo "")
  set_state "last_task_id" "\"${TASK_ID}\""
  set_state "last_commit_sha" "\"${NEW_COMMIT_SHA}\""
  set_state "retry_count" 0
  set_state "retry_task_id" "\"\""
  set_state "blocked_notified" "False"
  set_state "consecutive_transients" 0
  set_state "transient_notified" "False"
  VALIDATED_COUNT=$((VALIDATED_COUNT + 1))
  set_state "validated_count" "${VALIDATED_COUNT}"
  write_parked_heartbeat "running_${TASK_ID}"
  ok "Task ${TASK_ID} complete! (${SESSION_COUNT}/${DAILY_BUDGET})"
  telegram_text "${TASK_ID} done (${SESSION_COUNT}/${DAILY_BUDGET})"
  # Attach latest capture if available
  LATEST_CAPTURE=$(ls -t "${CAPTURE_DIR}"/*.png 2>/dev/null | head -1)
  if [[ -n "${LATEST_CAPTURE}" ]]; then
    telegram_photo "${LATEST_CAPTURE}" "${TASK_ID} — capture"
  fi
  VALIDATION_FAILED=0
else
  VALIDATION_FAILED=1
  VALIDATION_REASONS="finish_task_failure"
  if [[ "${TASK_ID}" == "${RETRY_TASK_ID}" ]]; then
      NEW_RETRY=$((RETRY_COUNT + 1))
    else
      NEW_RETRY=1
    fi
    warn "Attempt ${NEW_RETRY} failed for ${TASK_ID} (finish_task.sh)"
    telegram_text "Attempt ${NEW_RETRY} failed: ${TASK_ID}"
    set_state "retry_count" "${NEW_RETRY}"
    set_state "retry_task_id" "\"${TASK_ID}\""
    set_state "blocked_notified" "False"
    POST_SESSION_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")
    if [[ -n "${POST_SESSION_HEAD}" ]] && [[ "${POST_SESSION_HEAD}" != "${CURRENT_HEAD}" ]]; then
      git fetch origin main 2>/dev/null || true
      git reset --hard origin/main 2>/dev/null || true
      git checkout -- . 2>/dev/null || true
      git clean -fd 2>/dev/null || true
    fi
fi

# ── 5. State commit ───────────────────────────────────────────────────────────
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
  echo "========================================"
} >> "${LAST_RUN_LOG}"
tail -50 "${LAST_RUN_LOG}" > "${LAST_RUN_LOG}.tmp" 2>/dev/null || true && mv "${LAST_RUN_LOG}.tmp" "${LAST_RUN_LOG}" 2>/dev/null || true

cd "${PROJECT_DIR}"
git add "${STATE_FILE}" "${LAST_RUN_LOG}" 2>/dev/null || true
if ! git diff --cached --quiet 2>/dev/null; then
  git commit -m "foreman: state after ${TASK_ID} (${OUTCOME_LABEL})" 2>/dev/null || true
  git push origin main 2>/dev/null || true
fi

# ── 6. No-progress breaker ────────────────────────────────────────────────────
NEXT_TOP_LINE=$(find_top_task 2>/dev/null || echo "")
CURRENT_TOP=""
if [[ -n "${NEXT_TOP_LINE}" ]]; then
  CURRENT_TOP=$(echo "${NEXT_TOP_LINE}" | cut -d'|' -f1)
fi
if [[ -n "${CURRENT_TOP}" ]] && [[ "${CURRENT_TOP}" == "${TASK_ID}" ]]; then
  NO_PROGRESS_COUNT=$((NO_PROGRESS_COUNT + 1))
  set_state "no_progress_count" "${NO_PROGRESS_COUNT}"
  warn "No progress: same task ${CURRENT_TOP} still top — ${NO_PROGRESS_COUNT}/3"
  if [[ "${NO_PROGRESS_COUNT}" -ge 3 ]]; then
    warn "No progress after 3 consecutive sessions — creating HALT"
    telegram_text "🚨 No-progress: 3 consecutive sessions without queue advancement — creating HALT"
    touch "${HALT_FILE}"
    exit 1
  fi
else
  set_state "no_progress_count" 0
  NO_PROGRESS_COUNT=0
fi

# ── 7. Chain decision ─────────────────────────────────────────────────────────
# Success → chain to the next task if queue + budget remain. Every 3
# consecutive successes forces a 15-min cool-down (Claude's live-review
# window), then resumes. ANY failure/retry/block ends the chain — the
# circuit breakers above (HALT, pull, bus, budget, sticky-block, transient)
# exit directly and thereby end the chain too.
if [[ "${VALIDATION_FAILED}" -eq 0 ]]; then
  CONSECUTIVE_SUCCESSES=$((CONSECUTIVE_SUCCESSES + 1))
  ok "Task ${TASK_ID} complete — consecutive successes: ${CONSECUTIVE_SUCCESSES}"

  # Mandatory cool-down after every 3 consecutive successes
  if [[ "${CONSECUTIVE_SUCCESSES}" -ge 3 ]]; then
    warn "3 consecutive successes — 5-min cool-down (Claude review window)"
    telegram_text "3 tasks done back-to-back — 5-min cool-down, then resume"
    sleep 300
    CONSECUTIVE_SUCCESSES=0
    ok "Cool-down over — resuming chain"
  fi

  # Chain continuation gate: queue + budget (halves)
  NEXT_TOP=$(find_top_task)
  SESSION_COUNT_NOW=$(get_state "session_count")
  BUS_SESSION_COUNT_NOW=$(get_state "bus_session_count")
  BUS_SESSION_COUNT_NOW=${BUS_SESSION_COUNT_NOW:-0}
  SPENT_HALVES_NOW=$(( SESSION_COUNT_NOW * 2 + BUS_SESSION_COUNT_NOW ))
  BUDGET_HALVES_NOW=$(( DAILY_BUDGET * 2 ))
  if [[ -z "${NEXT_TOP}" ]]; then
    ok "Queue empty — chain ends"
    CHAIN_RUNNING=0
  elif [[ "${SPENT_HALVES_NOW}" -ge "${BUDGET_HALVES_NOW}" ]]; then
    info "Budget spent (${SPENT_HALVES_NOW}/${BUDGET_HALVES_NOW} halves) — chain ends"
    CHAIN_RUNNING=0
  else
    info "Chaining — budget ${SPENT_HALVES_NOW}/${BUDGET_HALVES_NOW} halves, next: ${NEXT_TOP%%|*}"
  fi
else
  warn "Chain ends — ${TASK_ID} ${OUTCOME_LABEL}"
  CHAIN_RUNNING=0
fi

done  # while [[ "${CHAIN_RUNNING}" -eq 1 ]]

# ── Final exit ──────────────────────────────────────────────────
if [[ "${VALIDATION_FAILED}" -eq 0 ]]; then
  exit 0
else
  exit 1
fi