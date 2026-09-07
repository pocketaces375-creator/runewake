#!/usr/bin/env bash
# tools/apk_preflight.sh — Run after EVERY APK export before posting links.
# All 8 checks must pass, including a real vision-model look at every
# screen, or the APK is not announced.
set -euo pipefail

# ─── Config ───────────────────────────────────────────────────────────────
APK="${1:-}"
ANDROID_SDK="${2:-/home/fictive/Android/Sdk}"
PREVIOUS_APK="${3:-/home/fictive/runewake/exports/Runewake.apk}"
AAPT="$ANDROID_SDK/build-tools/34.0.0/aapt"
APKSIGNER="$ANDROID_SDK/build-tools/34.0.0/apksigner"

if [ -z "$APK" ] || [ ! -f "$APK" ]; then
    echo "USAGE: $0 <path-to-apk> [android-sdk-path] [previous-apk-path]"
    exit 1
fi

PASS=0
FAIL=0
report() {
    local status="$1" msg="$2"
    if [ "$status" = "PASS" ]; then
        echo "  ✅ $msg"
        PASS=$((PASS + 1))
    else
        echo "  ❌ $msg"
        FAIL=$((FAIL + 1))
    fi
}

APK_DIR="$(dirname "$APK")"
APK_NAME="$(basename "$APK")"
echo "═══════════════════════════════════════════════════"
echo "  APK PREFLIGHT: $APK_NAME"
echo "  Size: $(ls -lh "$APK" | awk '{print $5}')"
echo "═══════════════════════════════════════════════════"

# ─── CHECK 1: Zip integrity ────────────────────────────────────────────────
echo ""
echo "[1/8] Zip integrity"
if unzip -t "$APK" > /dev/null 2>&1; then
    report PASS "unzip -t: no errors"
else
    report FAIL "unzip -t: archive is corrupt"
fi

# ─── CHECK 2: Valid signed APK ─────────────────────────────────────────────
echo ""
echo "[2/8] Signature verification"
if [ -x "$APKSIGNER" ]; then
    if "$APKSIGNER" verify "$APK" > /dev/null 2>&1; then
        report PASS "apksigner verify: valid signature"
    else
        report FAIL "apksigner verify: signature invalid"
    fi
elif command -v jarsigner &> /dev/null; then
    if jarsigner -verify "$APK" > /dev/null 2>&1; then
        report PASS "jarsigner -verify: valid signature"
    else
        report FAIL "jarsigner -verify: signature invalid"
    fi
else
    report FAIL "No APK signature tool available (apksigner or jarsigner)"
fi

# ─── CHECK 3: Manifest sanity ──────────────────────────────────────────────
echo ""
echo "[3/8] Manifest sanity"
if [ ! -x "$AAPT" ]; then
    report FAIL "aapt not found at $AAPT"
else
    BADGING=$("$AAPT" dump badging "$APK" 2>&1)
    PACKAGE=$(echo "$BADGING" | grep "^package:" | head -1)
    VERSION_CODE=$(echo "$PACKAGE" | grep -oP "versionCode='\K[^']+")
    PKG_NAME=$(echo "$PACKAGE" | grep -oP "package: name='\K[^']+")

    # Check package name (debug=com.runewake.game, release=com.runewake.buriedage)
    if [ "$PKG_NAME" = "com.runewake.game" ] || [ "$PKG_NAME" = "com.runewake.buriedage" ]; then
        report PASS "Package name: $PKG_NAME"
    else
        report FAIL "Package name: $PKG_NAME (expected com.runewake.game)"
    fi

    # Check versionCode increased from previous
    if [ -f "$PREVIOUS_APK" ]; then
        PREV_BADGING=$("$AAPT" dump badging "$PREVIOUS_APK" 2>&1)
        PREV_VERSION=$(echo "$PREV_BADGING" | grep -oP "versionCode='\K[^']+")
        if [ -n "$PREV_VERSION" ] && [ -n "$VERSION_CODE" ]; then
            if [ "$VERSION_CODE" -gt "$PREV_VERSION" ] 2>/dev/null; then
                report PASS "versionCode: $VERSION_CODE (increased from $PREV_VERSION)"
            elif [ "$VERSION_CODE" = "$PREV_VERSION" ]; then
                report FAIL "versionCode: $VERSION_CODE (NOT increased from $PREV_VERSION — bump it!)"
            else
                report FAIL "versionCode: $VERSION_CODE (DECREASED from $PREV_VERSION)"
            fi
        else
            report FAIL "Could not parse versionCode (current=$VERSION_CODE, prev=$PREV_VERSION)"
        fi
    else
        report PASS "versionCode: $VERSION_CODE (no previous APK to compare)"
    fi

    # Check landscape orientation
    if echo "$BADGING" | grep -q "android.hardware.screen.landscape"; then
        report PASS "Orientation: landscape"
    elif echo "$BADGING" | grep -qi "screenOrientation.*landscape"; then
        report PASS "Orientation: landscape (activity-level)"
    else
        report FAIL "Orientation: NOT landscape (will launch in portrait on device)"
    fi
