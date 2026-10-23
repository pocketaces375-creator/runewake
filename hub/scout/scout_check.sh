#!/usr/bin/env bash
# scout_check.sh — the Scout's "eyes". Runs hourly from Hermes cron, costs nothing.
# Looks at what changed since last hour. If nothing changed, prints {"wakeAgent": false}
# and the AI never wakes up (zero tokens). If something changed, prints a short report
# that the Scout agent turns into 1-3 suggestions for Trikzos.
# Install: cp to ~/.hermes/scripts/scout_check.sh && chmod +x
set -uo pipefail
STATE="$HOME/.hub/scout"; mkdir -p "$STATE"
SINCE_FILE="$STATE/last_run"; [[ -f $SINCE_FILE ]] || date -d '-1 hour' +%s > "$SINCE_FILE"
SINCE=$(cat "$SINCE_FILE"); SINCE_ISO=$(date -d "@$SINCE" -Is)
REPORT=()

# 1. New commits in every watched repo (add paths to ~/.hub/scout/repos.txt, one per line)
[[ -f $STATE/repos.txt ]] || printf '%s\n' "$HOME/runewake-build" "$HOME/vendops" > "$STATE/repos.txt"
while read -r repo; do
  [[ -d $repo/.git ]] || continue
  git -C "$repo" fetch -q origin 2>/dev/null
  log=$(git -C "$repo" log origin/HEAD --since="$SINCE_ISO" --pretty='  - %h %s' 2>/dev/null \
        | grep -v -E 'heartbeat|foreman: state|digest ' | head -15)
  [[ -n $log ]] && REPORT+=("NEW COMMITS in $(basename "$repo"):" "$log")
done < "$STATE/repos.txt"

# 2. New builds (APKs, exports) finished
apks=$(find "$HOME" -maxdepth 4 -name '*.apk' -newermt "@$SINCE" 2>/dev/null | head -5)
[[ -n $apks ]] && REPORT+=("NEW BUILD FILES:" "$(sed 's/^/  - /' <<<"$apks")")

# 3. Runewake lanes: halts, done/parked tasks, spend
for h in "$HOME"/runewake*/FOREMAN_HALT; do
  [[ -f $h ]] && REPORT+=("LANE HALTED: $(dirname "$h") — $(cat "$(dirname "$h")/FOREMAN_HALT_REASON" 2>/dev/null | head -c 200)")
done
for l in "$HOME"/runewake*/tools/foreman_lane*.log; do
  [[ -f $l ]] || continue
  [[ $(stat -c %Y "$l") -gt $SINCE ]] || continue
  hits=$(grep -E 'DONE|PARK|FAILED|BLOCKED' "$l" | tail -3)
  [[ -n $hits ]] && REPORT+=("LANE $(basename "$l"):" "$(sed 's/^/  - /' <<<"$hits")")
done
[[ -f /tmp/runewake_burn_line.txt ]] && SPEND=$(cat /tmp/runewake_burn_line.txt)

# 4. What every bot says it's doing (hub board)
if [[ -f $HOME/hub-digest/NOW.md && $(stat -c %Y "$HOME/hub-digest/NOW.md") -gt $SINCE ]]; then
  REPORT+=("BOT STATUS BOARD:" "$(grep '^- ' "$HOME/hub-digest/NOW.md" | head -14)")
fi

# 5. Anything dropped into the GPU box output folder (music/images/LoRA), if mounted/synced
for d in "$HOME/gpu-outbox" "$HOME/ace-step/outputs" "$HOME/lora/output"; do
  [[ -d $d ]] || continue
  n=$(find "$d" -type f -newermt "@$SINCE" 2>/dev/null | wc -l)
  [[ $n -gt 0 ]] && REPORT+=("GPU OUTPUT: $n new files in $d")
done

date +%s > "$SINCE_FILE"
if [[ ${#REPORT[@]} -eq 0 ]]; then
  echo '{"wakeAgent": false}'
  exit 0
fi
echo "Changes since $(date -d "@$SINCE" '+%H:%M'):"
printf '%s\n' "${REPORT[@]}"
[[ -n ${SPEND:-} ]] && echo "SPEND: $SPEND"
echo
echo "Trikzos' habits so far (what he said yes/no to):"
tail -30 "$STATE/habits.md" 2>/dev/null || echo "  (none yet)"
