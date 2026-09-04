#!/bin/bash
# Batch generate all portrait images
set -e
source ~/.hermes/.env
export OPENROUTER_API_KEY
cd /home/fictive/runewake

GEN_SCRIPT="python3 pipeline/gen_image_openrouter.py"
OUTDIR="/home/fictive/runewake-lane2/art_output/portraits"
mkdir -p "$OUTDIR"

echo "=== Generating 6 astrologist candidates ==="

# Astrologist 1
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork reserved for magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges rather than crisp digital edges. in the style of Bloomweaver and Thornbark Defender. A mysterious astrologist seer standing in a nighttime observatory, deep blue-teal and midnight purple palette, abyssal depths and pale foam edges with scattered sea-green starlight, holding a glowing celestial orb in both hands, the orb filled with tiny pinprick stars, robes flowing like starry night sky, ancient astronomical instruments and star charts visible in the background, single figure centered, full-length portrait, celestial motif, no text, unsigned" \
  "$OUTDIR/astrologist_candidate_1.png"

# Astrologist 2
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow, swirling expressive brushwork for magical energy, single grounded focal subject staged with breathing room, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges rather than crisp digital edges. in the style of Bloomweaver and Thornbark Defender. A hooded astrologist reading a glowing celestial orb in a stone observatory tower, deep blue-teal and midnight purple palette with scattered sea-green starlight, the orb illuminating her face from below, star charts and brass instruments around her, robes embroidered with constellation patterns, full-length portrait, celestial motif, no text, unsigned" \
  "$OUTDIR/astrologist_candidate_2.png"

# Astrologist 3
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic chiaroscuro lighting, swirling expressive brushwork for magical energy, single focal subject with breathing room in the manner of a Renaissance tableau, atmospheric depth, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges. in the style of Bloomweaver and Thornbark Defender. An astrologist gazing at a constellation-filled orb hovering before her, deep blue-teal and midnight purple palette with sea-green starlight, observatory balcony open to a starry night sky, flowing robes like the night sky, single figure centered, full-length portrait, celestial motif, no text, unsigned" \
  "$OUTDIR/astrologist_candidate_3.png"

# Astrologist 4
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow, swirling expressive brushwork, single grounded focal subject with breathing room, atmospheric depth, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges. in the style of Bloomweaver and Thornbark Defender. An astrologist in deep indigo robes standing at a celestial lectern, a crystal orb on the lectern glowing with starry light, deep blue-teal palette with sea-green and starlight accents, ancient stone chamber with arched windows showing the night sky, single figure, full-length portrait, celestial motif, no text, unsigned" \
  "$OUTDIR/astrologist_candidate_4.png"

# Astrologist 5
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light, chiaroscuro, swirling expressive brushwork for magical energy, single focal subject with breathing room, atmospheric depth, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges. in the style of Bloomweaver and Thornbark Defender. An astrologist with a star-scattered cloak, one hand raised to a hovering astrolabe, the other holding a glowing orb, deep blue-teal and midnight purple palette with sea-green starlight, stone observatory dome visible behind, full-length portrait, celestial motif, no text, unsigned" \
  "$OUTDIR/astrologist_candidate_5.png"

# Astrologist 6
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow, swirling expressive brushwork, single grounded focal subject with breathing room, atmospheric depth, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges. in the style of Bloomweaver and Thornbark Defender. A young astrologist woman in starry midnight robes, kneeling before a celestial pool, the pool reflecting constellations, an orb floating beside her, deep blue-teal and midnight purple palette with sea-green starlight, single figure centered, full-length portrait, celestial motif, no text, unsigned" \
  "$OUTDIR/astrologist_candidate_6.png"

echo "=== Generating remaining portraits ==="

# Rogue
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges rather than crisp digital edges. in the style of Bloomweaver and Thornbark Defender. A rogue in dark form-fitting leather armor, crouched and ready, murky violet and shadow-heavy palette, bone-white and murky violet with patches of sickly green, twin daggers held in reverse grip, one at the ready, one held back, shadowy alley or crypt background, cowl pulled up partially revealing a masked face, sleek nimble silhouette, stealthy posture, single figure, full-length portrait, no text, unsigned" \
  "$OUTDIR/rogue.png"

# Battlemage
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork for magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges rather than crisp digital edges. in the style of Bloomweaver and Thornbark Defender. A battlemage in battle-worn arcane armor, striding forward with a wand in one hand and a swirling aura of magical energy wreathed around the other arm, deep blue-teal and sea-green palette, abyssal depths with pale foam edges and scattered sea-green magical light, storm-tossed shoreline or magical battlefield background, single figure, full-length portrait, no text, unsigned" \
  "$OUTDIR/battlemage.png"

# Paladin
$GEN_SCRIPT \
  "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges rather than crisp digital edges. in the style of Bloomweaver and Thornbark Defender. A paladin in gleaming plate armor with a warhammer held in one hand and a banner planted beside them, standing guard, warm cream and pale gold palette with soft amber light, dawn-sky tones, radiant sunlit temple or battlefield at dawn, single figure, full-length portrait, no text, unsigned" \
  "$OUTDIR/paladin.png"

echo "=== ALL PORTRAITS GENERATED ==="
ls -la "$OUTDIR/"