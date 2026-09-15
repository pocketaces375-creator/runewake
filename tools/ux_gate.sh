#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="${ROOT}/artifacts/ux_gate.stamp"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"

echo "=== UX Gate ==="
echo ""

echo "-- Build --"
(cd "$ROOT" && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | tail -3)

echo ""
echo "-- Walkthrough --"
CAPTURES="${ROOT}/artifacts/captures"
mkdir -p "$CAPTURES"
cd "$ROOT"
timeout 300 xvfb-run -a "$GODOT_BIN" --path client -- "--uxwalk" 2>&1 | tee "${CAPTURES}/uxwalk.log" || true

# Parse results into summary table
echo ""
echo "=== UX Walkthrough Summary ==="
printf "%-6s %-8s %s\n" "Step" "Result" "Details"
echo "------------------------------------"
grep -E "PASS s|FAIL s" "${CAPTURES}/uxwalk.log" | while read -r line; do
    step=$(echo "$line" | grep -oP 's\d{2}')
    result=$(echo "$line" | grep -q "PASS" && echo "PASS" || echo "FAIL")
    detail=$(echo "$line" | sed 's/.*PASS s.. //;s/.*FAIL s.. //')
    printf "%-6s %-8s %s\n" "$step" "$result" "$detail"
done

PASS=$(grep -c "PASS s" "${CAPTURES}/uxwalk.log" || true)
FAIL=$(grep -c "FAIL s" "${CAPTURES}/uxwalk.log" || true)
echo "------------------------------------"
echo "Total: ${PASS} pass, ${FAIL} fail"

if [ "$FAIL" -gt 0 ]; then
    echo ""
    echo "UX gate FAILED"
    exit 1
fi

# Write stamp on success
HEAD=$(cd "$ROOT" && git rev-parse HEAD)
echo "$HEAD" > "$STAMP"
echo ""
echo "UX gate passed (stamp: $HEAD)"