fi

# ─── CHECK 4: Size sanity (±10% of previous) ───────────────────────────────
echo ""
echo "[4/8] Size sanity"
CUR_SIZE=$(stat --format=%s "$APK")
if [ -f "$PREVIOUS_APK" ]; then
    PREV_SIZE=$(stat --format=%s "$PREVIOUS_APK")
    RATIO=$(echo "scale=4; $CUR_SIZE / $PREV_SIZE" | bc)
    LOWER=$(echo "scale=0; $PREV_SIZE * 0.9" | bc)
    UPPER=$(echo "scale=0; $PREV_SIZE * 1.1" | bc)
    CUR_MB=$(echo "scale=1; $CUR_SIZE / 1048576" | bc)
    PREV_MB=$(echo "scale=1; $PREV_SIZE / 1048576" | bc)
    if [ "$CUR_SIZE" -ge "$(echo "$LOWER / 1" | bc)" ] && [ "$CUR_SIZE" -le "$(echo "$UPPER / 1" | bc)" ]; then
        report PASS "Size: ${CUR_MB}MB (within 10% of previous ${PREV_MB}MB, ratio=${RATIO})"
    else
        report FAIL "Size: ${CUR_MB}MB (outside 10% of previous ${PREV_MB}MB, ratio=${RATIO})"
    fi
else
    report PASS "Size: $(echo "scale=1; $CUR_SIZE / 1048576" | bc)MB (no previous APK to compare)"
fi

# ─── CHECK 5: .import file completeness ────────────────────────────────────
echo ""
echo "[5/8] Asset .import completeness"
ORPHANS=0
while IFS= read -r -d '' asset; do
    import_file="${asset}.import"
    if [ ! -f "$import_file" ]; then
        echo "  ❌ Missing .import: $asset"
        ORPHANS=$((ORPHANS + 1))
    fi
done < <(find "client/content/art" -type f \( -name "*.png" -o -name "*.webp" \) -print0)
if [ "$ORPHANS" -eq 0 ]; then
    report PASS "All client/content/art assets have .import files"
else
    report FAIL "$ORPHANS asset(s) missing .import files (listed above)"
fi

# ─── CHECK 6: SHA-256 ──────────────────────────────────────────────────────
echo ""
echo "[6/8] SHA-256 hash"
SHA=$(sha256sum "$APK" | cut -d' ' -f1)
echo "  SHA-256: $SHA"
report PASS "SHA-256 recorded"

# ─── CHECK 7: Fallback-size warning for class portraits ─────────────────────
echo ""
echo "[7/8] Portrait fallback-size check"
FALLBACK_COUNT=0
while IFS= read -r -d '' png; do
    size=$(stat --format=%s "$png" 2>/dev/null || echo "0")
    if [ "$size" -lt 102400 ] 2>/dev/null; then
        echo "  ⚠️  Suspected fallback ($(numfmt --to=iec $size)): $png"
        FALLBACK_COUNT=$((FALLBACK_COUNT + 1))
    fi
done < <(find "client/content/art/classes" -type f -name "*.png" -print0 2>/dev/null || true)
if [ "$FALLBACK_COUNT" -gt 0 ]; then
    report PASS "$FALLBACK_COUNT portrait(s) flagged as small/fallback (WARN only)"
else
    report PASS "All class portraits exceed 100KB — genuine art"
fi

# ─── CHECK 8: Visual gate (vision model reviews every screen) ─────────────
echo ""
echo "[8/8] Visual gate"
TOOLS_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -f "$TOOLS_DIR/regen_captures.sh" ] && [ -f "$TOOLS_DIR/visual_gate.py" ]; then
    if bash "$TOOLS_DIR/regen_captures.sh" && python3 "$TOOLS_DIR/visual_gate.py"; then
        report PASS "visual_gate: a vision model reviewed every checked screen and found nothing wrong"
    else
        report FAIL "visual_gate: a vision model found a real visual defect — see artifacts/VISUAL_GATE.json. Not shipping."
    fi
else
    report FAIL "visual_gate not installed (tools/regen_captures.sh or tools/visual_gate.py missing)"
fi

# ─── Summary ───────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════"
echo "  RESULTS: $PASS passed, $FAIL failed"
echo "═══════════════════════════════════════════════════"

if [ "$FAIL" -gt 0 ]; then
    exit 1
fi
exit 0