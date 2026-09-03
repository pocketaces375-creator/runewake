#!/usr/bin/env bash
# tools/progress_ping.sh — Post progress % to Runewake group every 2 hours
#
# Cron: 5 */2 * * *
# Posts ONE plain message to telegram:Runewake with:
#   - Percent complete (done / (done+open) from TASKS_QUEUE.md)
#   - Most recent TASK- commit, age
#   - Playable status from artifacts/PLAYABLE.json (if it exists)
#   - Stuck/stopped/idle warning if applicable

set -euo pipefail

PROJECT_DIR="${FOREMAN_PROJECT_DIR:-$HOME/runewake}"
HERMES_BIN="${HOME}/.local/bin/hermes"
QUEUE_FILE="${PROJECT_DIR}/TASKS_QUEUE.md"
STATE_FILE="${PROJECT_DIR}/tools/foreman_state.json"
PLAYABLE_FILE="${PROJECT_DIR}/artifacts/PLAYABLE.json"
TELEGRAM_TARGET="telegram:Runewake"

cd "${PROJECT_DIR}"

# ── Count done/open tasks from TASKS_QUEUE.md ──
DONE_COUNT=$(grep -cE '^- \[x\] TASK-' "${QUEUE_FILE}" 2>/dev/null || echo 0)
OPEN_COUNT=$(grep -cE '^- \[ \] TASK-' "${QUEUE_FILE}" 2>/dev/null || echo 0)
TOTAL=$(( DONE_COUNT + OPEN_COUNT ))

if [[ "${TOTAL}" -eq 0 ]]; then
  PCT="N/A"
  DONE_COUNT=0
  OPEN_COUNT=0
else
  PCT=$(( DONE_COUNT * 100 / TOTAL ))
fi

# Find newest TASK- commit (skip generic commits like 'mark [x] + DONE entry' and 'foreman:')
NEWEST_TASK_LINE=$(git log --oneline --grep='TASK-' --format='%s|%ct' 2>/dev/null | grep -v 'mark \[x\] + DONE entry' | grep -v '^foreman:' | head -1 || echo "")
NEWEST_TASK_COMMIT=$(echo "${NEWEST_TASK_LINE}" | cut -d'|' -f1)
NEWEST_TASK_TIME=$(echo "${NEWEST_TASK_LINE}" | cut -d'|' -f2)

if [[ -n "${NEWEST_TASK_COMMIT}" ]] && [[ "${NEWEST_TASK_TIME}" -gt 0 ]]; then
  NOW=$(date +%s)
  AGE_MIN=$(( (NOW - NEWEST_TASK_TIME) / 60 ))
  # Extract plain-English description: strip TASK-ID prefix and any trailing tool references
  LAST_DESC=$(echo "${NEWEST_TASK_COMMIT}" | sed 's/^TASK-[A-Z0-9-]*: *//' | sed 's/\. tools\/.*//' | xargs)
  # If desc is too short, use the full commit message minus the task ID
  if [[ -z "${LAST_DESC}" ]]; then
    LAST_DESC=$(echo "${NEWEST_TASK_COMMIT}" | sed 's/^.*: //')
  fi
else
  LAST_DESC="nothing yet"
  AGE_MIN="?"
fi

# ── Check PLAYABLE.json ──
PLAYABLE=""
if [[ -f "${PLAYABLE_FILE}" ]]; then
  PLAYABLE_VAL=$(python3 -c "
import json
try:
    with open('${PLAYABLE_FILE}') as f:
        d = json.load(f)
    print('yes' if d.get('playable', False) else 'no')
except:
    print('unknown')
" 2>/dev/null || echo "unknown")
  PLAYABLE="Playable end to end: ${PLAYABLE_VAL}"
fi

# ── Check stuck/stopped/idle ──
EXTRA=""
FOREMAN_HALT="${PROJECT_DIR}/FOREMAN_HALT"
if [[ -f "${FOREMAN_HALT}" ]]; then
  EXTRA="stopped, needs restart"
elif [[ -f "${STATE_FILE}" ]]; then
  HEARTBEAT_REASON=$(python3 -c "
import json
try:
    with open('${STATE_FILE}') as f:
        d = json.load(f)
    print(d.get('last_heartbeat_reason', ''))
except:
    print('')
" 2>/dev/null || echo "")
  if echo "${HEARTBEAT_REASON}" | grep -qi "block"; then
    EXTRA="stuck on one task"
  fi
fi

# If no open tasks, indicate idle
if [[ "${OPEN_COUNT}" -eq 0 ]] && [[ "${TOTAL}" -gt 0 ]]; then
  EXTRA="queue complete, waiting for new tasks"
elif [[ "${OPEN_COUNT}" -eq 0 ]] && [[ "${TOTAL}" -eq 0 ]]; then
  EXTRA="idle, waiting for tasks"
fi

# ── Build message ──
MSG="Runewake is about ${PCT}% done (${DONE_COUNT} of ${TOTAL} pieces built). Last finished: ${LAST_DESC}, ${AGE_MIN} min ago."

if [[ -n "${PLAYABLE}" ]]; then
  MSG="${MSG} ${PLAYABLE}."
fi

if [[ -n "${EXTRA}" ]]; then
  MSG="${MSG} ${EXTRA}."
fi

# ── Post to group ──
if [[ -x "${HERMES_BIN}" ]]; then
  "${HERMES_BIN}" send --to "${TELEGRAM_TARGET}" "${MSG}" 2>/dev/null || true
fi

echo "${MSG}"