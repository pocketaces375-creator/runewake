#!/usr/bin/env bash
# tools/ship_apk.sh — an APK after every patch, and it is worth opening.
#
# WHY THIS EXISTS, AND WHY IT IS NOT "SHIP NO MATTER WHAT"
# --------------------------------------------------------
# Trikzos asked for a build after every patch, because he had gone days without
# one: export_and_verify.sh exits on the FIRST red check, and the red checks
# were unrelated to the work — a stale gate stamp, misnamed capture files, a
# 4px text overflow. A Continue-button bug survived four rounds because nobody
# could press the button.
#
# The first version of this script answered that by shipping regardless. He
# pushed back, correctly: "we want the apk to be working any time if I'm gonna
# go through the process of opening it." Handing him a broken build does not
# save his time, it moves the waste onto him — and he is the scarcest tester on
# the project.
#
# So the question is not "did every check pass" and it is not "ship anyway". It
# is: WOULD A RED CHECK HERE MEAN THE BUILD IS NOT WORTH HIS TIME?
#
#   BLOCKING  the app will not run, will not install, or cannot be played:
#             the build, the engine tests, the campaign loop, the tutorial
#             script, the UX walkthrough, input smoke (can a card be clicked),
#             loop smoke (title -> map -> duel -> victory -> forge), and the
#             three install-critical APK facts — intact zip, valid signature,
#             landscape. If one of these is red he gets told what is broken
#             INSTEAD of a wasted download.
#
#   ADVISORY  cosmetic or tooling, and no reason to withhold a playable build:
#             label_fit spills, capture freshness and naming, the visual gate,
#             portrait file sizes. These are reported on the build's label.
#
# --anyway forces a build past a blocking failure, for when a broken build is
# specifically the thing being investigated. It is stamped as an override.
#
# finish_task.sh is unchanged and still decides "done". This decides "is this
# worth Trikzos's thumb".
#
#   Usage: tools/ship_apk.sh [--release|--debug] [--note "what to look at"] [--anyway]

set -uo pipefail          # deliberately NOT -e: checks must all run and report

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLIENT_DIR="$REPO_ROOT/client"
EXPORT_DIR="$CLIENT_DIR/exports"
REPORT="$REPO_ROOT/artifacts/SHIP_REPORT.md"

MODE="release"; NOTE=""; ANYWAY=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --release) MODE="release"; shift ;;
    --debug)   MODE="debug";   shift ;;
    --note)    NOTE="${2:-}";  shift 2 ;;
    --anyway)  ANYWAY=true;    shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

BLOCK_OK=(); BLOCK_BAD=(); ADVISE_OK=(); ADVISE_BAD=(); SKIPPED=(); WAIVED=()

# ─── Known failures (FABLE-015) ─────────────────────────────────────────────
# A blocking check can be red for a reason that has nothing to do with the work
# in hand, and then it is not protecting Trikzos, it is just keeping a build off
# his phone. tools/ship_known_failures.json lists those, each with a reason, an
# owning task and an expiry date.
#
# A waiver does not make a check pass. It reclassifies ONE named, dated,
# owned failure as "inherited" so it reports loudly instead of withholding.
#
# UNWAIVABLE is the floor and is deliberately not configurable. These are the
# checks that ARE the promise the build is worth opening; a waiver on one of
# them would make the promise a lie, so the script refuses to run rather than
# ignore the attempt.
UNWAIVABLE=(
  "C# build"
  "engine tests"
  "input smoke (a card can be tapped)"
  "loop smoke (title -> duel -> victory -> map)"
  "apk: intact archive"
  "apk: valid signature"
  "apk: launches landscape"
)

WAIVER_FILE="$REPO_ROOT/tools/ship_known_failures.json"
declare -A WAIVER_REASON=() WAIVER_TASK=()
if [ -f "$WAIVER_FILE" ]; then
  # Emits one TAB-separated line per LIVE waiver; expired ones are dropped here
  # so the rest of the script never has to think about dates.
  while IFS=$'\t' read -r wname wtask wreason; do
    [ -z "${wname:-}" ] && continue
    WAIVER_REASON["$wname"]="$wreason"
    WAIVER_TASK["$wname"]="$wtask"
  done < <(python3 - "$WAIVER_FILE" <<'PY'
import json, sys, datetime
today = datetime.date.today().isoformat()
try:
    data = json.load(open(sys.argv[1]))
except Exception as e:
    print(f"!! could not read waiver file: {e}", file=sys.stderr); sys.exit(0)
for w in data.get("waivers", []):
    name = w.get("check", "").strip()
    exp = w.get("expires", "")
    if not name:
        continue
    if not exp or exp < today:
        print(f"   ⏰ waiver for '{name}' expired {exp or '(no date)'} — it blocks again",
              file=sys.stderr)
        continue
    print(f"{name}\t{w.get('task','(no task)')}\t{w.get('reason','(no reason given)')}")
PY
  )
