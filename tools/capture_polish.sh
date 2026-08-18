#!/usr/bin/env bash
set -euo pipefail
# POLISH-PASS-1 full capture suite: title + map + duel dual-res + overlay + gate.
# Usage: bash tools/capture_polish.sh

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

# Clean previous captures
mkdir -p "$CAPTURE_DIR"
rm -f "$CAPTURE_DIR"/title_test*.png "$CAPTURE_DIR"/title_test*.meta.json
rm -f "$CAPTURE_DIR"/map_test*.png "$CAPTURE_DIR"/map_test*.meta.json
rm -f "$CAPTURE_DIR"/duel_test*.png "$CAPTURE_DIR"/duel_test*.meta.json
rm -f "$CAPTURE_DIR"/victory_overlay*.png "$CAPTURE_DIR"/defeat_overlay*.png

cap_one() {
    local capture_arg="$1"
    local width="$2"
    local height="$3"
    echo "=== Capture: ${capture_arg} (${width}x${height}) ==="

    # Patch viewport (key=value format under [display])
    sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width='"${width}"'|' "$PROJECT_GODOT"
    sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height='"${height}"'|' "$PROJECT_GODOT"

    # Run
    xvfb-run -a "$GODOT_BIN" --path client -- "--capture=${capture_arg}" 2>&1
    local rc=$?

    # Build the expected output filename — use the capture_arg directly
    local out="${capture_arg}"

    if [ -f "$CAPTURE_DIR/${out}.png" ]; then
        local pw ph
        read pw ph <<< "$(python3 -c "
import struct
with open('${CAPTURE_DIR}/${out}.png','rb') as f:
    f.read(8)
    while True:
        cl = struct.unpack('>I', f.read(4))[0]
        ct = f.read(4)
        cd = f.read(cl)
        f.read(4)
        if ct == b'IHDR':
            w = struct.unpack('>I', cd[0:4])[0]
            h = struct.unpack('>I', cd[4:8])[0]
            print(w, h)
            break
")"
        echo "  → ${out}.png: ${pw}x${ph}"
        if [ "$pw" -ne "$width" ] || [ "$ph" -ne "$height" ]; then
            echo "  ⚠ WARNING: Expected ${width}x${height}, got ${pw}x${ph}" >&2
        fi
    else
        echo "  ✗ FAIL: ${out}.png not produced" >&2
        rc=1
    fi
    return $rc
}

RC=0

# Standard (1152x648)
cap_one "title_test" 1152 648 || RC=1
cap_one "map_test" 1152 648 || RC=1
cap_one "duel_test" 1152 648 || RC=1
cap_one "victory_overlay" 1152 648 || RC=1
cap_one "defeat_overlay" 1152 648 || RC=1

# Wide (1999x932)
cap_one "title_test_wide" 1999 932 || RC=1
cap_one "map_test_wide" 1999 932 || RC=1
cap_one "duel_test_wide" 1999 932 || RC=1
cap_one "victory_overlay_wide" 1999 932 || RC=1
cap_one "defeat_overlay_wide" 1999 932 || RC=1

# Restore to standard
sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width=1152|' "$PROJECT_GODOT"
sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height=648|' "$PROJECT_GODOT"

echo ""
echo "=== POLISH-PASS-1-E capture results ==="
ls -la "$CAPTURE_DIR"/title_test.png "$CAPTURE_DIR"/map_test.png \
      "$CAPTURE_DIR"/duel_test.png "$CAPTURE_DIR"/duel_test_wide.png \
      "$CAPTURE_DIR"/victory_overlay.png "$CAPTURE_DIR"/victory_overlay_wide.png \
      "$CAPTURE_DIR"/defeat_overlay.png "$CAPTURE_DIR"/defeat_overlay_wide.png 2>/dev/null

echo ""
echo "Gate: python3 ${ROOT}/tools/capture_gate.py title_test title_test_wide map_test map_test_wide"
python3 "$ROOT/tools/capture_gate.py" title_test title_test_wide map_test map_test_wide 2>&1
GATE_RC=$?
echo "Title/Map gate exit: $GATE_RC"
python3 "$ROOT/tools/capture_gate.py" 2>&1
D_GATE_RC=$?
echo "Duel gate exit: $D_GATE_RC"

if [ $RC -eq 0 ] && [ $GATE_RC -eq 0 ] && [ $D_GATE_RC -eq 0 ]; then
    echo "ALL CAPTURES PASSED"
    exit 0
else
    echo "SOME CAPTURES FAILED (rc=$RC, gate=$GATE_RC)" >&2
    exit 1
fi