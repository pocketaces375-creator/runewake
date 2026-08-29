#!/usr/bin/env bash
set -euo pipefail
# FRAME-GEN-1: generate ornate stone card-border candidates (Trikzos: stone direction locked).
# Run on the Hermes box:  bash tools/gen_card_frames.sh
# Sources ~/.hermes/.env for OPENROUTER_API_KEY, same as the pipeline.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT="$ROOT/pipeline/work/card_frames"
mkdir -p "$OUT"

if [ -f "$HOME/.hermes/.env" ]; then set -a; source "$HOME/.hermes/.env"; set +a; fi
: "${OPENROUTER_API_KEY:?OPENROUTER_API_KEY not set}"

COMMON="ornate decorative border frame for a fantasy trading card, portrait orientation, frame only around the outer edges, border band about one tenth of the image width on all four sides, the entire central area is one plain solid pure black empty rectangle with clean straight vertical and horizontal inner edges, nothing painted inside the black window, painterly oil painting texture, dark fantasy, muted palette, flat straight-on view, symmetrical, crisp detail, no text, no letters, no numbers, no watermark"

declare -a PROMPTS=(
"weathered grey-green carved stone card border engraved with interlocking celtic knotwork, moss growing in the crevices, faint worn gold inlay tracing the knot lines, small chiseled rune at each corner, ancient ruin masonry, $COMMON"
"ancient carved stone card border entwined with thin living roots and creeping vines, tiny leaves and small pale blossoms at the corners, the stone cracked where roots push through, warm ember-orange glow in one corner rune, $COMMON"
"stately carved stone card border with polished antique gold corner caps and a continuous gold filigree inlay band, museum reliquary feel, subtle chips and age wear in the stone, $COMMON"
"rough salt-crusted basalt monolith card border, megalithic chiseled slabs, glowing sea-teal rune glyphs carved sparsely along the band, faint barnacle and tide-line texture, $COMMON"
)

i=1
for p in "${PROMPTS[@]}"; do
  echo "=== frame_${i} ==="
  python3 "$ROOT/pipeline/gen_image_openrouter.py" "$p" "$OUT/frame_${i}.png" --width 832 --height 1216
  i=$((i+1))
done

echo "Compositing real card art into the frames..."
python3 "$ROOT/tools/compose_frame_options.py"
echo "Done — post $OUT/frame_options_sheet.png (and preview_*.png) to the Runewake group."