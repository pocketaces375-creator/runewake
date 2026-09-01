#!/usr/bin/env bash
set -euo pipefail
# Dual-resolution duel capture: 2316x1080 standard + 2999x1080 wide + R2 variant.
# Swaps project.godot viewport settings between runs.
# Usage: bash tools/capture_duel.sh

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

# Clean previous captures
mkdir -p "$CAPTURE_DIR"
rm -f "$CAPTURE_DIR"/duel_test*.png "$CAPTURE_DIR"/duel_test*.meta.json "$CAPTURE_DIR"/audio_verify.json

capture_one() {
    local suffix="$1"    # "" or "_wide" or "_r2"
    local width="$2"
    local height="$3"

    echo "=== Capture: duel_test${suffix} (${width}x${height}) ==="

    # Patch project.godot viewport
    sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "$PROJECT_GODOT"
    sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "$PROJECT_GODOT"

    # Run capture
    xvfb-run -a "$GODOT_BIN" --path client -- "--capture=duel_test${suffix}" 2>&1
    local rc=$?

    # Verify output
    if [ -f "$CAPTURE_DIR/duel_test${suffix}.png" ]; then
        local pw ph
        read pw ph <<< "$(python3 -c "
import struct
with open('${CAPTURE_DIR}/duel_test${suffix}.png','rb') as f:
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
        echo "  -> duel_test${suffix}.png: ${pw}x${ph}"
        if [ "$pw" -ne "$width" ] || [ "$ph" -ne "$height" ]; then
            echo "  WARNING: Expected ${width}x${height}, got ${pw}x${ph}" >&2
        fi
    else
        echo "  FAIL: duel_test${suffix}.png not produced" >&2
        rc=1
    fi

    return $rc
}

# 1. Standard capture at 2316x1080
capture_one "" 2316 1080
STD_RC=$?

# 2. Wide capture at 2999x1080
capture_one "_wide" 2999 1080
WIDE_RC=$?

# 3. R2 variant capture at 2316x1080 (larger cards / wider art share)
# BOARD-MATCH-2: one extra capture for Trikzos to compare
capture_one "_r2" 2316 1080
R2_RC=$?

# Restore to standard viewport
sed -i "s/^window\/size\/viewport_width=.*/window\/size\/viewport_width=2316/" "$PROJECT_GODOT"
sed -i "s/^window\/size\/viewport_height=.*/window\/size\/viewport_height=1080/" "$PROJECT_GODOT"

echo "=== Triple capture results ==="
echo "Standard: $([ $STD_RC -eq 0 ] && echo 'PASS' || echo 'FAIL')"
echo "Wide:     $([ $WIDE_RC -eq 0 ] && echo 'PASS' || echo 'FAIL')"
echo "R2:      $([ $R2_RC -eq 0 ] && echo 'PASS' || echo 'FAIL')"

if [ $STD_RC -eq 0 ] && [ $WIDE_RC -eq 0 ] && [ $R2_RC -eq 0 ]; then
    echo "All three captures PASSED"
else
    echo "Capture failed" >&2
    exit 1
fi

# ─── Audio verification gate ───
echo ""
echo "=== Audio Verification Gate ==="
python3 "$ROOT/tools/capture_gate.py" --audio-only 2>&1 || {
    echo "FAIL: Audio verification gate failed" >&2
    exit 1
}
echo "Audio verification PASSED"