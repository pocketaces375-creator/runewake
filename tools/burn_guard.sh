#!/usr/bin/env bash
# tools/burn_guard.sh — independent spend + sanity watchdog for the Runewake lanes.
# Runs from cron every 10 minutes. Spends NO tokens itself (curl + shell + git only).
#
# Tripwires (any one halts every lane and posts to the group):
#   1. usage today > DAILY_CAP                      (hard budget)
#   2. burn rate  > HOURLY_CAP for 2 samples        (runaway loop)
#   3. the last FAIL_STREAK lane outcomes all failed (gate is probably broken — yesterday's failure mode)
#   4. hourly gate canary: clean build + loop_smoke on origin/main is red (no lane may start against a red gate)
# A canary-caused halt lifts itself when the canary turns green; every other halt needs a human (rm FOREMAN_HALT).
set -u
export PATH="$HOME/.local/bin:$HOME/.dotnet:$PATH"
R=/home/fictive/runewake; B=/home/fictive/runewake-build
LANES=(/home/fictive/runewake /home/fictive/runewake-lane2 /home/fictive/runewake-lane3 /home/fictive/runewake-lane4 /home/fictive/runewake-lane5)
STATE=/tmp/runewake_burn.json
CONF=$R/tools/burn_guard.conf
DAILY_CAP=15; HOURLY_CAP=2.5; FAIL_STREAK=5; CANARY_EVERY=3600; WARN_AT=0.75
[[ -f "$CONF" ]] && . "$CONF"
set -a; . "$HOME/.hermes/.env" 2>/dev/null; set +a
TG="$HOME/.local/bin/hermes -p tcgbot send --to telegram:-5481648844"
now=$(date +%s); today=$(date +%F)

