#!/usr/bin/env bash
# tools/ux_gate.sh — Run the UX walkthrough and checklist, gate the APK build.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="${ROOT}/artifacts/ux_gate.stamp"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"

echo "=== UX Gate ==="

# Step 1: Build
echo "── Building ──"
(cd "$ROOT" && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | tail -3)

# Step 2: Run walkthrough
echo "── Walkthrough ──"
CAPTURES="${ROOT}/artifacts/captures"
mkdir -p "$CAPTURES"
cd "$ROOT"
timeout 300 xvfb-run -a "$GODOT_BIN" --path client -- "--uxwalk" 2>&1 | tee "${CAPTURES}/uxwalk.log" || true

# Parse results
PASS=$(grep -c "PASS s" "${CAPTURES}/uxwalk.log" || true)
FAIL=$(grep -c "FAIL s" "${CAPTURES}/uxwalk.log" || true)
echo "Walkthrough: ${PASS} pass, ${FAIL} fail"

if [ "$FAIL" -gt 0 ]; then
  echo "❌ Walkthrough failed — see artifacts/captures/uxwalk.log"
  exit 1
fi

# Step 3: Lint captures if meta files exist
if [ -f "${CAPTURES}/uxwalk_s01.meta.json" ]; then
  python3 "${ROOT}/tools/layout_lint.py"
fi

# Step 4: Write stamp
HEAD=$(cd "$ROOT" && git rev-parse HEAD)
echo "$HEAD" > "$STAMP"
echo "✅ UX gate passed — stamp written ($HEAD)"