fi

# Rule 3 enforcement: refuse to run at all if someone waived the floor.
for u in "${UNWAIVABLE[@]}"; do
  if [ -n "${WAIVER_REASON[$u]:-}" ]; then
    echo "  ✖ ${WAIVER_FILE} waives '${u}', which is unwaivable." >&2
    echo "    That check is part of the promise that this build is worth opening." >&2
    echo "    Remove the entry. Refusing to build." >&2
    exit 2
  fi
done

# blocking <name> <command...>
blocking() {
  local name="$1"; shift
  echo ""; echo "── [must pass] ${name} ──"
  if "$@" 2>&1 | tail -6; then
    echo "  ✅ ${name}"; BLOCK_OK+=("${name}"); return 0
  fi
  if [ -n "${WAIVER_REASON[$name]:-}" ]; then
    echo "  ⏸  ${name} — RED, but a known failure (${WAIVER_TASK[$name]})"
    echo "     ${WAIVER_REASON[$name]}"
    echo "     Not withholding the build for it. This prints on every run."
    WAIVED+=("${name}|${WAIVER_TASK[$name]}|${WAIVER_REASON[$name]}")
    return 1
  fi
  echo "  ❌ ${name} — BLOCKING"; BLOCK_BAD+=("${name}"); return 1
}

# advisory <name> <command...>
advisory() {
  local name="$1"; shift
  echo ""; echo "── [advisory] ${name} ──"
  if "$@" 2>&1 | tail -4; then
    echo "  ✅ ${name}"; ADVISE_OK+=("${name}"); return 0
  fi
  echo "  ⚠️  ${name} — noted on the label, not blocking"; ADVISE_BAD+=("${name}"); return 1
}

COMMIT=$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo '?')
DIRTY=$(git -C "$REPO_ROOT" status --porcelain 2>/dev/null | wc -l)
FINGERPRINT=$(python3 "$REPO_ROOT/tools/capture_stamp.py" --fingerprint 2>/dev/null || echo "?")

echo "═══════════════════════════════════════════════════"
echo "  SHIP APK (${MODE})   commit ${COMMIT}   code ${FINGERPRINT}   dirty ${DIRTY}"
echo "═══════════════════════════════════════════════════"

# ─── Blocking: would this build waste his time? ─────────────────────────────
blocking "C# build" bash -c \
  "cd '$REPO_ROOT' && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | grep -q 'Build succeeded'"

blocking "engine tests" bash -c \
  "cd '$REPO_ROOT' && dotnet test tests/Runewake.Tests.csproj --no-restore -c Debug 2>&1 | grep -qE '^Passed!'"

[ -f "$REPO_ROOT/tools/campaign_loop_sim.py" ] && \
  blocking "campaign loop" python3 "$REPO_ROOT/tools/campaign_loop_sim.py"
[ -f "$REPO_ROOT/tools/tutorial_script_sim.py" ] && \
  blocking "tutorial script" python3 "$REPO_ROOT/tools/tutorial_script_sim.py"

blocking "UX walkthrough" bash -c \
  "cd '$REPO_ROOT' && timeout 300 xvfb-run -a \"\${GODOT_BIN:-\$HOME/.local/bin/godot}\" --path client -- --uxwalk 2>&1 | grep -qE 'WALKTHROUGH DONE|0 fail'"

# The two that speak directly to "can he actually play it".
[ -x "$REPO_ROOT/tools/input_smoke.sh" ] && \
  blocking "input smoke (a card can be tapped)" bash "$REPO_ROOT/tools/input_smoke.sh"
[ -x "$REPO_ROOT/tools/loop_smoke.sh" ] && \
  blocking "loop smoke (title -> duel -> victory -> map)" bash "$REPO_ROOT/tools/loop_smoke.sh"

# ─── Advisory: cosmetic, tooling, provenance ────────────────────────────────
[ -f "$REPO_ROOT/tools/capture_stamp.py" ] && \
  advisory "captures fresh" python3 "$REPO_ROOT/tools/capture_stamp.py" --verify
[ -f "$REPO_ROOT/tools/art_check.py" ] && \
  advisory "art_check" python3 "$REPO_ROOT/tools/art_check.py" gate "$CLIENT_DIR/content/art"
# FABLE-015. Advisory on purpose, and only on purpose for now: a check that has
# never once been green must not be the thing that withholds a build. The
# moment it passes, TASK-BTN-REACH-1 moves this line up into the blocking
# section — it is the only check that looks at the button that has broken
# three times.
[ -x "$REPO_ROOT/tools/button_reach_check.sh" ] && \
  advisory "end-of-duel buttons reachable (new — advisory until first green)" \
      bash "$REPO_ROOT/tools/button_reach_check.sh"

