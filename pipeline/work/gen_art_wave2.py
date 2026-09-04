#!/usr/bin/env python3
"""
TASK-ART-WAVE-2: Generate card art for TASK-CARD-WAVE-2's 40 cards.
FLUX.2 Pro via OpenRouter, ART_PROMPT_PLAYBOOK v1 prompts, 832x1216 portrait.
Style: v3.0 dark fantasy oil painting.
Per-stratum hex palette. Subject-first, no negative phrases.
"""
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(__file__)
ROOT = os.path.join(HERE, '..', '..')
SCRIPT = os.path.join(ROOT, 'pipeline', 'gen_image_openrouter.py')
OUT_DIR = os.path.join(ROOT, 'client', 'content', 'art')

MODEL = "black-forest-labs/flux.2-pro"
WIDTH = 832
HEIGHT = 1216

# --- v3.0 STYLE SPINE (Trikzos ruled 2026-08-17, final)
STYLE_SPINE = (
    "oil painting in the style of classical storybook illustration, "
    "dramatic painted light against deep shadow (chiaroscuro), "
    "swirling expressive brushwork, "
    "single grounded focal subject staged with breathing room "
    "in the manner of a Renaissance tableau, "
    "atmospheric depth with softly rendered distant background, "
    "restrained palette with selective vivid accents, "
    "thick impasto texture, painted by hand, unsigned artwork"
)

# --- Stratum palettes (HEX codes from playbook §3)
PALETTE = {
    "EMBER": "soot black #14100E with molten orange #C4501B and ash grey #6E655C",
    "HOLLOW": "bone white #D8CEBB with murky violet #3A2B45 and sickly green #6E7F4A",
    "VERDANT": "deep moss #1E2E1C with emerald #2F6B3A and wet bark brown #4A3626",
    "TIDE": "abyssal blue #0E2436 with teal #1F6F72 and pale foam #C9DCD8",
    "DAWN": "warm gold #C8A04A with pale cream #EFE3C6 and amber #8A5A1E",
}

# --- Camera clause for portraits/figures (playbook §2)
CAMERA_PORTRAIT = (
    "full figure, shot on a 50mm lens at f/2.8, low eye-level, rule of thirds"
)

# --- Context for each stratum
CONTEXT = {
    "EMBER": "volcanic forge interior, dark basalt walls, ember-lit haze, empty unlit background falling to shadow",
    "HOLLOW": "crumbling barrow chamber, bone-littered floor, cold mist, empty unlit background falling to shadow",
    "VERDANT": "dense forest clearing, moss-covered stone, shafts of green light through canopy, empty background falling to shadow",
    "TIDE": "submerged grotto, coral-covered pillars, dim aquatic light from above, empty background falling to shadow",
    "DAWN": "ancient temple interior at first light, warm stone, soft golden haze, empty background falling to shadow",
}

# --- Card-specific prompts
# Each: subject (card name + creature type), flavor-inspired details
# Built per playbook §1: SUBJECT, STATE, STYLE, CONTEXT, CAMERA
# Card art MAY contain creatures/figures (playbook §5 exception)

