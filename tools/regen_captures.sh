#!/usr/bin/env bash
# tools/regen_captures.sh — regenerate every standard screen capture fresh.
# Shared by finish_task.sh (client/engine diff path) and apk_preflight.sh
# (right before an APK ships), so a shipped build is always checked against
# captures of ITSELF, never a stale screenshot from an earlier commit.
#
# 2026-09-18: that promise was not being kept. The loop ended in `|| true`, so
# a Godot crash, a timeout or a mode that silently wrote nothing left the
# PREVIOUS run's PNG sitting on disk, and the script still printed "done" and
# exited 0. Every downstream check then inspected, gated and committed a
# screenshot of older code. That is how the FABLE-007 title screen came to be
# signed off against a five-day-old capture.
#
# Three rules now, and they are the whole fix:
#   1. Delete the target BEFORE capturing. A failed capture must leave a hole,
#      never yesterday's picture.
#   2. Never swallow the exit code. A mode that fails is named, and the script
#      exits non-zero.
#   3. Stamp the results (tools/capture_stamp.py) so that later, anyone
#      holding one of these PNGs can prove which code rendered it.
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="${PROJECT_DIR}/artifacts/captures"
mkdir -p "$CAPTURE_DIR"

RUN_STARTED=$(date +%s)

# Every capture rewrites the viewport size in project.godot. If the script
# dies mid-loop, that edit must not be left behind for the next person to
# commit by accident.
restore_viewport() {
  sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "${PROJECT_DIR}/client/project.godot"
  sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "${PROJECT_DIR}/client/project.godot"
}
trap restore_viewport EXIT

MODES=(
  "map_test:2316:1080"
  "map_test_wide:2999:1080"
  "map_test_r2:2316:1080"
  "map_test_r2_wide:2999:1080"
  "duel_test:2316:1080"
  "duel_test_wide:2999:1080"
  "duel_test_safe:2316:1080"
  "duel_test_r2:2316:1080"
  # TASK-GATE-UNJAM-1: DebugCapture has supported --capture=duel_test_hand4
  # all along, but no runner ever called it, so its layout.json sat frozen
  # from 2026-09-18 — and label_fit judged that frozen copy on every task.
  # A 4.1px spill in it made finish_task.sh unable to pass for ANY work.
  # Running it means the check is against current code, where it either
  # clears or is a real defect worth blocking on.
  "duel_test_hand4:2316:1080"
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
  "title_test:2316:1080"
  "title_test_wide:2999:1080"
  "settings_test:2316:1080"
  "settings_test_wide:2999:1080"
)

png_dims() {
  python3 - "$1" <<'PY'
import struct, sys
try:
    with open(sys.argv[1], 'rb') as f:
        if f.read(8) != b'\x89PNG\r\n\x1a\n':
            raise ValueError('not a png')
        f.read(4); f.read(4)
        w, h = struct.unpack('>II', f.read(8))
    print(f"{w} {h}")
except Exception:
    print("0 0")
PY
}

FAILED=()
EXPECT=()

for mode_entry in "${MODES[@]}"; do
  mode_name="${mode_entry%%:*}"
  rest="${mode_entry#*:}"
  width="${rest%%:*}"
  height="${rest#*:}"
  target="${CAPTURE_DIR}/${mode_name}.png"
  EXPECT+=("${mode_name}")

  echo "  Capturing ${mode_name} (${width}x${height})"

  # Rule 1: no stale file may survive a failed capture.
  rm -f "$target"

  sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "${PROJECT_DIR}/client/project.godot"
  sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "${PROJECT_DIR}/client/project.godot"

  # Rule 2: record the exit code instead of discarding it.
  rc=0
  timeout 600 xvfb-run -a "${GODOT_BIN}" --path "${PROJECT_DIR}/client" -- "--capture=${mode_name}" 2>&1 || rc=$?

  if [[ ! -f "$target" ]]; then
    echo "    ✗ ${mode_name}: no PNG produced (godot exit ${rc})" >&2
    FAILED+=("${mode_name} (no file, exit ${rc})")
    continue
  fi

  read -r got_w got_h <<< "$(png_dims "$target")"
  if [[ "$got_w" != "$width" || "$got_h" != "$height" ]]; then
    echo "    ✗ ${mode_name}: expected ${width}x${height}, got ${got_w}x${got_h}" >&2
    FAILED+=("${mode_name} (wrong size ${got_w}x${got_h})")
    continue
  fi

  if [[ "$rc" -ne 0 ]]; then
    echo "    ⚠ ${mode_name}: PNG written but godot exited ${rc}" >&2
  fi
  echo "    → ${mode_name}.png ${got_w}x${got_h}"
done

restore_viewport

if [[ "${#FAILED[@]}" -gt 0 ]]; then
  echo "" >&2
  echo "  regen_captures.sh: ${#FAILED[@]} capture(s) FAILED:" >&2
  for f in "${FAILED[@]}"; do echo "    - ${f}" >&2; done
  echo "  Those screens have no current picture. Do not gate, commit or judge" >&2
  echo "  anything on the captures from this run." >&2
  exit 1
fi

# Rule 3: stamp what was produced, so the PNGs can prove their own provenance.
EXPECT_CSV=$(IFS=,; echo "${EXPECT[*]}")
python3 "${PROJECT_DIR}/tools/capture_stamp.py" --record \
        --run-started "${RUN_STARTED}" --expect "${EXPECT_CSV}"

echo "  regen_captures.sh done — ${#MODES[@]} captures, all fresh"
