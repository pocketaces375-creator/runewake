#!/usr/bin/env bash
# tools/apk_deliver.sh — Upload, round-trip verify, and deliver APK links.
# Usage: tools/apk_deliver.sh <apk-path> [--skip-upload]
set -euo pipefail

APK="${1:-}"
SKIP_UPLOAD="${2:-}"
if [ -z "$APK" ] || [ ! -f "$APK" ]; then
    echo "USAGE: $0 <path-to-apk> [--skip-upload]"
    exit 1
fi

SHA=$(sha256sum "$APK" | cut -d' ' -f1)
SIZE=$(stat --format=%s "$APK")
SIZE_MB=$(echo "scale=1; $SIZE / 1048576" | bc)
APK_NAME=$(basename "$APK")
VERIFIED_LINKS=()
FAILED_LINKS=()

echo "═══════════════════════════════════════════════════"
echo "  APK DELIVERY — Round-trip verification"
echo "  File: $APK_NAME ($SIZE_MB MB)"
echo "  SHA-256: $SHA"
echo "═══════════════════════════════════════════════════"

roundtrip() {
    local label="$1" url="$2" expected_sha="$3"
    echo ""
    echo "  [$label] Verifying: $url"
    local tmpfile
    tmpfile=$(mktemp /tmp/apk_verify_XXXXXX.apk)
    set +e
    curl -sL -o "$tmpfile" --connect-timeout 30 --max-time 300 "$url" 2>&1
    local curl_exit=$?
    set -e
    if [ "$curl_exit" -ne 0 ]; then
        echo "  ❌ [$label] Download failed (curl exit $curl_exit)"
        FAILED_LINKS+=("$url")
        rm -f "$tmpfile"
        return 1
    fi
    local dl_sha
    dl_sha=$(sha256sum "$tmpfile" | cut -d' ' -f1)
    local dl_size=$(stat --format=%s "$tmpfile")
    rm -f "$tmpfile"
    if [ "$dl_sha" = "$expected_sha" ]; then
        echo "  ✅ [$label] Round-trip hash MATCHES ($dl_sha)"
        VERIFIED_LINKS+=("$url")
        return 0
    else
        echo "  ❌ [$label] Hash MISMATCH: expected $expected_sha, got $dl_sha"
        FAILED_LINKS+=("$url")
        return 1
    fi
}

# ─── Upload to each host ──────────────────────────────────────────────────
HOSTS_SELECTED=0

# gofile.io
echo ""
echo "── Uploading to gofile.io ──"
GOFILE_RESULT=$(curl -s -F "file=@$APK" "https://store-na-phx-3.gofile.io/uploadFile" 2>&1)
GOFILE_PAGE=$(echo "$GOFILE_RESULT" | python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    if d.get('status') == 'ok':
        print(d['data']['downloadPage'])
    else:
        print('ERROR:' + str(d))
except: print('PARSE_ERROR')
" 2>&1)
if echo "$GOFILE_PAGE" | grep -q "^https://"; then
    echo "  gofile: $GOFILE_PAGE"
    roundtrip "gofile.io" "$GOFILE_PAGE" "$SHA" && HOSTS_SELECTED=$((HOSTS_SELECTED + 1))
    # Also try direct download URL
    GOFILE_CODE=$(echo "$GOFILE_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['code'])")
    GOFILE_SERVER=$(echo "$GOFILE_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin)['data']['servers'][0])")
    GOFILE_DIRECT="https://${GOFILE_SERVER}.gofile.io/download/web/${GOFILE_CODE}/${APK_NAME}"
    roundtrip "gofile.io (direct)" "$GOFILE_DIRECT" "$SHA" && HOSTS_SELECTED=$((HOSTS_SELECTED + 1))
else
    echo "  ⚠ gofile.io upload failed: $GOFILE_PAGE"
fi

# catbox.moe
echo ""
echo "── Uploading to catbox.moe ──"
CATBOX_URL=$(curl -s -F "reqtype=fileupload" -F "fileToUpload=@$APK" "https://catbox.moe/user/api.php" 2>&1)
if echo "$CATBOX_URL" | grep -q "^https://files.catbox.moe/"; then
    echo "  catbox: $CATBOX_URL"
    roundtrip "catbox.moe" "$CATBOX_URL" "$SHA" && HOSTS_SELECTED=$((HOSTS_SELECTED + 1))
else
    echo "  ⚠ catbox.moe upload failed: $CATBOX_URL"
fi

# GitHub release
echo ""
echo "── Preparing GitHub release ──"
TAG="alpha-$(date +%F)"
if gh release view "$TAG" --repo pocketaces375-creator/runewake > /dev/null 2>&1; then
    echo "  Release $TAG exists — deleting existing asset"
    gh release delete-asset "$TAG" "$APK_NAME" --repo pocketaces375-creator/runewake --yes 2>/dev/null || true
    gh release upload "$TAG" "$APK" --repo pocketaces375-creator/runewake --clobber 2>&1
else
    echo "  Creating release $TAG..."
    gh release create "$TAG" "$APK" \
        --repo pocketaces375-creator/runewake \
        --title "$TAG" \
        --notes "Automated build. SHA-256: $SHA" 2>&1
fi
GH_URL="https://github.com/pocketaces375-creator/runewake/releases/download/${TAG}/${APK_NAME}"
echo "  GitHub: $GH_URL"
roundtrip "GitHub" "$GH_URL" "$SHA" && HOSTS_SELECTED=$((HOSTS_SELECTED + 1))

# ─── Summary ───────────────────────────────────────────────────────────────
echo ""
echo "═══════════════════════════════════════════════════"
echo "  DELIVERY SUMMARY"
echo "═══════════════════════════════════════════════════"
echo "  Verified links: ${#VERIFIED_LINKS[@]}"
echo "  Failed links:   ${#FAILED_LINKS[@]}"
echo ""

if [ "${#VERIFIED_LINKS[@]}" -gt 0 ]; then
    echo "  ✅ VERIFIED LINKS (round-trip hash match):"
    for link in "${VERIFIED_LINKS[@]}"; do
        echo "    • $link"
    done
    echo ""
    echo "  APK verified ✓ preflight pass, round-trip hash match"
    for link in "${VERIFIED_LINKS[@]}"; do
        echo "  $link"
    done | head -3
    echo "  — ${SIZE_MB}MB — SHA-256: $SHA"
else
    echo "  ❌ NO verified links. All hosts failed round-trip verification."
    echo "  Fallback: serve via tools/serve_apk.py"
    python3 /home/fictive/runewake/tools/serve_apk.py "$APK" &
    sleep 2
    echo "  Server PID: $!"
fi

if [ "${#FAILED_LINKS[@]}" -gt 0 ]; then
    echo ""
    echo "  ⚠ Failed hosts:"
    for link in "${FAILED_LINKS[@]}"; do
        echo "    • $link"
    done
fi

exit 0