#!/usr/bin/env bash
# Runewake Layout Verification Gate
# Exits with code 0 if layout checks pass, 1 if they fail.
#
# Builds a Linux headless binary, launches it with --verify,
# and checks the exit code from the verification gate.
#
# Usage: bash client/verify_layout.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLIENT_DIR="$REPO_ROOT/client"
VERIFY_BIN="/tmp/runewake_verify"

echo "=== Layout Verification Gate ===" >&2
echo "  Building verification binary..." >&2

cd "$CLIENT_DIR"

# Build a Linux headless binary with verification enabled
# The --export-release uses less debug overhead
set +e
build_output=$(xvfb-run -a godot --headless --editor --export-release "Linux/X11" "$VERIFY_BIN" 2>&1)
build_exit=$?
set -e

if [ "$build_exit" -ne 0 ]; then
  echo "  ❌ Build failed — cannot run verification" >&2
  echo "$build_output" | tail -5 >&2
  exit 1
fi

if [ ! -f "$VERIFY_BIN" ]; then
  echo "  ❌ Build produced no binary at $VERIFY_BIN" >&2
  exit 1
fi

BIN_SIZE=$(stat --printf="%s" "$VERIFY_BIN")
echo "  Binary size: $(( BIN_SIZE / 1024 )) KB" >&2

# Run with --verify flag
echo "  Running verification..." >&2
echo "" >&2

set +e
# Run with xvfb to provide a virtual display (needed for the Linux export binary)
xvfb-run -a "$VERIFY_BIN" --verify 2>&1
verify_exit=$?
set -e

echo "" >&2

if [ "$verify_exit" -eq 0 ]; then
  echo "=== RESULT: ✅ PASS ===" >&2
  echo "  All layout checks passed." >&2
  rm -f "$VERIFY_BIN"
  exit 0
else
  echo "=== RESULT: ❌ FAIL ===" >&2
  echo "  Layout verification detected issues (exit code $verify_exit)." >&2
  echo "  Fix the issues and re-run." >&2
  rm -f "$VERIFY_BIN"
  exit 1
fi