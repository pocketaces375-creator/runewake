#!/usr/bin/env bash
# tools/inbox_apply.sh — Apply Fable inbox files
#
# Cron every minute. Safe to run every minute:
# - Exit instantly when the inbox is empty
# - Own lock file so two runs never overlap
# - Pure queue files: insert lines above top unchecked task in TASKS_QUEUE.md
# - Free-form files (no `- [ ] TASK-` lines): moved to needs_reformat/ for Fable to fix
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

# ── Log rotation for inbox.log ───────────────────────────────────────────────
if [[ -f "${LOG_FILE}" ]]; then
  LAST_DAY=$(date -r "${LOG_FILE}" '+%Y-%m-%d' 2>/dev/null || echo "")
  TODAY=$(date '+%Y-%m-%d')
  if [[ -n "${LAST_DAY}" && "${LAST_DAY}" != "${TODAY}" ]]; then
    [[ -f "${LOG_FILE}.2" ]] && mv -f "${LOG_FILE}.2" "${LOG_FILE}.3" 2>/dev/null || true
    [[ -f "${LOG_FILE}.1" ]] && mv -f "${LOG_FILE}.1" "${LOG_FILE}.2" 2>/dev/null || true
    mv -f "${LOG_FILE}" "${LOG_FILE}.1" 2>/dev/null || true
  fi
fi

# Lock — atomic mkdir with staleness check
# Lock — atomic mkdir, broken ONLY when the holding process is actually gone.
# A one-shot hermes session legitimately holds this for 20-45 minutes. The old
# flat 300s staleness check broke a LIVE lock every 5 minutes and launched a
# duplicate session on the same inbox file (4 concurrent copies observed
# 2026-09-03). Never break a lock whose holder is alive.
if ! mkdir "${LOCK_FILE}" 2>/dev/null; then
  HOLDER=$(cat "${LOCK_FILE}/pid" 2>/dev/null || echo "")
  if [[ -n "${HOLDER}" ]] && kill -0 "${HOLDER}" 2>/dev/null; then
    exit 0
  fi
  LOCK_AGE=$(($(date +%s) - $(stat -c '%Y' "${LOCK_FILE}" 2>/dev/null || echo 0)))
  if [[ -z "${HOLDER}" ]] && [[ "${LOCK_AGE}" -lt 3600 ]]; then
    exit 0
  fi
  rm -rf "${LOCK_FILE}" 2>/dev/null || true
  mkdir "${LOCK_FILE}" 2>/dev/null || exit 0
fi
echo $$ > "${LOCK_FILE}/pid" 2>/dev/null || true
trap 'rm -rf "${LOCK_FILE}"' EXIT

info()  { echo "  $*"; }
warn()  { echo "  $*"; }

today() { date +%Y-%m-%d; }
TODAY=$(today)

get_state() {
  python3 -c "import json,sys; d=json.load(open('${STATE_FILE}')); print(d.get('${1}',''));" 2>/dev/null || echo ""
}

INBOX_DIR="${HOME}/bridge/projects/runewake-export/inbox"

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
  # Pure-queue: every non-blank, non-comment line is EITHER a task header
  # (- [ ] TASK-<ID> — <desc>) OR a continuation line (starts with whitespace).
  is_pure_queue=true
  while IFS= read -r line; do
    [[ -z "${line}" ]] && continue
    [[ "${line}" =~ ^[[:space:]]*# ]] && continue
    if echo "${line}" | grep -qE '^-\s+\[\s*\]\s+TASK-[A-Z0-9-]+\s+[—–-]'; then
      : # task header — ok
    elif echo "${line}" | grep -qE '^[[:space:]]'; then
      : # starts with whitespace — ok (continuation line)
    else
      is_pure_queue=false
      break
    fi
  done < "${inbox_file}"

  if [[ "${is_pure_queue}" == true ]]; then
    # Pure queue insert: insert lines above top unchecked task
    info "Fable inbox: inserting queue items from ${base_name}"
    insert_lines=$(grep -v '^[[:space:]]*$' "${inbox_file}" | grep -v '^[[:space:]]*#' | grep -v '# from: fable')
    # Build insert text verbatim (no indentation prefix — queue parser expects
    # task headers at column 0, continuation lines indented as-is)
    insert_text=""
    while IFS= read -r iline; do
      insert_text="${insert_text}${iline}"$'\n'
    done <<< "${insert_lines}"

    # Perform the insert via Python (character-accurate, handles UTF-8 multi-byte)
    insert_result=$(echo "${insert_text}" | python3 -c "
import re, sys
queue_file = '${QUEUE_FILE}'
insert_data = sys.stdin.read()
with open(queue_file) as f:
    content = f.read()
idx = content.find('## Queue')
if idx < 0:
    print('NO_INSERT')
    sys.exit(0)
after = content[idx:]
for i, line in enumerate(after.split('\n')):
    if re.match(r'^\s*-\s*\[\s*\]\s*TASK-', line):
        pos = idx + sum(len(l)+1 for l in after.split('\n')[:i])
        # Insert verbatim with a blank line before and after
        result = content[:pos] + '\n' + insert_data.rstrip('\n') + '\n' + content[pos:]
        with open(queue_file, 'w') as f:
            f.write(result)
        print('INSERT_OK')
        sys.exit(0)
lines = content.split('\\n')
qi = next(i for i, l in enumerate(lines) if l.startswith('## Queue'))
j = qi + 1
while j < len(lines) and (lines[j].lstrip().startswith('#') or not lines[j].strip()):
    j += 1
result = '\\n'.join(lines[:j]) + '\\n' + insert_data.rstrip('\\n') + '\\n\\n' + '\\n'.join(lines[j:])
with open(queue_file, 'w') as f:
    f.write(result)
print('INSERT_OK')
")
    if [[ "${insert_result}" == "INSERT_OK" ]]; then
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
    # Free-form file — not pure queue format. Move to needs_reformat/.
    info "Fable inbox: ${base_name} is not pure queue format — moved to needs_reformat/" >> "${LOG_FILE}"
    mkdir -p "${INBOX_DIR}/needs_reformat"
    mv "${inbox_file}" "${INBOX_DIR}/needs_reformat/"
    echo "$(date '+%Y-%m-%d %H:%M:%S') FABLE-INBOX: ${base_name} — not pure queue format, moved to needs_reformat/" >> "${LOG_FILE}"
    TELEGRAM_TARGET="${FOREMAN_TELEGRAM_TARGET:-telegram:Runewake}"
    "${HERMES_BIN}" send --to "${TELEGRAM_TARGET}" "Inbox file ${base_name} is not pure queue format — moved to needs_reformat/, nothing was run. Fable: resend as - [ ] TASK blocks with indented continuation lines." 2>/dev/null || true
  fi

  # Move file to applied/ (only reached when actually processed)
  mkdir -p "${INBOX_DIR}/applied"
  if [[ -f "${inbox_file}" ]]; then mv "${inbox_file}" "${INBOX_DIR}/applied/"; fi
  echo "$(date '+%Y-%m-%d %H:%M:%S') FABLE-INBOX: processed ${base_name} ($([[ "${is_pure_queue}" == true ]] && echo 'queue insert' || echo 'needs-reformat'))" >> "${LOG_FILE}"
done