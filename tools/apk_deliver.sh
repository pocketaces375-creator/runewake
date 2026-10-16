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
# FABLE-063: every upload has a time limit. A host that stalls used to hang the whole ship at
# "── Delivery ──" with the APK already built (seen as "ADB stall" — it was an upload or the
# fallback server below holding the output pipe open). GitHub goes first: it is the link that works.
HOSTS_SELECTED=0
UPLOAD_LIMIT="${APK_UPLOAD_SECONDS:-900}"

# GitHub release
echo ""
echo "── Preparing GitHub release ──"
TAG="alpha-$(date +%F)"
if timeout 60 gh release view "$TAG" --repo pocketaces375-creator/runewake > /dev/null 2>&1; then
    echo "  Release $TAG exists — replacing the asset"
    timeout 120 gh release delete-asset "$TAG" "$APK_NAME" --repo pocketaces375-creator/runewake --yes 2>/dev/null || true
    timeout "$UPLOAD_LIMIT" gh release upload "$TAG" "$APK" --repo pocketaces375-creator/runewake --clobber 2>&1 || echo "  ⚠ GitHub upload failed or timed out"
else
    echo "  Creating release $TAG..."
    timeout "$UPLOAD_LIMIT" gh release create "$TAG" "$APK" \
        --repo pocketaces375-creator/runewake \
        --title "$TAG" \
        --notes "Automated build. SHA-256: $SHA" 2>&1 || echo "  ⚠ GitHub release failed or timed out"
fi
GH_URL="https://github.com/pocketaces375-creator/runewake/releases/download/${TAG}/${APK_NAME}"
echo "  GitHub: $GH_URL"
roundtrip "GitHub" "$GH_URL" "$SHA" && HOSTS_SELECTED=$((HOSTS_SELECTED + 1))

# gofile.io
echo ""
echo "── Uploading to gofile.io ──"
GOFILE_RESULT=$(curl -s --connect-timeout 20 --max-time "$UPLOAD_LIMIT" -F "file=@$APK" "https://upload.gofile.io/uploadfile" 2>&1 || true)
GOFILE_PAGE=$(echo "$GOFILE_RESULT" | python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
    if d.get('status') == 'ok':
        print(d['data']['downloadPage'])
    else:
        print('ERROR:' + str(d)[:200])
except: print('PARSE_ERROR')
" 2>&1)
if echo "$GOFILE_PAGE" | grep -q "^https://"; then
    echo "  gofile: $GOFILE_PAGE (a download page — not round-trip checked)"
else
    echo "  ⚠ gofile.io upload failed: $GOFILE_PAGE"
fi

# catbox.moe (its limit is 200 MB)
echo ""
if [ "$SIZE" -lt 209715200 ]; then
    echo "── Uploading to catbox.moe ──"
    CATBOX_URL=$(curl -s --connect-timeout 20 --max-time "$UPLOAD_LIMIT" -F "reqtype=fileupload" -F "fileToUpload=@$APK" "https://catbox.moe/user/api.php" 2>&1 || true)
    if echo "$CATBOX_URL" | grep -q "^https://files.catbox.moe/"; then
        echo "  catbox: $CATBOX_URL"
        roundtrip "catbox.moe" "$CATBOX_URL" "$SHA" && HOSTS_SELECTED=$((HOSTS_SELECTED + 1))
    else
        echo "  ⚠ catbox.moe upload failed: ${CATBOX_URL:0:200}"
    fi
else
    echo "── catbox.moe skipped: ${SIZE_MB} MB is over its 200 MB limit ──"
fi

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
    # FABLE-063: detached, with its own log — a background child holding this script's output open
    # kept the ship waiting forever at "Delivery"
    setsid python3 /home/fictive/runewake/tools/serve_apk.py "$APK" > /tmp/serve_apk.log 2>&1 < /dev/null &
    sleep 2
    echo "  Server PID: $! (log: /tmp/serve_apk.log)"
fi

if [ "${#FAILED_LINKS[@]}" -gt 0 ]; then
    echo ""
    echo "  ⚠ Failed hosts:"
    for link in "${FAILED_LINKS[@]}"; do
        echo "    • $link"
    done
fi

exit 0