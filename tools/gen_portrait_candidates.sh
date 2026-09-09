#!/bin/bash
set -euo pipefail
cd /home/fictive/runewake

source /home/fictive/.hermes/profiles/tcgbot/.env
export OPENROUTER_API_KEY

GEN="python3 pipeline/gen_image_openrouter.py"
OUTDIR="art_output/portraits"
mkdir -p "$OUTDIR"

STYLE_PREFIX="oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork reserved for magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork, loose expressive brushstrokes, thick visible impasto throughout, canvas texture showing through, painterly edges rather than crisp digital edges. in the style of Bloomweaver and Thornbark Defender."

echo "=== BATTLEMAGE CANDIDATES ==="

$GEN "${STYLE_PREFIX} A battlemage in battle-worn arcane armor, striding forward with a wand in one hand and a swirling aura of magical energy wreathed around the other arm, deep blue-teal and sea-green TIDE palette, abyssal depths with pale foam edges and scattered sea-green magical light, storm-tossed shoreline background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/battlemage_candidate_1.png"

$GEN "${STYLE_PREFIX} A battlemage in arcane half-plate, both arms raised casting a spell, crackling blue-white energy converging at their fingertips, deep blue-teal and sea-green TIDE palette, abyssal depths with pale foam edges and sea-green magical light, storm-tossed harbor behind, single figure, full-length portrait, no text, unsigned" "$OUTDIR/battlemage_candidate_2.png"

$GEN "${STYLE_PREFIX} A battlemage with a rune-etched blade in one hand and magical flames in the other, arcane armor showing tide-marked weathering, deep blue-teal and sea-green TIDE palette, abyssal depths with pale foam edges, stormy sky background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/battlemage_candidate_3.png"

$GEN "${STYLE_PREFIX} A battlemage reading from a glowing scroll, magic energy spiraling around their arm and weapon, tide-marked arcane armor, deep blue-teal and sea-green TIDE palette, abyssal depths with pale foam edges and scattered sea-green light, storm-tossed battlefield background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/battlemage_candidate_4.png"

$GEN "${STYLE_PREFIX} A battlemage gripping a coral-carved staff crackling with energy, one hand wreathed in sea-green magical fire, tide-marked armor, deep blue-teal and sea-green TIDE palette, abyssal depths with pale foam edges, Saltmere harbor storm background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/battlemage_candidate_5.png"

$GEN "${STYLE_PREFIX} A battlemage standing amid magical residue after battle, wand lowered but still glowing, aura fading around them, tide-marked armor scarred from combat, deep blue-teal and sea-green TIDE palette, abyssal depths with pale foam edges, smoke and magical haze background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/battlemage_candidate_6.png"

echo "=== ROGUE (THIEF) CANDIDATES ==="

$GEN "${STYLE_PREFIX} A rogue in dark form-fitting leather armor, crouched and ready, murky violet and shadow-heavy HOLLOW palette, bone-white and murky violet with patches of sickly green, twin daggers held in reverse grip, one at the ready, one held back, shadowy crypt background, cowl pulled up partially revealing a masked face, single figure, full-length portrait, no text, unsigned" "$OUTDIR/rogue_candidate_1.png"

$GEN "${STYLE_PREFIX} A rogue scaling a stone wall in a crypt, one dagger between teeth, the other reaching up, murky violet and shadow-heavy HOLLOW palette, bone-white and murky violet with patches of sickly green, Duskchapel crypt background with candlelight filtering through, sleek nimble silhouette, single figure, full-length portrait, no text, unsigned" "$OUTDIR/rogue_candidate_2.png"

$GEN "${STYLE_PREFIX} A rogue pressed against a shadowy pillar, only their eyes and daggers catching the candlelight, murky violet and shadow-heavy HOLLOW palette, bone-white and murky violet with patches of sickly green, Duskchapel cathedral interior background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/rogue_candidate_3.png"

$GEN "${STYLE_PREFIX} A rogue reaching for a glowing relic on a crypt altar, one dagger drawn back ready to throw, murky violet and shadow-heavy HOLLOW palette, bone-white and murky violet with patches of sickly green, Duskchapel underground chamber background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/rogue_candidate_4.png"

$GEN "${STYLE_PREFIX} A rogue lounging on a rooftop with a small pouch of coins, twin daggers sheathed, relaxed triumphant pose, murky violet and shadow-heavy HOLLOW palette, bone-white and murky violet with patches of sickly green, Duskchapel night skyline background with distant candlelit windows, single figure, full-length portrait, no text, unsigned" "$OUTDIR/rogue_candidate_5.png"

$GEN "${STYLE_PREFIX} A rogue mid-leap, daggers leading the way, cloak billowing behind, murky violet and shadow-heavy HOLLOW palette, bone-white and murky violet with patches of sickly green, dark corridor background with a sliver of moonlight, single figure, full-length portrait, no text, unsigned" "$OUTDIR/rogue_candidate_6.png"

echo "=== PALADIN CANDIDATES ==="

$GEN "${STYLE_PREFIX} A paladin in gleaming plate armor with a warhammer held in one hand and a banner planted beside them, standing guard, warm cream and pale gold DAWN palette with soft amber light, dawn-sky tones, radiant sunlit temple background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/paladin_candidate_1.png"

$GEN "${STYLE_PREFIX} A paladin kneeling in prayer before a sunlit altar, warhammer resting across their knees, golden light streaming through a stained glass window, warm cream and pale gold DAWN palette with soft amber light, Sunspire cathedral interior, single figure, full-length portrait, no text, unsigned" "$OUTDIR/paladin_candidate_2.png"

$GEN "${STYLE_PREFIX} A paladin charging forward with shield raised and hammer ready to strike, banner tied to their back, warm cream and pale gold DAWN palette with soft amber light, battlefield at dawn, sunlight breaking through clouds behind them, single figure, full-length portrait, no text, unsigned" "$OUTDIR/paladin_candidate_3.png"

$GEN "${STYLE_PREFIX} A paladin standing protectively before a wounded civilian, shield facing forward, hammer in a defensive stance, warm cream and pale gold DAWN palette with soft amber light, dawn-lit cobblestone street background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/paladin_candidate_4.png"

$GEN "${STYLE_PREFIX} A paladin holding their warhammer aloft, holy light streaming down onto it, armor gleaming with reflected gold, warm cream and pale gold DAWN palette with soft amber light, Sunspire architecture behind them, single figure, full-length portrait, no text, unsigned" "$OUTDIR/paladin_candidate_5.png"

$GEN "${STYLE_PREFIX} A paladin standing vigil at a gate, lantern in one hand, hammer resting on shoulder, warm cream and pale gold DAWN palette muted to twilight tones, soft amber light from the lantern, stone archway background, single figure, full-length portrait, no text, unsigned" "$OUTDIR/paladin_candidate_6.png"

echo "=== ALL CANDIDATES GENERATED ==="
ls -la "$OUTDIR/" | grep -E "candidate|battlemage|rogue|paladin"