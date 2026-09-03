#!/usr/bin/env bash
# tools/inbox_apply.sh — Apply Fable inbox files
#
# Cron every minute. Safe to run every minute:
# - Exit instantly when the inbox is empty
# - Own lock file so two runs never overlap
# - Pure queue files: insert lines above top unchecked task in TASKS_QUEUE.md
# - Free-form files (no `- [ ] TASK-` lines): run as one-shot hermes prompt
# - After applying a queue insert: if foreman not running + budget remains,
#   start it via nohup
# - Move processed files to inbox/applied/

set -euo pipefail

PROJECT_DIR="${FOREMAN_PROJECT_DIR:-$HOME/runewake}"
HERMES_BIN="${HOME}/.local/bin/hermes"
QUEUE_FILE="${PROJECT_DIR}/TASKS_QUEUE.md"
LOCK_FILE="/tmp/runewake_inbox.lock"
LOG_FILE="${PROJECT_DIR}/tools/inbox.log"
STATE_FILE="${PROJECT_DIR}/tools/foreman_state.json"
DAILY_BUDGET=48

# Lock — atomic mkdir with staleness check
if ! mkdir "${LOCK_FILE}" 2>/dev/null; then
  # Check if the lock is stale (> 5 minutes old)
  if [[ -d "${LOCK_FILE}" ]]; then
    LOCK_AGE=$(($(date +%s) - $(stat -c '%Y' "${LOCK_FILE}")))
    if [[ "${LOCK_AGE}" -gt 300 ]]; then
      rm -rf "${LOCK_FILE}" 2>/dev/null || true
      # Retry once
      mkdir "${LOCK_FILE}" 2>/dev/null || exit 0
    else
      # Lock held — another instance is running
      exit 0
    fi
  else
    exit 0
  fi
fi
trap 'rm -rf "${LOCK_FILE}"' EXIT

info()  { echo "  $*"; }
warn()  { echo "  $*"; }

# Today's budget state
today() { date +%Y-%m-%d; }
TODAY=$(today)

# Read a field from foreman_state.json
get_state() {
  python3 -c "import json,sys; d=json.load(open('${STATE_FILE}')); print(d.get('${1}',''));" 2>/dev/null || echo ""
}

INBOX_DIR="${HOME}/bridge/projects/runewake-export/inbox"

