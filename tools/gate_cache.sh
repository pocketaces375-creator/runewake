#!/usr/bin/env bash
# tools/gate_cache.sh — publish a green/red verdict for the current code state. Spends no tokens.
set -u
export PATH="$HOME/.local/bin:$HOME/.dotnet:$PATH"
B=/home/fictive/runewake-build; G=/tmp/runewake_gate
mkdir -p "$G"
exec 9>"$G/.lock"; flock -n 9 || { echo "gate already running"; exit 0; }
# the build clone is shared with Fable's own jobs — never reset it under them
exec 8>/tmp/runewake_build.lock; flock -n 8 || { echo "build tree is busy — skipping"; exit 0; }
pgrep -f 'apk_(ship2|alt)_run.sh' >/dev/null && { echo "an apk build is using this tree — skipping"; exit 0; }
cd "$B" || exit 0
git fetch -q origin main 2>/dev/null || exit 0
git reset -q --hard origin/main 2>/dev/null
git clean -qfd -e client/android -e exports -e artifacts 2>/dev/null
CH=$(bash tools/code_hash.sh "$B"); [ -z "$CH" ] && exit 0
[ -f "$G/$CH" ] && exit 0
SHA=$(git rev-parse --short HEAD)
echo "gate: judging code state $CH at $SHA $(date -Is)"
VERDICT=bad; REASON=""
if ! { nice -n 10 dotnet build "$B/Runewake.sln" -c Debug --nologo && nice -n 10 dotnet build "$B/client/Runewake.Client.csproj" -c Debug --nologo; } >/tmp/runewake_gate_build.log 2>&1; then
  REASON="build failed: $(grep -m2 -E 'error [A-Z]+[0-9]+' /tmp/runewake_gate_build.log | head -2 | tr '\n' ' ')"
elif nice -n 10 bash tools/loop_smoke.sh >/tmp/runewake_gate_smoke.log 2>&1 && \
     python3 -c "import json,sys; sys.exit(0 if json.load(open('$B/artifacts/PLAYABLE.json')).get('playable') else 1)" 2>/dev/null; then
  VERDICT=ok
else
  REASON="full-loop playthrough failed at: $(python3 -c "import json;print(json.load(open('$B/artifacts/PLAYABLE.json')).get('failed_step'))" 2>/dev/null || echo unknown)"
fi
printf '%s\n' "$VERDICT" > "$G/$CH"
printf '%s\n' "$REASON" > "$G/$CH.reason"
printf '%s %s %s %s\n' "$CH" "$VERDICT" "$(date +%s)" "$SHA" > "$G/current"
echo "gate: $CH => $VERDICT $REASON"
if [ "$VERDICT" = bad ]; then
  GH_CH="$CH" GH_SHA="$SHA" GH_REASON="$REASON" python3 -c "
import json,time,os
json.dump({'code_hash':os.environ['GH_CH'],'commit':os.environ['GH_SHA'],'reason':os.environ['GH_REASON'],'ts':int(time.time())},open('/tmp/runewake_halt_reason.json','w'))" 2>/dev/null
  "$HOME/.local/bin/hermes" -p tcgbot send --to telegram:-5481648844 "Build check went red on ${SHA} — ${REASON}. Lanes are holding instead of spending against a broken build; the medic picks it up next." >/dev/null 2>&1 || true
fi
ls -1t "$G" | grep -vE '^(current|\.lock)$' | tail -n +40 | while read -r f; do rm -f "$G/$f"; done