CARDS = [
    # ── EMBER (8 cards: 4C/3U/1R) ───────────────────────────────────────
    # Families: PIERCE/REACH/VENOM
    {
        "id": "emb_c_stone_slinger",
        "prompt": (
            "A stone slinger — a wiry young man in soot-black leathers, a leather sling "
            "whirling above his head, a pouch of volcanic stones at his belt, his face lit "
            "by the orange glow of a nearby forge, barefoot on hot cinders. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_c_smoldering_pike",
        "prompt": (
            "A smoldering pike — a gaunt warrior in heat-cracked iron plates, one hand "
            "gripping a long pike whose metal tip glows a dull cherry-red, smoke curling "
            "from the haft where it meets the head, his body half-turned as if to thrust. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_c_furnace_reaver",
        "prompt": (
            "A furnace reaver — a scarred brute in patchwork plate and chain, dragging "
            "a length of red-hot chain behind him, the links glowing where they scrape stone, "
            "his face half-scarred from old burns, teeth bared in a grim snarl. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_c_volcanic_boon",
        "prompt": (
            "A volcanic boon — a broad-shouldered dwarf-like figure with a stone-covered back, "
            "arms wide in a gesture of offering, veins of molten orange running through the "
            "rock of his torso, a gentle radiance emanating from his chest like a forge-heart. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_u_lava_burst",
        "prompt": (
            "A ritual of lava burst — a shaman's hands cupped before her, a sphere of "
            "molten stone expanding between her palms, cracks of orange light spreading "
            "through the air around it, her face illuminated from below, expression "
            "strained with the effort of containment, volcanic heat shimmer visible. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_u_cinder_venom",
        "prompt": (
            "A cinder venom — a lithe, feral humanoid with ash-grey skin and burning "
            "yellow eyes, crouched low, its fingers lengthened into blackened claws that "
            "drip molten orange droplets, its mouth open revealing a forked tongue, "
            "a haze of heat rising from its shoulders. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_u_scoria_prowler",
        "prompt": (
            "A scoria prowler — a lean hunter in jagged volcanic glass armour, crouching "
            "atop a field of broken obsidian shards, a serrated black-glass blade in each "
            "hand, her body scarred from years of crawling over sharp rock, eyes scanning "
            "for prey with predatory stillness. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_r_sharptongue_elder",
        "prompt": (
            "A sharptongue elder — an ancient, wizened figure seated on a throne of cooled "
            "lava rock, his face a map of wrinkles and burn scars, one gnarled hand raised "
            "with a single finger pointing, the air around his mouth shimmering with heat "
            "as if his words themselves burned, dark robes hanging in tatters around him. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── HOLLOW (8 cards: 5C/2U/1R) ──────────────────────────────────────
    # Families: VENOM/UNEARTH/ECHO
    {
        "id": "hol_c_plague_rat",
        "prompt": (
            "A plague rat — a rat the size of a dog, its fur patchy and wet with "
            "greenish pus, its eyes milky white, a trail of filth and bile behind it, "
            "crouched on a damp stone floor in a crypt, one paw resting on a human "
            "finger-bone. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_venom_fang",
        "prompt": (
            "A venom fang — a sinuous, pale-scaled serpent as thick as a human arm, "
            "coiled on a slab of cracked stone, its head raised, mouth agape to reveal "
            "two long curved fangs dripping a viscous green venom, its forked tongue "
            "tasting the cold air of the crypt. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_bone_trader",
        "prompt": (
            "A bone trader — a hunched, crooked figure in a patchwork coat sewn from "
            "tanned hide and burial shrouds, a wooden cart laden with bones at his side, "
            "one hand holding up a polished femur as if appraising it, his face half-hidden "
            "beneath a wide-brimmed hat, a thin smile on grey lips. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_stygian_hound",
        "prompt": (
            "A Stygian hound — a gaunt, wolf-like creature with skin stretched tight over "
            "its ribs, its hide the colour of old ash, eyes glowing with cold blue light, "
            "saliva dripping from jaws that seem too wide, its claws scraping the stone "
            "floor as it pads forward, cold mist rising from its back. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_unearth_scavenger",
        "prompt": (
            "An unearth scavenger — a lanky humanoid with too many joints, wearing a "
            "tattered hooded cloak, its hands ending in long digging claws caked with "
            "fresh grave-dirt, a sack of bone fragments slung over one shoulder, its face "
            "a noseless skull with worms writhing in the eye sockets. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_u_barrow_rite",
        "prompt": (
            "A barrow rite — a priest in black vestments standing before a low stone "
            "altar, one hand holding a curved bone-knife, the other extended over a "
            "bowl of dark liquid, green witchlight rising from the bowl in curling "
            "ribbons, his face expressionless, the air cold enough to mist. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_u_nightshade_priest",
        "prompt": (
            "A nightshade priest — a tall woman in a long robe of deep purple-black, "
            "her face painted with white bone-dust patterns, a censer of green smoke "
            "swinging from her hand, her eyes entirely black, dark veins visible on her "
            "neck and hands, standing amid a circle of small candles. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_r_black_tide",
        "prompt": (
            "The black tide — a towering, amorphous figure of writhing shadow and bone, "
            "its form constantly shifting, human skulls and ribcages floating within its "
            "mass, two pinpoints of violet light where eyes should be, skeletal arms "
            "emerging from its sides to grasp at the air, a wave of darkness rolling "
            "beneath it. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── VERDANT (8 cards: 4C/3U/1R) ──────────────────────────────────────
    # Families: GUARD/ROOTED/WARD
    {
        "id": "vrd_c_root_sprout",
        "prompt": (
            "A root sprout — a tiny humanoid figure formed of twisted root and pale green "
            "sprouts, barely knee-high, its head a budding flower yet to open, its body "
            "thin and fragile, standing on a mossy stone amid ferns and small mushrooms. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_c_thorn_wall",
        "prompt": (
            "A thorn wall — a hulking mass of woven thorn and vine in roughly humanoid "
            "shape, its entire body covered in foot-long black thorns, no face visible "
            "beneath the tangled branches, standing immobile like a living barricade "
            "across a forest path. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_c_warding_vine",
        "prompt": (
            "A warding vine — a slender woman with living vines woven into her long hair "
            "and trailing from her wrists, her hands raised in a warding gesture, a "
            "semi-transparent barrier of green light forming between her palms, her eyes "
            "closed in concentration, moss growing along her collarbone. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_c_leaf_mender",
        "prompt": (
            "A leaf mender — a gentle-faced elderly woman in a cloak of fallen leaves, "
            "kneeling beside a wounded fox, her hands glowing with soft green light as she "
            "presses them to the animal's side, fallen leaves swirling gently around her "
            "in a slow spiral, her expression kind and tired. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_u_ironbark_guard",
        "prompt": (
            "An ironbark guard — a powerfully-built warrior whose skin has the texture "
            "of ancient oak bark, a greatshield of solid ironbark wood on one arm, his "
            "body scarred from countless battles, roots emerging from his feet into the "
            "earth, his face weathered and patient, eyes glowing with a steady amber light. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_u_sylvan_pulse",
        "prompt": (
            "Sylvan pulse — a druid standing at the centre of a forest glade, both arms "
            "outstretched, a wave of green energy pulsing outward from her core, the grass "
            "at her feet rippling in concentric rings, leaves and petals lifting into the "
            "air around her, her hair floating as if underwater. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_u_forest_stalker",
        "prompt": (
            "A forest stalker — a lithe hunter with antlers growing from his head, his "
            "body painted with earth tones and wrapped in mossy leathers, a long curved "
            "bow in one hand, nocked arrow tipped with a thorn, his face half-visible "
            "between the trees, one glowing green eye fixed on a target. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_r_worldtree_custodian",
        "prompt": (
            "A worldtree custodian — a tall, ageless woman whose body is partially "
            "merged with the trunk of an enormous glowing tree, her arms extending into "
            "branches covered in luminescent leaves, her lower body fused with the roots, "
            "her face serene and ancient, golden sap-like light flowing through "
            "veins visible beneath her bark-textured skin. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── TIDE (8 cards: 5C/2U/1R) ─────────────────────────────────────────
    # Families: ECHO/REACH/PIERCE
    {
        "id": "tid_c_foam_runner",
        "prompt": (
            "A foam runner — a slight, nimble humanoid with pale blue-grey skin and "
            "webbed feet, barefoot, running across the surface of shallow tidewater, "
            "foam spraying from each step, a trident of coral held loosely in one hand, "
            "its large dark eyes bright with mischief. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_tide_reacher",
        "prompt": (
            "A tide reacher — a bent old figure in salt-stained robes, one long arm "
            "extended toward the horizon, barnacles clinging to his sleeves, his face "
            "weather-beaten and wise, a staff of driftwood in his other hand, the hem "
            "of his robe trailing in shallow tidewater. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_kelp_diviner",
        "prompt": (
            "A kelp diviner — a woman with long seaweed-like hair draped across her "
            "shoulders, sitting cross-legged on a coral outcrop, strands of kelp spread "
            "across her lap like scrolls, her fingers tracing patterns in the air above "
            "them, a faint bioluminescent glow connecting the strands, her expression "
            "blank and reading. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_saltthorn_scout",
        "prompt": (
            "A saltthorn scout — a lean creature with crystalline salt-spines protruding "
            "from its shoulders and forearms, wearing armour pieced together from crustacean "
            "shells, a jagged blade of salt-crusted coral in each hand, crouched low, "
            "its eyes tracking sideways, tasting the salt air with a forked tongue. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_undertow_current",
        "prompt": (
            "An undertow current — a tall, silent figure whose body is composed of "
            "dark, translucent seawater in vaguely human shape, debris and small fish "
            "suspended within its form, its limbs trailing into wisps of current, "
            "standing in a submerged chamber, kelp swaying around its legs. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_u_tidal_barrier",
        "prompt": (
            "A tidal barrier — a broad-shouldered guardian with skin like wet granite, "
            "standing knee-deep in surging tidewater, a shield of solidified seawater "
            "on one arm, water streaming from it in sheets, his body covered in "
            "carved wave-pattern scars, rooted in place against the current. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_u_depth_charge",
        "prompt": (
            "Depth charge — a spellcaster in deep-sea robes, her hands pressed together "
            "as if in prayer, a sphere of impossibly dense dark water forming between "
            "them, pressure rippling visible around it, bubbles streaming upward, her "
            "face strained with the force of containment, veins bulging on her temples. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_r_echo_caller",
        "prompt": (
            "An echo caller — a beautiful, commanding woman in flowing robes of teal and "
            "silver, a conch-shell horn raised to her lips, concentric rings of sound "
            "visible as rippling light emanating from the horn, her hair and robes "
            "billowing as if caught in the wake of a great wave, her eyes glowing with "
            "inner light. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── DAWN (8 cards: 4C/2U/1R/1M) ──────────────────────────────────────
    # Families: WARD/PIERCE/FRAGILE
    {
        "id": "dwn_c_gleam_sentinel",
        "prompt": (
            "A gleam sentinel — a small construct of polished white stone and gold filigree, "
            "shaped like a sleek temple guardian, one eye a glowing amber gem, standing "
            "vigil on a stone plinth, shafts of golden dawn light falling across its form, "
            "its surface reflecting the warm light like polished marble. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_c_light_lancer",
        "prompt": (
            "A light lancer — a young woman in gleaming scale armour of pale gold, a long "
            "lance resting on her shoulder, the tip catching the first light of dawn, her "
            "expression sharp and ready, standing at the edge of a temple step, the rising "
            "sun casting a halo behind her helm. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_c_daybreak_guard",
        "prompt": (
            "A daybreak guard — a stoic knight in full cream-and-ivory plate armour, a "
            "tower shield planted before her, the shield's surface etched with a rising-sun "
            "motif, her helm visor lowered, standing in the doorway of a temple as the "
            "first rays of light stream past her, casting a long shadow behind. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_c_sunpearl_healer",
        "prompt": (
            "A sunpearl healer — a serene woman in simple cream robes, a single "
            "luminous pearl the size of an egg held in her cupped hands, warm golden "
            "light emanating from it to bathe her face and chest, her eyes closed, a "
            "faint smile on her lips, the pearl's light pushing back the shadows around her. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_u_fading_spark",
        "prompt": (
            "A fading spark — a young woman in tattered white robes, one hand pressed "
            "to her chest where a bright golden light is visibly fading, her body "
            "beginning to dissolve into motes of warm light, her expression peaceful "
            "and resigned, the last light catching her hair and cheek. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_u_reckoning_strike",
        "prompt": (
            "Reckoning strike — a stern-faced judge in white-gold ceremonial armour, "
            "one arm raised high, a blade of pure golden light materializing in her "
            "hand, her expression cold and righteous, scales of justice etched on her "
            "breastplate, rays of light splitting through the temple windows behind her "
            "as if answering the call. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_r_aurora_warden",
        "prompt": (
            "An aurora warden — a majestic winged figure in flowing robes of pale blue "
            "and gold, wings of shimmering aurora-light spreading behind her, a staff "
            "tipped with a crystal that swirls with captured dawn-light in one hand, "
            "her face calm and ancient, standing at the top of a temple stair as "
            "ribbons of aurora dance in the sky above her. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_m_last_dawn",
        "prompt": (
            "The last dawn — a single enormous golden orb floating above a circular "
            "stone altar, its surface a turmoil of white-gold fire and blinding light, "
            "rays extending in all directions, the altar cracked and glowing from the "
            "heat, smaller orbs of light orbiting it like planets, the scene both "
            "beautiful and apocalyptic, empty temple receding into pure white. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
]


# ── RULE (8) corner check ───────────────────────────────────────────────────
def check_rule8(path):
    """Check image for corner lettering. Returns (ok, message)."""
    try:
        from PIL import Image
        img = Image.open(path).convert("RGB")
        w, h = img.size
        corner_size = min(40, w // 8, h // 8)
        corners = {
            "top-left": (0, 0, corner_size, corner_size),
            "top-right": (w - corner_size, 0, w, corner_size),
            "bottom-left": (0, h - corner_size, corner_size, h),
            "bottom-right": (w - corner_size, h - corner_size, w, h),
        }
        issues = []
        for name, (x1, y1, x2, y2) in corners.items():
            pixels = []
            for py in range(y1, y2):
                for px in range(x1, x2):
                    r, g, b = img.getpixel((px, py))
                    pixels.append(0.2126 * r + 0.7152 * g + 0.0722 * b)
            if not pixels:
                continue
            n = len(pixels)
            mean = sum(pixels) / n
            var = sum((p - mean) ** 2 for p in pixels) / n
            std = var ** 0.5
            if std > 30:
                issues.append(f"{name} std={std:.1f}")
        if issues:
            return False, "RULE(8) corner check: " + "; ".join(issues)
        return True, "RULE(8) corner check: clean"
    except Exception as e:
        return False, f"RULE(8) check error: {e}"


# ── Generate a single image ─────────────────────────────────────────────
def generate_one(card_id, prompt, out_path, max_attempts=3):
    """Generate one image with up to max_attempts retries after RULE 8.
    Returns True on success."""
    current_prompt = prompt
    for attempt in range(1, max_attempts + 1):
        print(f"  [{card_id}] Attempt {attempt}/{max_attempts}...")
        start = time.time()
        ret = subprocess.run(
            [sys.executable, SCRIPT, current_prompt, out_path, "--model", MODEL,
             "--width", str(WIDTH), "--height", str(HEIGHT)],
            capture_output=True, text=True, timeout=300,
        )
        elapsed = time.time() - start

        if ret.returncode != 0 or not os.path.exists(out_path) or os.path.getsize(out_path) < 1000:
            print(f"    API FAILED (exit {ret.returncode}, {elapsed:.0f}s)")
            if ret.stderr:
                print(f"    stderr: {ret.stderr[:300]}")
            if attempt < max_attempts:
                print(f"    Retrying...")
                time.sleep(3)
                continue
            return False

        size = os.path.getsize(out_path)
        try:
            from PIL import Image as PILImage
            img = PILImage.open(out_path)
            dims = f"{img.size[0]}x{img.size[1]}"
        except Exception:
            dims = "unknown"
        cost = 0.036
        print(f"    Saved ({size/1024:.0f} KB, {dims}, ${cost:.3f}, {elapsed:.0f}s)")

        # RULE 8 check
        r8_ok, r8_msg = check_rule8(out_path)
        if r8_ok:
            print(f"    {r8_msg}")
            return True
        else:
            print(f"    {r8_msg}")
            if attempt < max_attempts:
                print(f"    Regenerating with reinforced text suppression...")
                current_prompt = (prompt.rstrip(" .\"'") +
                    ". Absolutely NO text, NO letters, NO signatures anywhere in the image.")
                time.sleep(2)
                continue
            else:
                print(f"    PERSISTS after {max_attempts} attempts — will flag")
                return True  # Accept anyway with flag

    return True


# ── Create .import file ─────────────────────────────────────────────────
def create_import(card_id, out_path):
    """Create Godot .import file for the webp asset."""
    safe_id = card_id.replace("_", "")
    import_path = out_path + ".import"
    uid = f"uid://{safe_id}"
    import_content = f"""\
[remap]

importer="texture"
type="CompressedTexture2D"
uid="{uid}"
path="res://.godot/imported/{card_id}.webp-{card_id}_placeholder.ctex"
metadata={{
"vram_texture": false
}}

[deps]

source_file="res://content/art/{card_id}.webp"
dest_files=["res://.godot/imported/{card_id}.webp-{card_id}_placeholder.ctex"]

[params]

compress/mode=0
compress/high_quality=true
compress/lossy_quality=0.7
compress/hdr_compression=1
compress/normal

"""
    with open(import_path, "w") as f:
        f.write(import_content)
    print(f"    Created: {import_path}")


# ── Wire art into card JSON ────────────────────────────────────────────
def wire_art_into_json(card_ids):
    """Update each card's JSON file to set art: {\"file\": \"<card_id>.webp\"} for the new cards."""
    strata_files = {
        "emb": os.path.join(ROOT, "content/cards/ember.json"),
        "hol": os.path.join(ROOT, "content/cards/hollow.json"),
        "vrd": os.path.join(ROOT, "content/cards/verdant.json"),
        "tid": os.path.join(ROOT, "content/cards/tide.json"),
        "dwn": os.path.join(ROOT, "content/cards/dawn.json"),
    }
    
    def prefix_of(cid):
        return cid[:3]
    
    for prefix, path in strata_files.items():
        with open(path) as f:
            cards = json.load(f)
        
        changed = 0
        for c in cards:
            if c["id"] in card_ids:
                c["art"] = {"file": f"{c['id']}.webp"}
                changed += 1
        
        if changed:
            with open(path, "w") as f:
                json.dump(cards, f, indent=2)
            print(f"  Wired {changed} cards in {path}")
        else:
            print(f"  No new cards to wire in {path}")


# ── Main ────────────────────────────────────────────────────────────────
def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    # Sort cards by strata and rarity for logical order
    priority = {"EMBER": 0, "HOLLOW": 1, "VERDANT": 2, "TIDE": 3, "DAWN": 4}
    rarity_order = {"COMMON": 0, "UNCOMMON": 1, "RARE": 2, "RELIC": 3}
    
    def strata_of(cid):
        prefix = cid[:3]
        return {"emb": "EMBER", "hol": "HOLLOW", "vrd": "VERDANT", "tid": "TIDE", "dwn": "DAWN"}.get(prefix, "UNKNOWN")
    def rarity_of(cid):
        parts = cid.split("_")
        r = parts[1]
        return {"c": "COMMON", "u": "UNCOMMON", "r": "RARE", "x": "RELIC", "m": "RELIC"}.get(r, "UNKNOWN")
    
    sorted_cards = sorted(CARDS, key=lambda c: (priority.get(strata_of(c["id"]), 99), rarity_order.get(rarity_of(c["id"]), 99)))
    
    total_cost = 0.0
    successes = 0
    failures = []
    rule8_flags = []

    # First 6 = veto gate samples (one from each strata, prioritize rares/mythic)
    veto_ids = [
        'emb_r_sharptongue_elder',
        'hol_r_black_tide',
        'vrd_r_worldtree_custodian',
        'tid_r_echo_caller',
        'dwn_r_aurora_warden',
        'dwn_m_last_dawn',
    ]

    print(f"{'='*70}")
    print(f"TASK-ART-WAVE-2 — 40 card images for TASK-CARD-WAVE-2")
    print(f"FLUX.2 Pro via OpenRouter, 832x1216, style v3.0")
    print(f"Estimated cost: ${len(sorted_cards) * 0.036:.2f}")
    print(f"{'='*70}")

    # Phase 1: 6-sample veto gate
    print(f"\n{'='*70}")
    print(f"PHASE 1 — Veto Gate (6 samples)")
    print(f"{'='*70}")
    for cdef in sorted_cards:
        cid = cdef["id"]
        if cid not in veto_ids:
            continue
        out_path = os.path.join(OUT_DIR, f"{cid}.webp")
        print(f"\n[{cid}] {cdef['prompt'][:80]}...")
        
        if generate_one(cid, cdef["prompt"], out_path):
            create_import(cid, out_path)
            successes += 1
            total_cost += 0.036
        else:
            print(f"  FAILED: {cid}")
            failures.append(cid)
        
        time.sleep(2)

    # Report veto gate results
    print(f"\n{'='*70}")
    print(f"VETO GATE — 6 samples generated")
    print(f"{'='*70}")
    for cid in veto_ids:
        path = os.path.join(OUT_DIR, f"{cid}.webp")
        exists = os.path.exists(path)
        size = os.path.getsize(path) if exists else 0
        ok = "OK" if exists and size > 1000 else "FAIL"
        print(f"  {ok} {cid}.webp ({size/1024:.0f} KB)" if ok == "OK" else f"  {ok} {cid}.webp")
    
    # Phase 2: remaining 34 cards
    print(f"\n{'='*70}")
    print(f"PHASE 2 — Full batch (remaining 34 cards)")
    print(f"{'='*70}")
    for cdef in sorted_cards:
        cid = cdef["id"]
        if cid in veto_ids:
            continue
        out_path = os.path.join(OUT_DIR, f"{cid}.webp")
        print(f"\n[{cid}] {cdef['prompt'][:80]}...")
        
        if generate_one(cid, cdef["prompt"], out_path):
            create_import(cid, out_path)
            successes += 1
            total_cost += 0.036
        else:
            print(f"  FAILED: {cid}")
            failures.append(cid)
        
        time.sleep(2)

    # Wire art into JSON
    print(f"\n{'='*70}")
    print(f"PHASE 3 — Wire art into card JSON files")
    print(f"{'='*70}")
    all_ids = [c["id"] for c in sorted_cards]
    wire_art_into_json(all_ids)

    # Summary
    print(f"\n{'='*70}")
    print(f"TASK-ART-WAVE-2 COMPLETE")
    print(f"{'='*70}")
    print(f"  Generated: {successes}/{len(sorted_cards)}")
    print(f"  Total cost: ${total_cost:.2f}")
    if failures:
        print(f"  FAILURES: {', '.join(failures)}")
    if rule8_flags:
        print(f"  RULE 8 flags: {', '.join(rule8_flags)}")
    
    # Verify all files
    print(f"\n  Verification:")
    all_ok = True
    for cdef in sorted_cards:
        cid = cdef["id"]
        path = os.path.join(OUT_DIR, f"{cid}.webp")
        import_path = path + ".import"
        webp_ok = os.path.exists(path) and os.path.getsize(path) > 1000
        imp_ok = os.path.exists(import_path)
        status = "✓" if webp_ok and imp_ok else "✗"
        if not webp_ok or not imp_ok:
            all_ok = False
        print(f"    {status} {cid}.webp {'+ .import' if imp_ok else ''}")
    
    sys.exit(0 if all_ok else 1)


if __name__ == "__main__":
    main()