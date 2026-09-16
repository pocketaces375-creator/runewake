#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APK="${1:-}"
if [ -z "$APK" ] || [ ! -f "$APK" ]; then
    echo "USAGE: $0 <apk-path>"
    exit 1
fi
APKSIGNER="/home/fictive/Android/Sdk/build-tools/34.0.0/apksigner"
AAPT="/home/fictive/Android/Sdk/build-tools/34.0.0/aapt"
ID="$ROOT/exports/SIGNING_IDENTITY.txt"
CERT_FP=$(grep "^cert_sha256=" "$ID" | cut -d= -f2 2>/dev/null || echo "")
EXP_PKG=$(grep "^package_name=" "$ID" | cut -d= -f2 2>/dev/null || echo "")
LAST_VER=$(grep "^last_version_code=" "$ID" | cut -d= -f2 2>/dev/null || echo "0")
PASS=0; FAIL=0

echo "=== SIGNING IDENTITY CHECK ==="
echo ""

echo "SIGNING CERTIFICATE"
BADGING=$("$AAPT" dump badging "$APK" 2>/dev/null)

PKG=$(echo "$BADGING" | head -1 | awk -F"'" '{print $2}')
VER=$(echo "$BADGING" | head -1 | awk -F"'" '{print $4}')
CERT=$("$APKSIGNER" verify --print-certs "$APK" 2>/dev/null | grep "SHA-256" | head -1 | sed 's/.* //')

if [ -z "$CERT" ]; then
    echo "  FAIL: could not read cert"; FAIL=$((FAIL + 1))
elif [ "$CERT" != "$CERT_FP" ]; then
    echo "  FAIL: got $CERT, expected $CERT_FP"; FAIL=$((FAIL + 1))
else
    echo "  PASS: $CERT"; PASS=$((PASS + 1))
fi

echo ""; echo "PACKAGE NAME"
if [ -z "$PKG" ]; then
    echo "  FAIL: could not read package"; FAIL=$((FAIL + 1))
elif [ "$PKG" != "$EXP_PKG" ]; then
    echo "  FAIL: got $PKG, expected $EXP_PKG"; FAIL=$((FAIL + 1))
else
    echo "  PASS: $PKG"; PASS=$((PASS + 1))
fi

echo ""; echo "VERSION CODE"
if [ -z "$VER" ]; then
    echo "  FAIL: could not read versionCode"; FAIL=$((FAIL + 1))
elif [ "$VER" -le "$LAST_VER" ] 2>/dev/null; then
    echo "  FAIL: $VER <= last shipped $LAST_VER"; FAIL=$((FAIL + 1))
else
    echo "  PASS: $VER > $LAST_VER"; PASS=$((PASS + 1))
fi

echo ""; echo "Result: $PASS pass, $FAIL fail"
exit $FAIL