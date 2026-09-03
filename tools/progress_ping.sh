#!/usr/bin/env bash
PROJECT_DIR="${FOREMAN_PROJECT_DIR:-$HOME/runewake}"
HERMES_BIN="${HOME}/.local/bin/hermes"
cd "$PROJECT_DIR" || exit 0
git pull --ff-only origin main -q 2>/dev/null || true
MSG=$(FOREMAN_PROJECT_DIR="$PROJECT_DIR" python3 - <<'PY'
import subprocess,json,time,re,os
R=os.path.expanduser(os.environ.get("FOREMAN_PROJECT_DIR","~/runewake"))
def sh(*a):
    try: return subprocess.check_output(a,cwd=R,text=True,stderr=subprocess.DEVNULL)
    except Exception: return ""
q=open(os.path.join(R,"TASKS_QUEUE.md")).read().splitlines()
done=sum(1 for l in q if re.match(r'^- \[x\] TASK-',l))
openn=[l for l in q if re.match(r'^- \[ \] TASK-',l)]
parked=sum(1 for l in q if re.match(r'^- \[!\] TASK-',l))
total=done+len(openn)+parked
pct=round(100*done/total) if total else 0
def firstsent(t):
    t=re.sub(r'^(TOP PRIORITY|REOPEN of [A-Z0-9-]+)\.\s*','',t)
    return re.split(r'(?<=[.!?]) ',t)[0].strip()[:90]
def desc_of(line):
    m=re.match(r'^- \[.\] TASK-[A-Z0-9-]+ [—–-] (.*)',line)
    return firstsent(m.group(1) if m else line)
lastdesc=""; mins=""
for row in sh("git","log","-40","--format=%ct\t%s").splitlines():
    try: ct,s=row.split("\t",1)
    except: continue
    if s.startswith("TASK-OPS") or "mark [x]" in s or s.startswith("FABLE") or s.startswith("foreman") or s.startswith("ops:"): continue
    if s.startswith("TASK-"):
        s=re.sub(r'^TASK-[A-Z0-9-]+: ','',s)
        s=re.sub(r'^REOPEN of [A-Z0-9-]+\.\s*','',s)
        lastdesc=firstsent(s); mins=int((time.time()-int(ct))//60); break
GAME=re.compile(r'^(client/|content/|engine/|pipeline/|src/)')
raw=sh("git","log","--since=midnight","--format=__C__%s","--name-only")
gids=set(); dones=[]
cur=None
for line in raw.splitlines():
    if line.startswith("__C__"):
        cur=line[5:]
        if "mark [x]" in cur: dones.append(cur)
    elif line.strip() and cur and GAME.match(line.strip()):
        m=re.match(r'^(TASK-[A-Z0-9-]+):',cur)
        if m: gids.add(m.group(1))
fin=sum(1 for d in dones if re.match(r'^(TASK-[A-Z0-9-]+):',d) and re.match(r'^(TASK-[A-Z0-9-]+):',d).group(1) in gids)
nextup=desc_of(openn[0]) if openn else ""
play=""
try:
    d=json.load(open(os.path.join(R,"artifacts/PLAYABLE.json")))
    play="Playable end to end: "+("yes" if d.get("playable") else "no")+"."
except Exception: pass
st=os.path.expanduser("~/.runewake_ping_last")
prev=None
try: prev=int(open(st).read().strip())
except Exception: pass
open(st,"w").write(str(done))
delta=f" (+{done-prev} since the last update)" if prev is not None and done>prev else (" (no change since the last update)" if prev is not None else "")
lines=[f"Built: {done} pieces{delta}. About {pct}% of the {total} planned."]
if lastdesc: lines.append(f"Last finished: {lastdesc} ({mins} min ago).")
if nextup: lines.append(f"Next up: {nextup}")
tail=f"Finished today: {fin}."
if parked: tail+=f" Parked (waiting on Fable): {parked}."
if play: tail+=f" {play}"
lines.append(tail)
print("\n".join(lines))
PY
)
[[ -z "$MSG" ]] && exit 0
"$HERMES_BIN" -p tcgbot send --to telegram:-5481648844 "$MSG" >/dev/null 2>&1 || "$HERMES_BIN" -p tcgbot send --to telegram:7007731907 "$MSG" >/dev/null 2>&1 || true
echo "$MSG"