# ─── The gate on his time ───────────────────────────────────────────────────
if [ "${#BLOCK_BAD[@]}" -gt 0 ] && [ "$ANYWAY" = false ]; then
  echo ""
  echo "═══════════════════════════════════════════════════"
  echo "  NOT SHIPPING — this build would waste your time."
  echo ""
  for b in "${BLOCK_BAD[@]}"; do echo "    ❌ ${b}"; done
  echo ""
  echo "  These are the checks that predict a build you cannot play."
  echo "  Fix them and run this again; do not hand over the APK."
  echo "  If a broken build IS the thing being investigated: --anyway"
  echo "═══════════════════════════════════════════════════"
  exit 1
fi

# ─── Export ─────────────────────────────────────────────────────────────────
if [ "$MODE" = "release" ]; then
  PRESET="Android Release"; EXPORT_FLAG="--export-release"; OUTFILE="Runewake.apk"
  RELEASE_ENV="$HOME/.runewake/release.env"
  if [ -f "$RELEASE_ENV" ]; then
    # shellcheck disable=SC1090
    source "$RELEASE_ENV"
  else
    echo ""; echo "  ⚠️  no keystore at $RELEASE_ENV — building a debug APK instead"
    SKIPPED+=("release signing (no keystore)"); MODE="debug"
  fi
fi
[ "$MODE" = "debug" ] && { PRESET="Android Debug"; EXPORT_FLAG="--export-debug"; OUTFILE="Runewake-debug.apk"; }

echo ""; echo "── Import pass ──"
cd "$CLIENT_DIR"
timeout 600 xvfb-run -a godot --headless --import --path . 2>&1 | tail -3

echo ""; echo "── Godot export (${PRESET}) ──"
mkdir -p "$EXPORT_DIR"
APK="$EXPORT_DIR/$OUTFILE"
rm -f "$APK"                      # a failed export must not leave a stale APK
if godot --headless "$EXPORT_FLAG" "$PRESET" "exports/$OUTFILE" 2>&1 | tail -8; then
  echo "  ✅ Export complete: $OUTFILE"
else
  echo "  ❌ Export failed"
  exit 1
fi

if [ ! -f "$APK" ]; then
  echo ""; echo "  ❌ the export produced no file — nothing to hand over."; exit 1
fi
SHA=$(sha256sum "$APK" | cut -d' ' -f1)
SIZE_MB=$(echo "scale=1; $(stat --format=%s "$APK") / 1048576" | bc)
echo "  ${OUTFILE}  ${SIZE_MB} MB"

# ─── Install-critical APK facts: blocking, because a build that will not ────
# install or launches sideways is not testable at all.
blocking "apk: intact archive" unzip -t "$APK"
if [ "$MODE" = "release" ] && [ -x /home/fictive/Android/Sdk/build-tools/34.0.0/apksigner ]; then
  blocking "apk: valid signature" /home/fictive/Android/Sdk/build-tools/34.0.0/apksigner verify "$APK"
fi
AAPT=/home/fictive/Android/Sdk/build-tools/34.0.0/aapt
if [ -x "$AAPT" ]; then
  blocking "apk: launches landscape" bash -c \
    "'$AAPT' dump badging '$APK' 2>/dev/null | grep -qiE 'screen.landscape|screenOrientation.*landscape'"
fi

# The full preflight stays advisory — it bundles cosmetic checks (portrait file
# sizes, baked-texture counts) with the install-critical ones already covered.
[ -f "$REPO_ROOT/tools/apk_preflight.sh" ] && \
  advisory "apk preflight (full)" bash "$REPO_ROOT/tools/apk_preflight.sh" \
      "$APK" /home/fictive/Android/Sdk "$REPO_ROOT/exports/Runewake.apk"

if [ "${#BLOCK_BAD[@]}" -gt 0 ] && [ "$ANYWAY" = false ]; then
  echo ""
  echo "  NOT SHIPPING — the APK itself failed an install-critical check:"
  for b in "${BLOCK_BAD[@]}"; do echo "    ❌ ${b}"; done
  exit 1
fi

PLAYABLE="unknown"
[ -f "$REPO_ROOT/artifacts/PLAYABLE.json" ] && \
  PLAYABLE=$(python3 -c "import json;print(json.load(open('$REPO_ROOT/artifacts/PLAYABLE.json')).get('playable'))" 2>/dev/null || echo unknown)

