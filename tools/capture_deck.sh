#!/usr/bin/env bash
set -euo pipefail
# Dual-resolution deck builder capture: 2316x1080 standard + 390x844 phone.
# Swaps project.godot viewport settings between runs.
# Usage: bash tools/capture_deck.sh

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

# Clean previous captures
mkdir -p "$CAPTURE_DIR"
rm -f "$CAPTURE_DIR"/deck_test*.png "$CAPTURE_DIR"/deck_test*.meta.json

capture_one() {
    local suffix="$1"    # "" or "_phone"
    local width="$2"
    local height="$3"

    echo "=== Capture: deck_test${suffix} (${width}x${height}) ==="

    # Patch project.godot viewport
    sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width='"${width}"'|' "$PROJECT_GODOT"
    sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height='"${height}"'|' "$PROJECT_GODOT"

    # Run capture
    xvfb-run -a "$GODOT_BIN" --path client -- "--capture=deck_test${suffix}" 2>&1
    local rc=$?

    # Verify output
    if [ -f "$CAPTURE_DIR/deck_test${suffix}.png" ]; then
        local pw ph
        read pw ph <<< "$(python3 -c "
import struct
with open('${CAPTURE_DIR}/deck_test${suffix}.png','rb') as f:
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
        echo "  → deck_test${suffix}.png: ${pw}x${ph}"
        if [ "$pw" -ne "$width" ] || [ "$ph" -ne "$height" ]; then
            echo "  ⚠ WARNING: Expected ${width}x${height}, got ${pw}x${ph}" >&2
        fi
    else
        echo "  ✗ FAIL: deck_test${suffix}.png not produced" >&2
        rc=1
    fi

    return $rc
}

# 1. Standard capture at 2316x1080
capture_one "" 2316 1080
STD_RC=$?

# 2. Phone capture
capture_one "_phone" 390 844
PHONE_RC=$?

# Restore to standard viewport
sed -i "s/^window\/size\/viewport_width=.*/window\/size\/viewport_width=2316/" "$PROJECT_GODOT"
sed -i "s/^window\/size\/viewport_height=.*/window\/size\/viewport_height=1080/" "$PROJECT_GODOT"

echo "=== Dual capture results ==="
echo "Standard: $([ $STD_RC -eq 0 ] && echo 'PASS' || echo 'FAIL')"
echo "Phone:    $([ $PHONE_RC -eq 0 ] && echo 'PASS' || echo 'FAIL')"

if [ $STD_RC -eq 0 ] && [ $PHONE_RC -eq 0 ]; then
    echo "Both captures PASSED"
    exit 0
else
    echo "Capture failed" >&2
    exit 1
fi