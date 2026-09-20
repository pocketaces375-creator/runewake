#!/usr/bin/env bash
# tools/button_reach_check.sh — FABLE-015
#
# Can the end-of-duel buttons actually be pressed?
#
# Three separate bugs have shipped in which they could not: something opaque on
# top of them (FABLE-005), the button freed between touch-down and touch-up so
# BaseButton never paired the press (FABLE-012), and an exception inside the
# handler that Godot silently swallowed (FABLE-011). Each one cost a round, and
# the last one cost four, because the automated checks never touched a button:
# soak mode calls ChangeSceneToFile itself on a timer, so "loop smoke passed"
# has never meant "Continue works". It means "the scene AFTER Continue loads".
#
# DuelScene.AuditEndOfDuelButtons now prints, on the frame after the game-over
# overlay lays out, whether each button is visible, enabled, sized, filtering
# input, and free of anything drawn over its centre. This runs the two overlay
# capture modes and reads those lines.
#
# It is deliberately ADVISORY in ship_apk.sh on its first outing. It is new, it
# has never run, and a brand-new check that has never been green has no business
# deciding whether Trikzos gets a build. Promote it to blocking once it has
# passed once — that is the whole of TASK-BTN-REACH-1.
#
# Exit 0 = both overlays reported every button REACHABLE.
# Exit 1 = a button was blocked, unreachable, or never audited at all.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
MODES=(victory_overlay defeat_overlay)

# These capture modes rewrite their PNG and sidecars. If those files were clean
# before we started, put them back afterwards — this check is not allowed to
# invalidate the capture provenance stamp as a side effect.
PATHS=()
for m in "${MODES[@]}"; do
  PATHS+=("artifacts/captures/${m}.png" "artifacts/captures/${m}.layout.json" "artifacts/captures/${m}.meta.json")
done
WAS_CLEAN=$(cd "$ROOT" && git status --porcelain -- "${PATHS[@]}" 2>/dev/null | wc -l)

FAIL=0
for mode in "${MODES[@]}"; do
  echo "── ${mode} ──"
  OUT=$(cd "$ROOT" && timeout 300 xvfb-run -a "$GODOT_BIN" --path client -- "--capture=${mode}" 2>&1)

  AUDIT=$(echo "$OUT" | grep -F '[BTN-AUDIT]')
  if [ -z "$AUDIT" ]; then
    echo "  ✗ ${mode}: the overlay never audited its buttons."
    echo "    Either the overlay was not built, or AuditEndOfDuelButtons did not run."
    echo "    Last lines of the run:"
    echo "$OUT" | tail -5 | sed 's/^/      /'
    FAIL=1
    continue
  fi

  echo "$AUDIT" | sed 's/^/    /'

  if echo "$AUDIT" | grep -qE 'BLOCKED BY|NOT REACHABLE'; then
    echo "  ✗ ${mode}: a button cannot be pressed. That is the bug, named, before it ships."
    FAIL=1
  else
    N=$(echo "$AUDIT" | grep -c 'REACHABLE')
    # Exactly two buttons on this overlay. Fewer means one of them was not
    # audited, which is not the same as "it is fine".
    if [ "$N" -lt 2 ]; then
      echo "  ✗ ${mode}: only ${N} button(s) audited, expected 2."
      FAIL=1
    else
      echo "  ✓ ${mode}: ${N} buttons reachable"
    fi
  fi
done

if [ "$WAS_CLEAN" -eq 0 ]; then
  (cd "$ROOT" && git checkout -- "${PATHS[@]}" 2>/dev/null) || true
fi

exit "$FAIL"
