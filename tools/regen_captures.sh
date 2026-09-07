#!/usr/bin/env bash
# tools/regen_captures.sh — regenerate every standard screen capture fresh.
# Shared by finish_task.sh (client/engine diff path) and apk_preflight.sh
# (right before an APK ships), so a shipped build is always checked against
# captures of ITSELF, never a stale screenshot from an earlier commit.
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="${PROJECT_DIR}/artifacts/captures"
mkdir -p "$CAPTURE_DIR"

MODES=(
  "map_test:2316:1080"
  "map_test_wide:2999:1080"
  "map_test_r2:2316:1080"
  "map_test_r2_wide:2999:1080"
  "duel_test:2316:1080"
  "duel_test_wide:2999:1080"
  "duel_test_safe:2316:1080"
  "duel_test_r2:2316:1080"
  "choose_path:2316:1080"
  "choose_path_wide:2999:1080"
  "victory_overlay:2316:1080"
  "victory_overlay_wide:2999:1080"
  "defeat_overlay:2316:1080"
  "defeat_overlay_wide:2999:1080"
  "reliquary_test:2316:1080"
  "reliquary_test_wide:2999:1080"
  "reliquary_test_all:2316:1080"
  "reliquary_test_all_wide:2999:1080"
  "slots_test:2316:1080"
)

for mode_entry in "${MODES[@]}"; do
  mode_name="${mode_entry%%:*}"
  rest="${mode_entry#*:}"
  width="${rest%%:*}"
  height="${rest#*:}"
  echo "  Capturing ${mode_name} (${width}x${height})"
  sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "${PROJECT_DIR}/client/project.godot"
  sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "${PROJECT_DIR}/client/project.godot"
  timeout 600 xvfb-run -a "${GODOT_BIN}" --path "${PROJECT_DIR}/client" -- "--capture=${mode_name}" 2>&1 || true
done

sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "${PROJECT_DIR}/client/project.godot"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "${PROJECT_DIR}/client/project.godot"
echo "  regen_captures.sh done"
