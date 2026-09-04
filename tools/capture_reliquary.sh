#!/bin/bash
# capture_reliquary.sh — Capture the Reliquary (collection browser) at both resolutions.
# Captures: filtered (EMBER) + unfiltered (ALL) at standard and wide.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

# Ensure captures directory
mkdir -p "$CAPTURE_DIR"
rm -f "$CAPTURE_DIR"/reliquary_test*.png "$CAPTURE_DIR"/reliquary_test*.meta.json

capture_one() {
    local suffix="$1"    # e.g. "" or "_wide" or "_all" or "_all_wide"
    local width="$2"
    local height="$3"

    echo "=== Capture: reliquary_test${suffix} (${width}x${height}) ==="

    # Patch project.godot viewport
    sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "$PROJECT_GODOT"
    sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "$PROJECT_GODOT"

    # Run capture (no --headless — xvfb provides the display, GetImage() works)
    timeout 600 xvfb-run -a "$GODOT_BIN" --path "$ROOT/client" -- "--capture=reliquary_test${suffix}" 2>&1 |
        grep -E '(Reliquary|FAIL|Error|ERROR|Main)' || true
    local rc=$?

    # Verify output
    if [ -f "$CAPTURE_DIR/reliquary_test${suffix}.png" ]; then
        local pw ph
        read pw ph <<< "$(python3 -c "
import struct
with open('${CAPTURE_DIR}/reliquary_test${suffix}.png','rb') as f:
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
        echo "  -> reliquary_test${suffix}.png: ${pw}x${ph}"
        if [ "$pw" -ne "$width" ] || [ "$ph" -ne "$height" ]; then
            echo "  WARNING: Expected ${width}x${height}, got ${pw}x${ph}" >&2
        fi
    else
        echo "  FAIL: reliquary_test${suffix}.png not produced" >&2
        rc=1
    fi

    # Check meta.json
    if [ -f "$CAPTURE_DIR/reliquary_test${suffix}.meta.json" ]; then
        echo "  -> meta.json present"
    else
        echo "  WARNING: reliquary_test${suffix}.meta.json not produced" >&2
    fi

    return $rc
}

# ── Filtered captures (EMBER strata, via --capture=reliquary_test) ──
capture_one "" 2316 1080

echo ""

# ── Wide filtered (EMBER, via --capture=reliquary_test_wide) ──
capture_one "_wide" 2999 1080

echo ""

# ── Unfiltered captures (ALL strata, via --capture=reliquary_test_all) ──
capture_one "_all" 2316 1080

echo ""

# ── Wide unfiltered (ALL, via --capture=reliquary_test_all_wide) ──
capture_one "_all_wide" 2999 1080

# ---- Restore project.godot to standard resolution ----
sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width=2316|' "$PROJECT_GODOT"
sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height=1080|' "$PROJECT_GODOT"

echo ""
echo "=== Running gate ==="
python3 "$ROOT/tools/capture_gate.py" reliquary_test
echo ""
python3 "$ROOT/tools/capture_gate.py" reliquary_test_wide
echo ""
python3 "$ROOT/tools/capture_gate.py" reliquary_test_all
echo ""
python3 "$ROOT/tools/capture_gate.py" reliquary_test_all_wide

echo ""
echo "=== Done ==="