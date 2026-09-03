#!/usr/bin/env bash
L=/tmp/runewake_push.lock
for i in $(seq 1 90); do
  if mkdir "$L" 2>/dev/null; then echo $$ > "$L/pid"; break; fi
  H=$(cat "$L/pid" 2>/dev/null)
  if [[ -z "$H" ]] || ! kill -0 "$H" 2>/dev/null; then rm -rf "$L"; continue; fi
  sleep 2
done
trap 'rm -rf "$L"' EXIT
git -c rebase.autoStash=true pull --rebase -q origin main 2>/dev/null || true
git push -q origin main
