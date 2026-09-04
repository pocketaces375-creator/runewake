#!/bin/bash
# Batch generate all 36 tile candidates (6 per item, 6 items)
set -e
source ~/.hermes/.env
export OPENROUTER_API_KEY
cd /home/fictive/runewake

GEN_SCRIPT="python3 pipeline/gen_image_openrouter.py"
OUTDIR="/home/fictive/runewake-lane2/art_output/tiles"
mkdir -p "$OUTDIR"

echo "=== Generating 6 candidates for each of 6 item tiles ==="
echo "=== artf_necromancer_ritual_piece ==="
for i in 1 2 3 4 5 6; do
  $GEN_SCRIPT \
    "oil painting of a fantasy game item icon, a close-up painted icon of a ritual fetish — a small bone-and-ivory talisman wrapped in dark silk cord, with a single black gem, bone-white and murky violet palette, simple iconic centered composition, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, canvas texture showing through, painterly edges rather than crisp digital edges, no text, no words, no letters, unsigned" \
    "$OUTDIR/artf_necromancer_ritual_piece_candidate_${i}.png"
done

echo "=== artf_paladin_banner ==="
for i in 1 2 3 4 5 6; do
  $GEN_SCRIPT \
    "oil painting of a fantasy game item icon, a close-up painted icon of a battle-worn ceremonial banner — a tattered golden standard on a wooden pole, warm cream and pale gold palette with soft amber light, simple iconic centered composition, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, canvas texture showing through, painterly edges, no text, no words, no letters, unsigned" \
    "$OUTDIR/artf_paladin_banner_candidate_${i}.png"
done

echo "=== artf_druid_book_of_familiar ==="
for i in 1 2 3 4 5 6; do
  $GEN_SCRIPT \
    "oil painting of a fantasy game item icon, a close-up painted icon of an ancient leather-bound tome with a green leaf clasp, deep forest green and earthy moss brown palette with golden highlights, simple iconic centered composition, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, canvas texture showing through, painterly edges, no text, no words, no letters, unsigned" \
    "$OUTDIR/artf_druid_book_of_familiar_candidate_${i}.png"
done

echo "=== artf_druid_elemental_bond ==="
for i in 1 2 3 4 5 6; do
  $GEN_SCRIPT \
    "oil painting of a fantasy game item icon, a close-up painted icon of a rune-etched green elemental stone, deep forest green and earthy moss brown with golden highlights, simple iconic centered composition, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, canvas texture showing through, painterly edges, no text, no words, no letters, unsigned" \
    "$OUTDIR/artf_druid_elemental_bond_candidate_${i}.png"
done

echo "=== artf_astrologist_orb ==="
for i in 1 2 3 4 5 6; do
  $GEN_SCRIPT \
    "oil painting of a fantasy game item icon, a close-up painted icon of a crystal orb filled with tiny pinprick stars and constellations, deep blue-teal with scattered sea-green starlight, simple iconic centered composition, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, canvas texture showing through, painterly edges, no text, no words, no letters, unsigned" \
    "$OUTDIR/artf_astrologist_orb_candidate_${i}.png"
done

echo "=== artf_astrologist_constellation_starlight ==="
for i in 1 2 3 4 5 6; do
  $GEN_SCRIPT \
    "oil painting of a fantasy game item icon, a close-up painted icon of a constellation pattern glowing with starlight — interconnected stars forming a celestial diagram, deep blue-teal with scattered sea-green starlight, simple iconic centered composition, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, canvas texture showing through, painterly edges, no text, no words, no letters, unsigned" \
    "$OUTDIR/artf_astrologist_constellation_starlight_candidate_${i}.png"
done

echo "=== ALL 36 TILE CANDIDATES GENERATED ==="
ls -la "$OUTDIR/"