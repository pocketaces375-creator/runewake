#!/usr/bin/env bash
# Kill sessions that are spending without working: past SOFT_KILL seconds with no file touched in IDLE.
set -u
SOFT_KILL=${SOFT_KILL:-900}; IDLE=${IDLE:-300}; killed=0
for pid in $(pgrep -f 'hermes -p runewake' 2>/dev/null); do
  cmd=$(tr '\0' ' ' < /proc/$pid/cmdline 2>/dev/null) || continue
  et=$(ps -o etimes= -p "$pid" 2>/dev/null | tr -d ' '); [ -z "$et" ] && continue
  [ "$et" -lt "$SOFT_KILL" ] && continue
  dir=$(printf '%s' "$cmd" | grep -oE '/home/fictive/runewake(-lane[0-9])?' | head -1)
  [ -z "$dir" ] && dir=/home/fictive/runewake
  newest=$(find "$dir" -path "$dir/.git" -prune -o -newermt "-${IDLE} seconds" -type f -print -quit 2>/dev/null)
  if [ -z "$newest" ]; then
    echo "$(date -Is) killing idle session pid=$pid age=${et}s dir=$dir" >> /tmp/runewake_watchdog.log
    kill "$pid" 2>/dev/null; sleep 3; kill -9 "$pid" 2>/dev/null; killed=$((killed+1))
  fi
done
exit 0
