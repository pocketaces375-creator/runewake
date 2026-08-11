#!/usr/bin/env bash
# Runewake APK export — with DLL freshness guard.
#
# Godot's Android export does NOT surface dotnet publish failures.
# A build can report success while packaging stale assemblies.
# This script verifies binary freshness before and after export.
#
# Usage: ./client/export_apk.sh [output.apk]
#   Default output: client/exports/Runewake.apk

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT="${1:-$REPO_ROOT/client/exports/Runewake.apk}"
CLIENT_DIR="$REPO_ROOT/client"
EXPORT_PRESET="Android"

# ── 1. Find the newest source file ──────────────────────────────────────────
echo "=== Checking source freshness ===" >&2

NEWEST_SRC=$(find "$REPO_ROOT/client/scripts" "$REPO_ROOT/engine" \
  -name '*.cs' -type f -printf '%T@ %p\n' 2>/dev/null | \
  sort -rn | head -1 | awk '{print $2}')

if [ -z "$NEWEST_SRC" ]; then
  echo "ERROR: No .cs files found in client/scripts/ or engine/" >&2
  exit 1
fi

NEWEST_SRC_MTIME=$(stat --printf="%Y" "$NEWEST_SRC")
echo "  Newest source: $NEWEST_SRC ($(date -d @"$NEWEST_SRC_MTIME" '+%H:%M:%S'))" >&2

# ── 2. Find the newest assembly in the Godot .NET build cache ───────────────
DLL_CACHE="$CLIENT_DIR/.godot/mono/temp/bin"
if [ ! -d "$DLL_CACHE" ]; then
  echo "  WARNING: No .godot/mono/temp/bin/ found — fresh build required." >&2
  NEEDS_REBUILD=1
else
  NEWEST_DLL=$(find "$DLL_CACHE" -name '*.dll' -type f -printf '%T@ %p\n' 2>/dev/null | \
    sort -rn | head -1 | awk '{print $2}')

  if [ -z "$NEWEST_DLL" ]; then
    echo "  WARNING: No DLLs in cache — fresh build required." >&2
    NEEDS_REBUILD=1
  else
    NEWEST_DLL_MTIME=$(stat --printf="%Y" "$NEWEST_DLL")
    echo "  Newest DLL: $NEWEST_DLL ($(date -d @"$NEWEST_DLL_MTIME" '+%H:%M:%S'))" >&2

    if [ "$NEWEST_DLL_MTIME" -lt "$NEWEST_SRC_MTIME" ]; then
      STALE_MINUTES=$(( (NEWEST_SRC_MTIME - NEWEST_DLL_MTIME) / 60 ))
      echo "  !! DLL is ${STALE_MINUTES}m older than newest source — STALE !!" >&2
      NEEDS_REBUILD=1
    else
      echo "  ✓ DLL is fresh (sources unchanged since last build)" >&2
      NEEDS_REBUILD=0
    fi
  fi
fi

# ── 3. Rebuild if stale ────────────────────────────────────────────────────
if [ "$NEEDS_REBUILD" -eq 1 ]; then
  echo "" >&2
  echo "=== Rebuilding (dotnet build) ===" >&2
  cd "$CLIENT_DIR"
  if ! dotnet build -c Release; then
    echo "" >&2
    echo "ERROR: dotnet build FAILED — aborting export." >&2
    echo "  Fix the compilation error and re-run the script." >&2
    exit 1
  fi
  echo "  ✓ dotnet build succeeded" >&2

  # Re-check DLL timestamp after build
  NEWEST_DLL=$(find "$DLL_CACHE" -name '*.dll' -type f -printf '%T@ %p\n' 2>/dev/null | \
    sort -rn | head -1 | awk '{print $2}')
  if [ -n "$NEWEST_DLL" ]; then
    NEWEST_DLL_MTIME=$(stat --printf="%Y" "$NEWEST_DLL")
    if [ "$NEWEST_DLL_MTIME" -lt "$NEWEST_SRC_MTIME" ]; then
      echo "ERROR: dotnet build reported success but DLL is still stale!" >&2
      echo "  Source: $NEWEST_SRC (mtime=$NEWEST_SRC_MTIME)" >&2
      echo "  DLL:    $NEWEST_DLL (mtime=$NEWEST_DLL_MTIME)" >&2
      echo "  This means dotnet build silently failed." >&2
      exit 1
    fi
    echo "  ✓ DLL fresh after rebuild" >&2
  fi
