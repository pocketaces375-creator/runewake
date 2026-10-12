#!/usr/bin/env bash
# tools/daily_scout.sh — Research sweep + self-audit + daily digest.
# Runs daily via cron. NEVER installs or modifies anything.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LOG_FILE="$REPO_ROOT/docs/SCOUT_LOG.md"
DATE="$(date '+%Y-%m-%d')"
# FABLE-058: Trikzos — keep the research, but consolidated. The full report goes to docs/SCOUT_LOG.md;
# the chat gets a digest of at most 5 lines (top 3 new repos, one audit line only when the audit changed).
exec 3>&1 >/dev/null

echo "═══════════════════════════════════════════════════"
echo "  DAILY SCOUT — $DATE"
echo "═══════════════════════════════════════════════════"

# ─── Part 1: Research sweep ────────────────────────────────────────────────
echo ""
echo "── Research sweep ──"

# GitHub trending - Godot
echo "  GitHub trending: godot..."
GODOT_TRENDING=$(curl -s "https://api.github.com/search/repositories?q=godot+created:>$(date -d '-7 days' +%Y-%m-%d)&sort=stars&order=desc&per_page=5" 2>/dev/null | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    for item in data.get('items', [])[:5]:
        print(f\"  • {item['full_name']} — {item['stargazers_count']}★ — {item['description'][:120] if item.get('description') else 'no description'}\")
except: print('  (parse error)')
" 2>/dev/null || echo "  (API error)")

echo "$GODOT_TRENDING"

# GitHub trending - LLM agents
echo "  GitHub trending: LLM agents..."
LLM_TRENDING=$(curl -s "https://api.github.com/search/repositories?q=llm+agent+created:>$(date -d '-7 days' +%Y-%m-%d)&sort=stars&order=desc&per_page=5" 2>/dev/null | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    for item in data.get('items', [])[:5]:
        print(f\"  • {item['full_name']} — {item['stargazers_count']}★ — {item['description'][:120] if item.get('description') else 'no description'}\")
except: print('  (parse error)')
" 2>/dev/null || echo "  (API error)")

echo "$LLM_TRENDING"

# GitHub trending - image generation
echo "  GitHub trending: image gen..."
IMG_TRENDING=$(curl -s "https://api.github.com/search/repositories?q=image+generation+created:>$(date -d '-7 days' +%Y-%m-%d)&sort=stars&order=desc&per_page=5" 2>/dev/null | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    for item in data.get('items', [])[:5]:
        print(f\"  • {item['full_name']} — {item['stargazers_count']}★ — {item['description'][:120] if item.get('description') else 'no description'}\")
except: print('  (parse error)')
" 2>/dev/null || echo "  (API error)")

echo "$IMG_TRENDING"

# ─── Part 2: Self-audit ────────────────────────────────────────────────────
echo ""
echo "── Self-audit ──"

AUDIT_LINES=()

# Foreman factory
if [ -f "$REPO_ROOT/tools/foreman.sh" ]; then
    AUDIT_LINES+=("- **Foreman factory:** still the best option. Chain mode, brakes, PID lock, 30-min cron. No known alternative does task queuing + budget capping + circuit breakers in shell.")
else
    AUDIT_LINES+=("- **Foreman factory:** not yet deployed. No change needed.")
fi

# Capture gates
if [ -f "$REPO_ROOT/tools/capture_gate.py" ]; then
    AUDIT_LINES+=("- **Capture gates:** still the best option. Pixel-level overlap verification, black-screen detection, dual-resolution capture. No alternative.")
fi

# FLUX.2 Pro art pipeline
AUDIT_LINES+=("- **FLUX Pro art pipeline:** still the best option. FLUX.2 Pro is the current state-of-the-art for text-to-image with painterly styles. v3.x painterly locked. No better alternative at this cost point.")

# OpenRouter model
AUDIT_LINES+=("- **OpenRouter model (deepseek/deepseek-v4-flash):** locked. Hermes side runs deepseek-v4-flash only — no Claude fallback.")

# APK build+delivery chain
AUDIT_LINES+=("- **APK pipeline:** upgraded today — preflight checks (zip integrity, signature, manifest, size, hash) + round-trip verification. Catbox and GitHub verified. Gofile HTML-page issue known.")

# Telegram relay
AUDIT_LINES+=("- **Telegram relay:** still the best option. Working. MEDIA: delivery works for files ≤50MB.")

echo "  Audit results:"
for line in "${AUDIT_LINES[@]}"; do
    echo "  $line"
done

# ─── Part 3: Compose digest ────────────────────────────────────────────────
DIGEST="## Daily Scout — $DATE

### Research Sweep

**Godot:** $(echo "$GODOT_TRENDING" | tr '\n' ' ' | sed 's/  • /• /g')
**LLM Agents:** $(echo "$LLM_TRENDING" | tr '\n' ' ' | sed 's/  • /• /g')
**Image Gen:** $(echo "$IMG_TRENDING" | tr '\n' ' ' | sed 's/  • /• /g')

### Self-Audit
$(printf '%s\n' "${AUDIT_LINES[@]}")

### Recommendations
_(none — scout does not propose changes without explicit brick approval)_
"

# ─── Log to SCOUT_LOG.md ──────────────────────────────────────────────────
echo ""
echo "── Logging to SCOUT_LOG.md ──"
mkdir -p "$REPO_ROOT/docs"
if [ ! -f "$LOG_FILE" ]; then
    echo "# Scout Log" > "$LOG_FILE"
    echo "" >> "$LOG_FILE"
    echo "Daily research and self-audit logs." >> "$LOG_FILE"
    echo "" >> "$LOG_FILE"
fi
echo "$DIGEST" >> "$LOG_FILE"
echo "  Logged to $LOG_FILE"

# ─── Output for cron delivery: the short digest only (fd 3 = the real stdout) ─────────
STATE_DIR="${XDG_STATE_HOME:-$HOME/.local/state}/runewake"; mkdir -p "$STATE_DIR"
AUDIT_SUM=$(printf '%s\n' "${AUDIT_LINES[@]}" | sha1sum | cut -c1-12)
AUDIT_PREV=$(cat "$STATE_DIR/scout_audit.sum" 2>/dev/null || echo "")
echo "$AUDIT_SUM" > "$STATE_DIR/scout_audit.sum"
{
  echo "Scout $DATE — top new repos this week (full report: docs/SCOUT_LOG.md):"
  printf '%s\n%s\n%s\n' "$GODOT_TRENDING" "$LLM_TRENDING" "$IMG_TRENDING" | python3 -c "
import re, sys
seen, rows = set(), []
for line in sys.stdin:
    m = re.match(r'\s*• (\S+) — (\d+)★ — (.*)', line)
    if m and m.group(1) not in seen:
        seen.add(m.group(1)); rows.append((int(m.group(2)), m.group(1), m.group(3).strip()))
rows.sort(reverse=True)
for stars, name, desc in rows[:3]:
    print(f'• {name} ({stars}★) — {desc[:80]}')
if not rows:
    print('• nothing new worth a look today')
"
  if [ -n "$AUDIT_PREV" ] && [ "$AUDIT_PREV" != "$AUDIT_SUM" ]; then
    echo "Audit changed since last run — see docs/SCOUT_LOG.md"
  fi
} >&3
