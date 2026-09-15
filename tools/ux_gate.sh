#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STAMP="${ROOT}/artifacts/ux_gate.stamp"
CAPTURES="${ROOT}/artifacts/captures"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"

echo "=== UX Gate ==="
echo ""

echo "-- Build --"
(cd "$ROOT" && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | tail -3)

echo ""
echo "-- Walkthrough --"
mkdir -p "$CAPTURES"
cd "$ROOT"
EXIT_CODE=0
timeout 300 xvfb-run -a "$GODOT_BIN" --path client -- "--uxwalk" 2>&1 | tee "${CAPTURES}/uxwalk.log" || EXIT_CODE=$?

echo ""
echo "=== UX Walkthrough Results ==="

# Condition 1: Godot exit code
if [ "$EXIT_CODE" -ne 0 ]; then
    echo "  1/4: Godot exited with code $EXIT_CODE (expected 0)"
else
    echo "  1/4: Godot exit code = 0"
fi

# Condition 2: COMPLETE line
if grep -q "COMPLETE steps=12" "${CAPTURES}/uxwalk.log" 2>/dev/null; then
    echo "  2/4: Walkthrough reached COMPLETE"
else
    echo "  2/4: No COMPLETE steps=12 line"
fi

# Condition 3: FAIL count
FAIL_COUNT=$(grep -c "FAIL s" "${CAPTURES}/uxwalk.log" 2>/dev/null || echo "0")
PASS_COUNT=$(grep -c "PASS s" "${CAPTURES}/uxwalk.log" 2>/dev/null || echo "0")
if [ "$FAIL_COUNT" -eq 0 ]; then
    echo "  3/4: $PASS_COUNT pass, 0 fail"
else
    echo "  3/4: $FAIL_COUNT failure(s) detected ($PASS_COUNT pass)"
fi

# Condition 4: All 12 captures
MISSING=0
for s in $(seq -w 1 12); do
    f="${CAPTURES}/uxwalk_s${s}.png"
    if [ ! -f "$f" ]; then
        echo "  4/4: Missing uxwalk_s${s}.png"
        MISSING=$((MISSING + 1))
    elif [ "$(stat -c%s "$f" 2>/dev/null || echo 0)" -lt 10000 ]; then
        echo "  4/4: uxwalk_s${s}.png too small"
        MISSING=$((MISSING + 1))
    fi
done
if [ "$MISSING" -eq 0 ]; then
    echo "  4/4: All 12 captures present and > 10KB"
fi

# Summary
echo ""
echo "=== Step Summary ==="
printf "%-6s %-8s %s\n" "Step" "Result" "Details"
echo "------------------------------------"
grep -E "PASS s|FAIL s" "${CAPTURES}/uxwalk.log" 2>/dev/null | while read -r line; do
    step=$(echo "$line" | grep -oP 's\d{2}')
    if echo "$line" | grep -q "PASS"; then
        result="PASS"; detail=$(echo "$line" | sed 's/.*PASS s.. //')
    else
        result="FAIL"; detail=$(echo "$line" | sed 's/.*FAIL s.. //')
    fi
    printf "%-6s %-8s %s\n" "$step" "$result" "$detail"
done
echo "------------------------------------"
echo "Total: ${PASS_COUNT} pass, ${FAIL_COUNT} fail"

# Verdict
if [ "$EXIT_CODE" -eq 0 ] && grep -q "COMPLETE steps=12" "${CAPTURES}/uxwalk.log" 2>/dev/null && [ "$FAIL_COUNT" -eq 0 ] && [ "$MISSING" -eq 0 ]; then
    HEAD=$(cd "$ROOT" && git rev-parse HEAD)
    echo "$HEAD" > "$STAMP"
    echo ""
    echo "UX gate PASSED (stamp: $HEAD)"
else
    echo ""
    echo "UX gate FAILED"
    exit 1
fi