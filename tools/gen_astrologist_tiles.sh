#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ -f "$HOME/.hermes/.env" ]; then set -a; source "$HOME/.hermes/.env"; set +a; fi
: "${OPENROUTER_API_KEY:?OPENROUTER_API_KEY not set}"

ART_DIR="$ROOT/client/content/art/artifacts"
mkdir -p "$ART_DIR"

# Style spine + isolation clause + camera for artifact tiles (per ART_PROMPT_PLAYBOOK.md)
STYLE="Dark fantasy oil painting, heavy impasto brushwork, dramatic rim light, medieval woodcut influence, painterly edges"
ISO="The object alone at the centre of the frame, resting on a bare slab of dark stone, empty unlit background falling to black"
CAM="Shot on an 85mm lens at f/4, centred composition, eye-level, museum lighting from the upper left"

# Astrologist palette: deep night blues and silver with celestial accents
PALETTE="Abyssal blue #0E2436 with teal #1F6F72 and pale foam #C9DCD8"

declare -A PROMPTS
PROMPTS["artf_astrologist_lunar_lens"]='An eye-shaped amber lens the size of an open hand, hairline fractures catching stray moonlight, a crescent moon reflection in the polished curve, the iris a swirling silver galaxy, '"$ISO"', '"$STYLE"'. '"$PALETTE"'. '"$CAM"

PROMPTS["artf_astrologist_eclipse_sphere"]='A perfectly smooth obsidian sphere rimmed in tarnished silver, a ring of pale light around its equator, consuming all light that touches it, dark star-speckled void within, '"$ISO"', '"$STYLE"'. '"$PALETTE"'. '"$CAM"

PROMPTS["artf_astrologist_meteor_shower"]='A scattering of jagged celestial shards frozen mid-fall, each trailing a thin thread of silver light, rough-edged star-metal fragments glowing with dying heat, '"$ISO"', '"$STYLE"'. '"$PALETTE"'. '"$CAM"

PROMPTS["artf_astrologist_twin_stars"]='Two cold points of pale blue-white light connected by a hair-thin silver thread, suspended in perfect symmetry, a faint constellation pattern pulsing between them, '"$ISO"', '"$STYLE"'. '"$PALETTE"'. '"$CAM"

for id in "${!PROMPTS[@]}"; do
  prompt="${PROMPTS[$id]}"
  out="$ART_DIR/$id.webp"
  echo "=== $id ==="
  python3 "$ROOT/pipeline/gen_image_openrouter.py" "$prompt" "$out" --width 832 --height 832
  echo ""
done

echo "=== ALL DONE ==="