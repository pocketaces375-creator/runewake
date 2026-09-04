#!/usr/bin/env bash
# tools/loop_smoke.sh — TASK-LOOP-GATE-1: Full game-loop smoke test.
# Plays the full UI loop (title → Choose Your Path → map → duel → victory →
# drops → Reliquary → Deck Forge → map) using the Godot-side LoopSmokeTest
# autoload with injected _GuiInput events on the title and ChoosePath screens,
# and the soak auto-play infrastructure for map/duel/victory navigation.
#
# Writes artifacts/PLAYABLE.json: {"playable": true|false, "commit": "<sha>",
#                                   "checked_at": "<iso>", "failed_step": "<name or null>"}
# Must finish in under 10 minutes.
# Usage: bash tools/loop_smoke.sh
# Returns 0 on PASS, 1 on FAIL.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"
PLAYABLE_FILE="$ROOT/artifacts/PLAYABLE.json"
RESULT_FILE="$CAPTURE_DIR/loop_smoke_result.json"

mkdir -p "$CAPTURE_DIR" "$ROOT/artifacts"
rm -f "$PLAYABLE_FILE" "$RESULT_FILE"

# ── CLEAN INSTALL ────────────────────────────────────────────────────────────
# The loop test asks "can a NEW PLAYER get from the title screen to the end of the loop?", so it must
# start with no save. Without this, one earlier run (a soak, an APK check, another lane — they all share
# ~/.local/share/godot/app_userdata/<app>) leaves a save behind, the title offers Continue instead of
# New Campaign, the test lands on the Map and reports the game unplayable when it is fine.
APP_NAME=$(grep -m1 '^config/name=' "$PROJECT_GODOT" | cut -d'"' -f2)
if [ -n "${APP_NAME}" ]; then
  USER_DIR="$HOME/.local/share/godot/app_userdata/${APP_NAME}"
  if [ -d "${USER_DIR}" ]; then
    rm -rf "${USER_DIR}"
    echo "  Cleared save data at ${USER_DIR} (the loop test always runs as a new player)"
  fi
fi

# Ensure headless rendering backend
export GODOT_RENDERER=Headless

echo "=== Loop Smoke Test ==="
echo "Root: $ROOT"
echo "Godot: $GODOT_BIN"

# Set viewport to 2316x1080
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Clear any stale PLAYABLE.json from client/ dir
rm -f "$ROOT/client/artifacts/PLAYABLE.json"

echo "=== Running LoopSmokeTest (timeout: 600s) ==="
START_TIME=$(date +%s)

# Run the client headless with loop smoke test capture mode
timeout 600 xvfb-run -a "$GODOT_BIN" --path "$ROOT/client" -- "--capture=loop_smoke_test" 2>&1 || true

END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))
echo "=== Loop smoke test completed in ${ELAPSED}s ==="

# Restore project.godot
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Check for PLAYABLE.json — may be in artifacts/ or client/artifacts/
if [ ! -f "$PLAYABLE_FILE" ]; then
    CLIENT_PLAYABLE="$ROOT/client/artifacts/PLAYABLE.json"
    if [ -f "$CLIENT_PLAYABLE" ]; then
        cp "$CLIENT_PLAYABLE" "$PLAYABLE_FILE"
        echo "  Copied PLAYABLE.json from client/artifacts/"
    fi
fi

# If neither exists, also check for the legacy loop_smoke_result.json
if [ ! -f "$PLAYABLE_FILE" ] && [ -f "$RESULT_FILE" ]; then
    VERDICT=$(python3 -c "
import json
with open('$RESULT_FILE') as f:
    data = json.load(f)
print(data.get('verdict', 'UNKNOWN'))
" 2>/dev/null || echo "UNKNOWN")

    # Create PLAYABLE.json from result
    COMMIT=$(cd "$ROOT" && git rev-parse HEAD 2>/dev/null || echo "unknown")
    python3 -c "
import json, datetime
with open('$RESULT_FILE') as f:
    data = json.load(f)
verdict = data.get('verdict', 'UNKNOWN')
playable = str(verdict == 'PASS').lower()
with open('$PLAYABLE_FILE', 'w') as f:
    json.dump({
        'playable': verdict == 'PASS',
        'commit': '$COMMIT',
        'checked_at': datetime.datetime.utcnow().isoformat(),
        'failed_step': data.get('failed_step', None)
    }, f, indent=2)
"
fi

# Final check
if [ ! -f "$PLAYABLE_FILE" ]; then
    echo "FAIL: PLAYABLE.json not produced" >&2
    # Write a failure PLAYABLE.json
    COMMIT=$(cd "$ROOT" && git rev-parse HEAD 2>/dev/null || echo "unknown")
    python3 -c "
import json, datetime
with open('$PLAYABLE_FILE', 'w') as f:
    json.dump({
        'playable': False,
        'commit': '$COMMIT',
        'checked_at': datetime.datetime.utcnow().isoformat(),
        'failed_step': 'no_output'
    }, f, indent=2)
"
fi

# Read and display results
if [ -f "$PLAYABLE_FILE" ]; then
    echo ""
    echo "=== PLAYABLE.json ==="
    cat "$PLAYABLE_FILE"
    echo ""
    PLAYABLE=$(python3 -c "
import json
with open('$PLAYABLE_FILE') as f:
    data = json.load(f)
print(str(data.get('playable', False)).lower())
")

    if [ "$PLAYABLE" = "true" ]; then
        echo "=== Loop Smoke Test PASSED ==="
        exit 0
    else
        echo "=== Loop Smoke Test FAILED ===" >&2
        exit 1
    fi
fi

echo "FAIL: Could not determine result" >&2
exit 1