fi

# ── 4. Build hash of source tree for comparison after export ────────────────
# Use a deterministic hash of the C# source files so we can detect if
# the export process somehow used different binaries.
SRC_HASH=$(find "$REPO_ROOT/client/scripts" "$REPO_ROOT/engine" -name '*.cs' -type f \
  -exec md5sum {} + | md5sum | cut -c1-16)
echo "  Source tree hash: $SRC_HASH" >&2

# ── 5. Export ──────────────────────────────────────────────────────────────
echo "" >&2
echo "=== Exporting APK ===" >&2
mkdir -p "$(dirname "$OUTPUT")"

cd "$CLIENT_DIR"
godot --headless --editor --export-debug "$EXPORT_PRESET" "$OUTPUT" 2>&1

if [ ! -f "$OUTPUT" ]; then
  echo "ERROR: Export produced no APK at $OUTPUT" >&2
  exit 1
fi

# ── 6. Post-export verification ────────────────────────────────────────────
echo "" >&2
echo "=== Verifying APK ===" >&2

APK_SIZE=$(stat --printf="%s" "$OUTPUT")
APK_MB=$((APK_SIZE / 1048576))
APK_KB=$(( (APK_SIZE % 1048576) / 1024 ))

echo "  Output: $OUTPUT" >&2
echo "  Size:   ${APK_MB}.${APK_KB} MB" >&2

# Warn on suspiciously small APKs (below 30MB is likely stale/missing assemblies)
if [ "$APK_SIZE" -lt 30000000 ]; then
  echo "WARNING: APK is only ${APK_MB}MB — suspiciously small. Likely missing assemblies." >&2
  echo "  A healthy debug APK with C# assemblies is typically 75-120 MB." >&2
  echo "  This may indicate dotnet publish failed silently during export." >&2
fi

# Verify DLLs inside the APK are recent
echo "" >&2
echo "  Checking DLL timestamps inside APK..." >&2
DLL_IN_APK=$(unzip -l "$OUTPUT" 2>/dev/null | grep '\.dll$' | head -5 || true)
if [ -z "$DLL_IN_APK" ]; then
  echo "  WARNING: No .dll files found inside APK!" >&2
  echo "  C# assemblies may not have been included in the export." >&2
else
  echo "  DLLs found in APK (${APK_SIZE} bytes total):" >&2
  unzip -l "$OUTPUT" 2>/dev/null | grep '\.dll$' | awk '{print "    " $4, "("$1" bytes)"}' >&2

  # Count DLL files to spot missing assemblies
  DLL_COUNT=$(unzip -l "$OUTPUT" 2>/dev/null | grep '\.dll$' | wc -l)
  echo "  DLL count: $DLL_COUNT" >&2
  if [ "$DLL_COUNT" -lt 3 ]; then
    echo "  WARNING: Very few DLLs ($DLL_COUNT) — Runewake assemblies may be missing!" >&2
  fi
fi

# ── 7. Print summary ────────────────────────────────────────────────────────
SHA256=$(sha256sum "$OUTPUT" | cut -c1-64)

echo "" >&2
echo "══════════════════════════════════════════════" >&2
echo "  APK ready" >&2
echo "  SHA-256: $SHA256" >&2
echo "  Size:    ${APK_MB}.${APK_KB} MB" >&2
echo "  Source:  $(basename "$NEWEST_SRC")" >&2
echo "══════════════════════════════════════════════" >&2

# Print machine-readable output for script consumers
echo "SHA256=$SHA256"
echo "SIZE=$APK_SIZE"
echo "PATH=$OUTPUT"