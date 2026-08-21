#!/usr/bin/env bash
# tools/export_and_verify.sh — Full export + preflight + delivery pipeline.
# Usage: tools/export_and_verify.sh [--debug|--release] [--skip-deliver]
set -euo pipefail

MODE="${1:-debug}"
SKIP_DELIVER=false
if [ "$MODE" = "--skip-deliver" ]; then
    SKIP_DELIVER=true
    MODE="debug"
elif [ "$MODE" = "--release" ]; then
    MODE="release"
    shift 2>/dev/null || true
    if [ "${1:-}" = "--skip-deliver" ]; then SKIP_DELIVER=true; fi
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLIENT_DIR="$REPO_ROOT/client"
EXPORT_DIR="$CLIENT_DIR/exports"
PREVIOUS_APK="$REPO_ROOT/exports/Runewake.apk"

echo "═══════════════════════════════════════════════════"
echo "  EXPORT + PREFLIGHT + DELIVERY"
echo "  Mode: $MODE"
echo "═══════════════════════════════════════════════════"

# ─── Step 1: Clean build directory ────────────────────────────────────────
echo ""
echo "── Cleaning build directory ──"
rm -rf "$EXPORT_DIR" "$CLIENT_DIR/android/build/build"
mkdir -p "$EXPORT_DIR"
echo "  Cleaned."

# ─── Step 2: Dotnet build ─────────────────────────────────────────────────
echo ""
echo "── dotnet build (C# check) ──"
cd "$CLIENT_DIR"
if dotnet build 2>&1 | tail -5; then
    echo "  ✅ dotnet build succeeded"
else
    echo "  ❌ dotnet build failed"
    exit 1
fi

# ─── Step 3: Godot export ─────────────────────────────────────────────────
echo ""
echo "── Godot export ──"
if [ "$MODE" = "release" ]; then
    PRESET="Android Release"
    OUTFILE="Runewake-release.apk"
else
    PRESET="Android"
    OUTFILE="Runewake.apk"
fi

cd "$CLIENT_DIR"
if godot --headless --export-debug "$PRESET" "exports/$OUTFILE" 2>&1; then
    echo "  ✅ Export complete: $OUTFILE"
else
    echo "  ❌ Export failed"
    exit 1
fi

APK="$EXPORT_DIR/$OUTFILE"
if [ ! -f "$APK" ]; then
    echo "  ❌ APK not found at $APK"
    exit 1
fi

# ─── Step 4: Preflight ────────────────────────────────────────────────────
echo ""
echo "── Preflight checks ──"
if bash "$REPO_ROOT/tools/apk_preflight.sh" "$APK" /home/fictive/Android/Sdk "$PREVIOUS_APK"; then
    echo "  ✅ Preflight PASSED"
else
    echo "  ❌ Preflight FAILED — APK not announced"
    exit 1
fi

# ─── Step 5: Copy to previous-APK location ────────────────────────────────
echo ""
echo "── Archiving as previous APK reference ──"
mkdir -p "$REPO_ROOT/exports"
cp "$APK" "$PREVIOUS_APK"
echo "  Copied to $PREVIOUS_APK"

# ─── Step 6: Deliver (unless skipped) ─────────────────────────────────────
if [ "$SKIP_DELIVER" = false ]; then
    echo ""
    echo "── Delivery + round-trip verification ──"
    bash "$REPO_ROOT/tools/apk_deliver.sh" "$APK"
else
    echo ""
    echo "── Delivery skipped (--skip-deliver flag) ──"
    echo "  APK at: $APK"
    echo "  SHA-256: $(sha256sum "$APK" | cut -d' ' -f1)"
fi

echo ""
echo "═══════════════════════════════════════════════════"
echo "  EXPORT PIPELINE COMPLETE"
echo "═══════════════════════════════════════════════════"