# Exit instantly when inbox is empty
shopt -s nullglob
inbox_files=("${INBOX_DIR}"/*.md)
shopt -u nullglob

if [[ ${#inbox_files[@]} -eq 0 ]]; then
  exit 0
fi

cd "${PROJECT_DIR}"

for inbox_file in $(ls "${INBOX_DIR}"/*.md 2>/dev/null | sort); do
  base_name="$(basename "${inbox_file}")"
  [[ "${base_name}" == "applied" ]] && continue

  # Validate fable marker
  last_line=$(grep -v '^[[:space:]]*$' "${inbox_file}" | tail -1)
  if [[ "${last_line}" != "# from: fable" ]]; then
    info "Inbox ${base_name}: missing '# from: fable' marker — ignored" >> "${LOG_FILE}"
    continue
  fi

  # Check if content is pure queue blocks
  is_pure_queue=true
  while IFS= read -r line; do
    [[ -z "${line}" ]] && continue
    [[ "${line}" =~ ^[[:space:]]*# ]] && continue
    if ! echo "${line}" | grep -qE '^\s*-\s*\[.\]\s*TASK-'; then
      is_pure_queue=false
      break
    fi
  done < "${inbox_file}"

  if [[ "${is_pure_queue}" == true ]]; then
    # Pure queue insert: insert blocks above top unchecked task
    info "Fable inbox: inserting queue items from ${base_name}"
    insert_lines=$(grep -v '^[[:space:]]*$' "${inbox_file}" | grep -v '^[[:space:]]*#' | grep -v '# from: fable')
    # Build indented insert text
    indented=""
    while IFS= read -r iline; do
      if [[ -n "${iline}" ]]; then
        indented="${indented}    ${iline}"$'\n'
      fi
    done <<< "${insert_lines}"
    # Find insert position using Python
    insert_pos=$(python3 -c "
import re
with open('${QUEUE_FILE}') as f:
    content = f.read()
idx = content.find('## Queue')
if idx < 0:
    exit(1)
after = content[idx:]
for i, line in enumerate(after.split('\n')):
    if re.match(r'^\s*-\s*\[\s*\]\s*TASK-', line):
        pos = idx + sum(len(l)+1 for l in after.split('\n')[:i])
        print(pos)
        exit(0)
exit(1)
" 2>/dev/null || echo "")
    if [[ -n "${insert_pos}" ]]; then
      before=$(head -c "${insert_pos}" "${QUEUE_FILE}")
      after=$(tail -c "+$((insert_pos + 1))" "${QUEUE_FILE}")
      printf '%s%s\n%s' "${before}" "${indented}" "${after}" > "${QUEUE_FILE}"
      git add "${QUEUE_FILE}"
      git commit -m "FABLE-INBOX: ${base_name}" 2>/dev/null || true
      git push 2>/dev/null || true
      info "Fable inbox: inserted queue items from ${base_name}" >> "${LOG_FILE}"

      # After queue insert: start foreman if not running and budget remains
      STATE_DATE=$(get_state "date")
      SESSION_COUNT=$(get_state "session_count")
      SESSION_COUNT=${SESSION_COUNT:-0}
      BUS_SESSION_COUNT=$(get_state "bus_session_count")
      BUS_SESSION_COUNT=${BUS_SESSION_COUNT:-0}
      SPENT_HALVES=$(( SESSION_COUNT * 2 + BUS_SESSION_COUNT ))
      BUDGET_HALVES=$(( DAILY_BUDGET * 2 ))

      if [[ "${STATE_DATE}" != "${TODAY}" ]]; then
        # Budget resets daily — today is a fresh day, we have budget
        BUDGET_OK=true
      else
        if [[ "${SPENT_HALVES}" -ge "${BUDGET_HALVES}" ]]; then
          BUDGET_OK=false
        else
          BUDGET_OK=true
        fi
      fi

      FOREMAN_LOCK="/tmp/runewake_foreman.pid"
      FOREMAN_RUNNING=false
      if [[ -f "${FOREMAN_LOCK}" ]]; then
        FPID=$(cat "${FOREMAN_LOCK}" 2>/dev/null || echo "")
        if [[ -n "${FPID}" ]] && kill -0 "${FPID}" 2>/dev/null; then
          FOREMAN_RUNNING=true
        fi
      fi

      if [[ "${BUDGET_OK}" == true ]] && [[ "${FOREMAN_RUNNING}" == false ]]; then
        info "Starting foreman after inbox insert" >> "${LOG_FILE}"
        nohup bash "${PROJECT_DIR}/tools/foreman.sh" >> "${PROJECT_DIR}/tools/foreman_cron.log" 2>&1 &
      fi
    else
      warn "Fable inbox: no unchecked task found to insert above" >> "${LOG_FILE}"
    fi
  else
    # Free-form instruction — run as one-shot prompt
    info "Fable inbox: executing ${base_name} as one-shot prompt" >> "${LOG_FILE}"
    "${HERMES_BIN}" -p tcgbot chat -q "$(cat "${inbox_file}")" -Q 2>&1 | tail -5 >> "${LOG_FILE}" || true
  fi

  # Move file to applied/
  mkdir -p "${INBOX_DIR}/applied"
  mv "${inbox_file}" "${INBOX_DIR}/applied/"
  echo "$(date '+%Y-%m-%d %H:%M:%S') FABLE-INBOX: processed ${base_name} ($([[ "${is_pure_queue}" == true ]] && echo 'queue insert' || echo 'one-shot'))" >> "${LOG_FILE}"
done