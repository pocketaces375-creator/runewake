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

# ─── Keychain (release mode only) ───────────────────────────────────────────
RELEASE_ENV="$HOME/.runewake/release.env"
if [ "$MODE" = "release" ]; then
    if [ ! -f "$RELEASE_ENV" ]; then
        echo "  ❌ Release keystore not found at $RELEASE_ENV"
        echo "     Run the KEYSTORE-1 task to generate it."
        exit 1
    fi
    source "$RELEASE_ENV"
    echo "  🔑 Keystore: $KEYSTORE_PATH (alias: $KEYSTORE_ALIAS)"
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
rm -rf "$EXPORT_DIR" "$CLIENT_DIR/android/build"
# Extract a fresh Android build template (the source zip is the canonical copy)
ANDROID_SOURCE_ZIP="/home/fictive/.local/share/godot/export_templates/4.3.stable.mono/android_source.zip"
mkdir -p "$CLIENT_DIR/android/build"
if unzip -q -o "$ANDROID_SOURCE_ZIP" -d "$CLIENT_DIR/android/build" 2>/dev/null; then
    echo "  ✅ Android build template extracted"
else
    echo "  ❌ Failed to extract Android build template from $ANDROID_SOURCE_ZIP"
    exit 1
fi
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

# ─── Step 3: Prepare keystore (release only) ─────────────────────────────────
if [ "$MODE" = "release" ]; then
    echo ""
    echo "── Preparing release keystore ──"
    mkdir -p "$EXPORT_DIR"
    cp "$KEYSTORE_PATH" "$EXPORT_DIR/release.keystore"
    chmod 600 "$EXPORT_DIR/release.keystore"
    # Write password into export_presets.cfg (it's in .gitignore, never committed)
    cd "$CLIENT_DIR"
    if grep -q 'keystore/release_password=' export_presets.cfg; then
        sed -i "s|keystore/release_password=\".*\"|keystore/release_password=\"$KEYSTORE_PASSWORD\"|" export_presets.cfg
    fi
    echo "  ✅ Keystore staged at $EXPORT_DIR/release.keystore"
fi

# ─── Step 4: Godot export ─────────────────────────────────────────────────
echo ""
echo "── Godot export ──"
if [ "$MODE" = "release" ]; then
    PRESET="Android Release"
    OUTFILE="Runewake-release.apk"
    EXPORT_FLAG="--export-release"
else
    PRESET="Android"
    OUTFILE="Runewake.apk"
    EXPORT_FLAG="--export-debug"
fi

cd "$CLIENT_DIR"
if godot --headless "$EXPORT_FLAG" "$PRESET" "exports/$OUTFILE" 2>&1; then
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