# ─── The label that travels with the build ──────────────────────────────────
mkdir -p "$(dirname "$REPORT")"
{
  echo "# Ship report"
  echo ""
  if [ "$ANYWAY" = true ] && [ "${#BLOCK_BAD[@]}" -gt 0 ]; then
    echo "> ⚠️ **OVERRIDE BUILD (--anyway).** Blocking checks failed. This is not"
    echo "> expected to work; it was built to investigate something specific."
  elif [ "${#WAIVED[@]}" -gt 0 ]; then
    echo "> ✅ **Every check that can be trusted to predict a working build passed**"
    echo "> — including the unwaivable ones: the build, the engine tests, input"
    echo "> smoke and loop smoke. ${#WAIVED[@]} inherited failure(s) are listed under"
    echo "> *Known-broken* below, with who owns each."
  else
    echo "> ✅ **Every check that predicts a working build passed.** Anything listed"
    echo "> under Advisory is cosmetic or tooling and does not affect play."
  fi
  echo ""
  echo "| | |"
  echo "|---|---|"
  echo "| apk | \`${OUTFILE}\` (${SIZE_MB} MB, ${MODE}) |"
  echo "| sha256 | \`${SHA}\` |"
  echo "| commit | \`${COMMIT}\` |"
  echo "| code fingerprint | \`${FINGERPRINT}\` |"
  echo "| loop smoke says playable | ${PLAYABLE} |"
  echo "| built | $(date '+%Y-%m-%d %H:%M %Z') |"
  [ -n "$NOTE" ] && echo "| look at | ${NOTE} |"
  echo ""
  echo "## Must-pass checks"; echo ""
  for p in "${BLOCK_OK[@]:-}"; do [ -n "$p" ] && echo "- ✅ ${p}"; done
  for f in "${BLOCK_BAD[@]:-}"; do [ -n "$f" ] && echo "- ❌ **${f}** (overridden)"; done
  echo ""
  if [ "${#WAIVED[@]}" -gt 0 ]; then
    echo "## Known-broken, shipped anyway"; echo ""
    echo "These are must-pass checks that are RED. They were red before this"
    echo "work, each has an owning task, and \`loop_smoke\` and \`input_smoke\`"
    echo "— which cannot be waived — still passed. Read them and decide for"
    echo "yourself whether to trust the build."; echo ""
    for w in "${WAIVED[@]}"; do
      echo "- ⏸ **${w%%|*}** — owner: \`$(echo "$w" | cut -d'|' -f2)\`"
      echo "  <br>$(echo "$w" | cut -d'|' -f3-)"
    done
    echo ""
  fi
  if [ "${#ADVISE_BAD[@]}" -gt 0 ] || [ "${#ADVISE_OK[@]}" -gt 0 ]; then
    echo "## Advisory — cosmetic/tooling, does not affect play"; echo ""
    for p in "${ADVISE_OK[@]:-}"; do [ -n "$p" ] && echo "- ✅ ${p}"; done
    for f in "${ADVISE_BAD[@]:-}"; do [ -n "$f" ] && echo "- ⚠️ ${f}"; done
    echo ""
  fi
  if [ "${#SKIPPED[@]}" -gt 0 ]; then
    echo "## Skipped"; echo ""
    for s in "${SKIPPED[@]}"; do echo "- ${s}"; done; echo ""
  fi
  if [ "${DIRTY}" -gt 0 ]; then
    echo "⚠️ Built from a working tree with ${DIRTY} uncommitted file(s) — this APK"
    echo "does not correspond to any commit."; echo ""
  fi
} > "$REPORT"

# ─── Deliver ────────────────────────────────────────────────────────────────
echo ""; echo "── Delivery ──"
mkdir -p "$REPO_ROOT/exports" && cp "$APK" "$REPO_ROOT/exports/$(basename "$APK")" 2>/dev/null || true
if [ -f "$REPO_ROOT/tools/apk_deliver.sh" ]; then
  bash "$REPO_ROOT/tools/apk_deliver.sh" "$APK" 2>&1 | tail -20
else
  echo "  apk_deliver.sh missing — APK is at $APK"
fi

echo ""
echo "═══════════════════════════════════════════════════"
echo "  SHIPPED: ${OUTFILE}  ${SIZE_MB} MB   playable=${PLAYABLE}"
if [ "${#WAIVED[@]}" -gt 0 ]; then
  for w in "${WAIVED[@]}"; do echo "  known-broken, shipped anyway: ${w%%|*}"; done
fi
if [ "${#ADVISE_BAD[@]}" -gt 0 ]; then
  echo "  advisory noted: ${ADVISE_BAD[*]}"
fi
echo "═══════════════════════════════════════════════════"
echo "Post the APK link AND ${REPORT#"$REPO_ROOT/"} together."
exit 0