usage=$(curl -s -m 20 https://openrouter.ai/api/v1/auth/key -H "Authorization: Bearer ${OPENROUTER_API_KEY:-}" | python3 -c "import json,sys; print(json.load(sys.stdin)['data'].get('usage_daily',0))" 2>/dev/null || echo "")
[[ -z "$usage" ]] && usage="nan"

# ── outcomes from git: 'foreman: state after TASK (label)' commits, newest first, last 24h ──
cd "$B" && git fetch -q origin main 2>/dev/null
outcomes=$(git log origin/main --since='24 hours ago' --format='%ct %s' 2>/dev/null | grep -E 'foreman: state after TASK-[A-Z0-9-]+ \((success|retry|blocked|transient)\)' | sed -E 's/.*\((success|retry|blocked|transient)\).*/\1/')
sessions_today=$(git log origin/main --since="$today 00:00" --format='%s' 2>/dev/null | grep -cE 'foreman: state after TASK-')
succ_today=$(git log origin/main --since="$today 00:00" --format='%s' 2>/dev/null | grep -cE 'foreman: state after TASK-.*\(success\)')
streak=$(echo "$outcomes" | head -n "$FAIL_STREAK" | grep -cvE '^success$')
n_recent=$(echo "$outcomes" | head -n "$FAIL_STREAK" | grep -c .)

watched=$(python3 - "$STATE" "$now" "$usage" "$today" <<'PY'
import json,sys,os
p,now,usage,today=sys.argv[1],int(sys.argv[2]),sys.argv[3],sys.argv[4]
d=json.load(open(p)) if os.path.exists(p) else {"samples":[],"canary":{},"halted_by":None,"warned":None}
try: u=float(usage)
except: u=None
if u is not None: d["samples"]=[s for s in d["samples"] if s[2]==today][-60:]+[[now,u,today]]
json.dump(d,open(p,"w"))
# spend the guard has actually watched today = usage now minus the first sample of the day
print(f"{(d['samples'][-1][1]-d['samples'][0][1]):.2f}" if d["samples"] else "0")
PY
)

rate=$(python3 - "$STATE" "$now" <<'PY'
import json,sys
d=json.load(open(sys.argv[1])); now=int(sys.argv[2]); s=d["samples"]
if len(s)<2: print("0"); sys.exit()
old=[x for x in s if now-x[0]>=3000]  # ~50+ min ago
if not old: print("0"); sys.exit()
o=old[-1]; dt=(s[-1][0]-o[0])/3600.0
print(f"{(s[-1][1]-o[1])/dt:.2f}" if dt>0 else "0")
PY
)
halted_by=$(python3 -c "import json;print(json.load(open('$STATE')).get('halted_by') or '')")
set_state(){ python3 -c "import json;d=json.load(open('$STATE'));d['$1']=$2;json.dump(d,open('$STATE','w'))"; }

halt_all(){ # $1 = reason tag, $2 = message
  for d in "${LANES[@]}"; do touch "$d/FOREMAN_HALT"; echo "burn_guard: $1 $(date -Is)" > "$d/FOREMAN_HALT_REASON"; done
  for f in /tmp/runewake_foreman.pid /tmp/runewake_foreman_lane{2,3,4,5}.pid; do p=$(cat $f 2>/dev/null); [[ -n "$p" ]] && kill "$p" 2>/dev/null; done
  pkill -f 'hermes -p runewake' 2>/dev/null; pkill -f 'hermes -z Implement the top unchecked task' 2>/dev/null
  set_state halted_by "\"burn_guard:$1\""
  $TG "⛔ BURN GUARD halted all lanes — $2. Spend today \$${usage}. Fable/Trikzos: fix, then rm FOREMAN_HALT in each lane to resume." >/dev/null 2>&1 || true
  logger -t burn-guard "HALT $1: $2"
}
unhalt_all(){ for d in "${LANES[@]}"; do rm -f "$d/FOREMAN_HALT" "$d/FOREMAN_HALT_REASON"; done; set_state halted_by null; $TG "✅ Burn guard: $1 — lanes resumed." >/dev/null 2>&1 || true; }

# ── 1 + 2: budget tripwires ──
if [[ "$usage" != "nan" ]]; then
  over=$(python3 -c "print(1 if $watched > $DAILY_CAP else 0)")
  if [[ "$over" == "1" && -z "$halted_by" ]]; then halt_all budget "lanes spent \$${watched} under watch today, over the \$${DAILY_CAP} cap"; exit 0; fi
  warn=$(python3 -c "print(1 if $watched > $DAILY_CAP*$WARN_AT else 0)")
  warned=$(python3 -c "import json;print(json.load(open('$STATE')).get('warned') or '')")
  if [[ "$warn" == "1" && "$warned" != "$today" ]]; then set_state warned "\"$today\""; $TG "⚠️ Burn guard: \$${watched} spent under watch today (key total \$${usage}), ${WARN_AT} of the \$${DAILY_CAP} cap. Rate \$${rate}/h, ${sessions_today} sessions, ${succ_today} passed." >/dev/null 2>&1 || true; fi
  hot=$(python3 -c "print(1 if $rate > $HOURLY_CAP else 0)")
  if [[ "$hot" == "1" ]]; then
    hot_prev=$(python3 -c "import json;print(json.load(open('$STATE')).get('hot_prev') or 0)")
    if [[ "$hot_prev" == "1" && -z "$halted_by" ]]; then halt_all rate "burn rate \$${rate}/h above \$${HOURLY_CAP}/h for 20 min"; exit 0; fi
    set_state hot_prev 1
  else set_state hot_prev 0; fi
fi

# ── 3: failure streak (the gate is probably broken) ──
if [[ "$n_recent" -ge "$FAIL_STREAK" && "$streak" -ge "$FAIL_STREAK" && -z "$halted_by" ]]; then
  halt_all streak "last ${FAIL_STREAK} lane sessions all failed — the gate itself is probably broken"; exit 0
fi

# ── 4: hourly gate canary on a clean origin/main ──
last_canary=$(python3 -c "import json;print(json.load(open('$STATE')).get('canary',{}).get('ts',0))")
if (( now - last_canary >= CANARY_EVERY )); then
  cd "$B" && git reset -q --hard origin/main && git clean -qfd -e client/android -e exports 2>/dev/null
  if dotnet build client/Runewake.Client.csproj -c Debug --nologo -v q >/tmp/canary_build.log 2>&1 && timeout 600 bash tools/loop_smoke.sh >/tmp/canary_smoke.log 2>&1 && grep -q '"playable": true' artifacts/PLAYABLE.json 2>/dev/null; then
    python3 -c "import json;d=json.load(open('$STATE'));d['canary']={'ts':$now,'ok':True,'commit':'$(git rev-parse --short HEAD)'};json.dump(d,open('$STATE','w'))"
    if [[ "$halted_by" == "burn_guard:canary" ]]; then unhalt_all "gate is green again on $(git rev-parse --short HEAD)"; fi
  else
    reason=$(grep -oE 'failed_step[^,}]*|error CS[0-9]+[^\[]{0,80}|ContentValidation\][^\n]{0,100}' /tmp/canary_build.log /tmp/canary_smoke.log artifacts/PLAYABLE.json 2>/dev/null | head -2 | tr '\n' ' ')
    python3 -c "import json;d=json.load(open('$STATE'));d['canary']={'ts':$now,'ok':False,'commit':'$(git rev-parse --short HEAD)','reason':'''${reason//\'/}'''};json.dump(d,open('$STATE','w'))"
    [[ -z "$halted_by" ]] && halt_all canary "gate is RED on a clean origin/main ($(git rev-parse --short HEAD)): ${reason:-see /tmp/canary_smoke.log}"
  fi
fi

# summary line for the hourly progress ping
pass_pct=$([[ "$sessions_today" -gt 0 ]] && python3 -c "print(round(100*$succ_today/$sessions_today))" || echo "-")
echo "Lane spend today \$${watched} of \$${DAILY_CAP} cap (key total \$${usage}) · \$${rate}/h · ${sessions_today} sessions, ${pass_pct}% passed$( [[ -n "$halted_by" ]] && echo " · HALTED (${halted_by#burn_guard:})" )" > /tmp/runewake_burn_line.txt
