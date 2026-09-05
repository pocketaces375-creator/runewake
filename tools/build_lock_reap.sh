#!/usr/bin/env bash
# Release the shared build lock when only leftover build daemons are holding it.
# ONLY build tools are ever reaped: MSBuild's node-reuse daemons and stray godot/dotnet processes that
# outlived their parent. Detached worker scripts are orphans by design and must never be touched.
set -u
MAX_AGE=${MAX_AGE:-600}
LOCK=/tmp/runewake_build.lock
holders=$(fuser "$LOCK" 2>/dev/null | tr -s ' ' '\n' | grep -E '^[0-9]+$' || true)
[ -z "$holders" ] && exit 0
killed=0
for pid in $holders; do
  args=$(ps -o args= -p "$pid" 2>/dev/null) || continue
  case "$args" in
    *MSBuild.dll*|*VBCSCompiler*|*"godot --headless"*) ;;   # build daemons only
    *) continue ;;
  esac
  ppid=$(ps -o ppid= -p "$pid" 2>/dev/null | tr -d ' ')
  age=$(ps -o etimes= -p "$pid" 2>/dev/null | tr -d ' ')
  [ -z "$age" ] && continue
  if [ "${ppid:-1}" = "1" ] && [ "$age" -gt "$MAX_AGE" ]; then
    echo "$(date -Is) reaping build daemon pid=$pid age=${age}s $(echo "$args" | cut -c1-70)" >> /tmp/runewake_reap.log
    kill "$pid" 2>/dev/null; sleep 2; kill -9 "$pid" 2>/dev/null; killed=$((killed+1))
  fi
done
[ "$killed" -gt 0 ] && echo "reaped $killed build daemon(s)"
exit 0
