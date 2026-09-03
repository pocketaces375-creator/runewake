#!/usr/bin/env bash
# tools/progress_ping.sh — Post progress % to Runewake Telegram group
#
# Cron: 5 */2 * * *
# Posts ONE plain message with progress stats.
#
# Usage:
#   bash tools/progress_ping.sh                    # post to Runewake group
#   bash tools/progress_ping.sh --dry-run          # print to stdout only

set -euo pipefail

PROJECT_DIR="${FOREMAN_PROJECT_DIR:-$HOME/runewake}"
HERMES_BIN="${HOME}/.local/bin/hermes"
QUEUE_FILE="${PROJECT_DIR}/TASKS_QUEUE.md"
STATE_FILE="${PROJECT_DIR}/tools/foreman_state.json"
PLAYABLE_FILE="${PROJECT_DIR}/artifacts/PLAYABLE.json"
TELEGRAM_TARGET="telegram:Runewake"

DRY_RUN=false
if [[ "${1:-}" == "--dry-run" ]]; then
  DRY_RUN=true
fi

# Count done and open tasks from TASKS_QUEUE.md
counts=$(python3 -c "
import re
with open('${QUEUE_FILE}') as f:
    content = f.read()
done = len(re.findall(r'^- \[x\] TASK-', content, re.MULTILINE))
open_ = len(re.findall(r'^- \[ \] TASK-', content, re.MULTILINE))
total = done + open_
pct = round(done / total * 100) if total > 0 else 0
print(f'{pct}|{done}|{total}')
" 2>/dev/null || echo "0|0|0")

PCT=$(echo "${counts}" | cut -d'|' -f1)
DONE=$(echo "${counts}" | cut -d'|' -f2)
TOTAL=$(echo "${counts}" | cut -d'|' -f3)

# Find the newest TASK- commit message (plain English)
newest_commit=$(git -C "${PROJECT_DIR}" log --oneline --grep='^TASK-' -1 2>/dev/null || echo "")
newest_desc=""
newest_min_ago=""
if [[ -n "${newest_commit}" ]]; then
  newest_desc=$(echo "${newest_commit}" | sed 's/^[a-f0-9]*\s*TASK-[A-Z0-9-]*\s*:\s*//')
  commit_ts=$(git -C "${PROJECT_DIR}" log --format="%at" -1 2>/dev/null || echo "0")
  now_ts=$(date +%s)
  newest_min_ago=$(( (now_ts - commit_ts) / 60 ))
  if [[ "${newest_min_ago}" -lt 1 ]]; then
    newest_min_ago=1
  fi
fi

# Build the message
msg="Runewake is about ${PCT}% done (${DONE} of ${TOTAL} pieces built)."
if [[ -n "${newest_desc}" ]]; then
  msg="${msg} Last finished: ${newest_desc}, ${newest_min_ago} min ago."
else
  msg="${msg} No finished tasks yet."
fi

# Playable check
if [[ -f "${PLAYABLE_FILE}" ]]; then
  playable=$(python3 -c "
import json
try:
    d = json.load(open('${PLAYABLE_FILE}'))
    print(d.get('playable', False))
except:
    print('false')
" 2>/dev/null || echo "false")
  if [[ "${playable}" == "True" ]] || [[ "${playable}" == "true" ]]; then
    msg="${msg} Playable end to end: yes."
  else
    msg="${msg} Playable end to end: no."
  fi
fi

# Status line — only if something is wrong
if [[ -f "${PROJECT_DIR}/FOREMAN_HALT" ]]; then
  msg="${msg} Stopped, needs restart."
else
  status_reason=$(python3 -c "
import json
try:
    d = json.load(open('${STATE_FILE}'))
    print(d.get('last_heartbeat_reason', ''))
except:
    print('')
" 2>/dev/null || echo "")
  if echo "${status_reason}" | grep -qi "block"; then
    msg="${msg} Stuck on one task."
  else
    # Check if queue has an open task
    has_open=$(python3 -c "
import re
with open('${QUEUE_FILE}') as f:
    content = f.read()
open_tasks = re.findall(r'^- \[ \] TASK-', content)
print(len(open_tasks))
" 2>/dev/null || echo "0")
    if [[ "${has_open}" -eq 0 ]]; then
      msg="${msg} Idle, waiting for tasks."
    fi
  fi
fi

if [[ "${DRY_RUN}" == true ]]; then
  echo "${msg}"
else
  "${HERMES_BIN}" send --to "${TELEGRAM_TARGET}" "${msg}" 2>/dev/null || true
fi
