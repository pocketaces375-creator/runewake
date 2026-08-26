#!/usr/bin/env bash
set -euo pipefail
# TITLE-ART-GEN-1: generate wide title-screen key art candidates (Tidal Seal direction).
# Run on a machine with OpenRouter egress (foreman/Hermes box):
#   bash tools/gen_title_art.sh
# Sources ~/.hermes/.env for OPENROUTER_API_KEY, same as the pipeline.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT="$ROOT/pipeline/work/title_art"
mkdir -p "$OUT"

if [ -f "$HOME/.hermes/.env" ]; then set -a; source "$HOME/.hermes/.env"; set +a; fi
: "${OPENROUTER_API_KEY:?OPENROUTER_API_KEY not set}"

STYLE="painterly oil-painting style, hand-painted textured brushwork, dark moody palette with teal and muted gold accents, dramatic cinematic wide composition, epic fantasy game title key art, no text, no letters, no watermark"

declare -a PROMPTS=(
"a colossal ancient circular stone seal carved with interlocking runes and set with glowing sea-green gems, half-buried at the edge of a storm-lashed coastal cliff, towering waves crashing and spraying around it, drowned ruined towers in the misty distance, shafts of pale light breaking through storm clouds catching the seal, faint teal runic glow awakening across its carvings, $STYLE"
"low wide-angle view across a black-sand shore at dusk: an immense runic stone disc tilted and half-sunken in the shallows, waves breaking against it, its central gem casting a beam of sea-green light into the storm clouds, wreckage of ancient ships scattered along the beach, distant drowned city skyline, $STYLE"
"an enormous ancient rune-carved seal standing upright like a monolith gate in a flooded temple ruin, seawater cascading off its face, glowing teal glyphs spiraling from its center, mist and spray, broken marble columns framing the composition left and right, god rays from above, $STYLE"
"aerial three-quarter view of a giant cracked stone seal lying flat on a clifftop overlooking a raging sea, its rune rings glowing faintly teal, moss and salt crusted over old gold inlay, storm front rolling in with lightning on the horizon, tiny lone traveler standing on its rim for scale, $STYLE"
)

i=1
for p in "${PROMPTS[@]}"; do
  echo "=== title_${i} ==="
  python3 "$ROOT/pipeline/gen_image_openrouter.py" "$p" "$OUT/title_${i}.png" --width 1536 --height 864
  i=$((i+1))
done
echo "Done — candidates in $OUT"
