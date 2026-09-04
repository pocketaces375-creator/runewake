#!/bin/bash
# capture_shop.sh — Capture the Card Shop (rotating card shop) at both resolutions.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

# Ensure captures directory
mkdir -p "$CAPTURE_DIR"
rm -f "$CAPTURE_DIR"/shop_test*.png "$CAPTURE_DIR"/shop_test*.meta.json

capture_one() {
    local suffix="$1"    # "" or "_wide"
    local width="$2"
    local height="$3"

    echo "=== Capture: shop_test${suffix} (${width}x${height}) ==="

    # Patch project.godot viewport
    sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "$PROJECT_GODOT"
    sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "$PROJECT_GODOT"

    # Run capture
    timeout 600 xvfb-run -a "$GODOT_BIN" --path "$ROOT/client" -- "--capture=shop_test${suffix}" 2>&1 |
        grep -E '(CardShopScene|FAIL|Error|ERROR|Main)' || true
    local rc=$?

    # Verify output
    if [ -f "$CAPTURE_DIR/shop_test${suffix}.png" ]; then
        local pw ph
        read pw ph <<< "$(python3 -c "
import struct
with open('${CAPTURE_DIR}/shop_test${suffix}.png','rb') as f:
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
        echo "  -> shop_test${suffix}.png: ${pw}x${ph}"
        if [ "$pw" -ne "$width" ] || [ "$ph" -ne "$height" ]; then
            echo "  WARNING: Expected ${width}x${height}, got ${pw}x${ph}" >&2
        fi
    else
        echo "  FAIL: shop_test${suffix}.png not produced" >&2
        rc=1
    fi

    # Check layout.json
    if [ -f "$CAPTURE_DIR/shop_test${suffix}.layout.json" ]; then
        echo "  -> layout.json present"
    else
        echo "  WARNING: shop_test${suffix}.layout.json not produced" >&2
    fi

    return $rc
}

# Capture standard (2316x1080)
capture_one "" 2316 1080

echo ""

# Capture wide (2999x1080)
capture_one "_wide" 2999 1080

# ---- Restore project.godot to standard resolution ----
sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width=2316|' "$PROJECT_GODOT"
sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height=1080|' "$PROJECT_GODOT"

echo ""
echo "=== Running gate ==="
python3 "$ROOT/tools/capture_gate.py" shop_test
echo ""
python3 "$ROOT/tools/capture_gate.py" shop_test_wide

echo ""
echo "=== Done ==="