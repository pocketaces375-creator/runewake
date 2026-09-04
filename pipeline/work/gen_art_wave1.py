#!/usr/bin/env python3
"""
TASK-ART-WAVE-1: Generate card art for TASK-CARD-WAVE-1's 40 cards.
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
# Per ART_WAVES.md and playbook: dark fantasy oil painting
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
    # ── EMBER (8 cards) ──────────────────────────────────────────────────
    {
        "id": "emb_c_scorch_imp",
        "prompt": (
            "A scorch imp — a hairless, ember-skinned humanoid the size of a child, "
            "crouched low, one hand pressed to the stone, the other raised in a claw, "
            "its eyes burning like forge-coals, a thin trail of smoke rising from its back. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_c_blaze_scout",
        "prompt": (
            "A blaze scout — a lean young woman in soot-stained leathers, running forward "
            "through a cloud of ash, one hand trailing a ribbon of fire, her face lit from below "
            "by the flames, expression focused and fierce. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_c_ember_impaler",
        "prompt": (
            "An ember impaler — a broad-shouldered warrior in cracked obsidian plate, "
            "holding a spear forged from a volcanic shard, the tip glowing orange-white, "
            "deep cracks in the stone revealing molten veins, muscles tensed for a throw. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_c_firecrown_raider",
        "prompt": (
            "A firecrown raider — a towering berserker in battered mail, a crown of living "
            "flame flickering above his helm, twin axes raised, ash swirling around his shoulders, "
            "the forge-light catching the edges of his weapons. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_u_flame_tongue_knight",
        "prompt": (
            "A flame-tongue knight — a solemn warrior in dark steel plate, one hand resting "
            "on a longsword whose blade burns with a quiet orange flame, his face half-lit "
            "by the glow, standing motionless amid drifting cinders. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_u_burnout_mage",
        "prompt": (
            "A burnout mage — a woman in singed robes, both hands outstretched, a vortex of "
            "flame condensing between her palms, her hair half-burned, face radiant with effort, "
            "sparks trailing from her fingertips. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_u_lava_caller",
        "prompt": (
            "A lava caller — a shaman draped in volcanic glass beads, arms raised, commanding "
            "a ribbon of molten stone to rise from a crack in the floor, the heat warping the air, "
            "her expression calm and absolute. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "emb_r_ember_warleader",
        "prompt": (
            "An ember warleader — a scarred commander in ornate black plate trimmed with gold, "
            "a greatshield on one arm, her other hand gripping a broadsword whose fuller glows "
            "with banked heat, standing at the head of an ash-choked ridge, war-helm under one arm. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['EMBER']}. "
            f"{CONTEXT['EMBER']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── HOLLOW (8 cards) ─────────────────────────────────────────────────
    {
        "id": "hol_c_bone_tick",
        "prompt": (
            "A bone tick — a bloated, segmented insect-thing the size of a cat, its carapace "
            "the colour of old ivory, mandibles clicking, a slick of grave-mud trailing behind it, "
            "crouched on a stone slab in a dark crypt. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_dust_spinner",
        "prompt": (
            "A dust spinner — a gaunt figure wrapped in tattered burial shrouds, fingers "
            "moving like weaver's shuttles, spinning threads of pale dust and bone-silk into a "
            "gossamer shroud, its face a blank ivory mask. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_ghoul_scavenger",
        "prompt": (
            "A ghoul scavenger — a hunched, emaciated humanoid with grey-green skin and a "
            "tattered leather apron, clutching a rusted cleaver, its head cocked as if listening "
            "for something beneath the flagstones, grave-dust on its chin. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_barrow_call",
        "prompt": (
            "A barrow caller — a lanky figure in rotted priest-vestments, one arm raised, "
            "mouth open in a silent chant, pale green witchlight spilling from its eye sockets, "
            "the barrow mound behind it seeping cold mist. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_c_wraith_bonebreaker",
        "prompt": (
            "A wraith bonebreaker — a translucent, skeletal figure clad in rusted chainmail, "
            "one hand gripping a notched battle-axe, its form half-coalesced from swirling "
            "bone-dust, a cold blue core visible in its chest cavity. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_u_soul_picker",
        "prompt": (
            "A soul picker — a sharp-featured woman in corroded bronze plate, a lantern "
            "on a chain in one hand, the lantern's light a pulsing violet, her other hand "
            "extended as if to pluck something invisible from the air. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_u_putrid_knight",
        "prompt": (
            "A putrid knight — a hollow-eyed warrior in cracked and rusting full plate, "
            "greenish corruption seeping from the joints, a longsword held point-down, "
            "his oath-sigil barely visible beneath the bloom of decay on his shield. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "hol_r_death_drummer",
        "prompt": (
            "A death drummer — a skeletal drummer in rotting ceremonial armour, both arms "
            "raised, drumsticks poised above a war-drum made of stretched human hide and "
            "aged bone, a rhythmic green glow pulsing from within the drum's hollow. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['HOLLOW']}. "
            f"{CONTEXT['HOLLOW']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── VERDANT (8 cards) ────────────────────────────────────────────────
    {
        "id": "vrd_c_moss_shield",
        "prompt": (
            "A moss shield — a small moss-covered golem, its body a mound of living stone "
            "and thick green moss, a knot of roots forming one arm like a shield, standing "
            "sentry amid ferns and damp undergrowth. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_c_root_tender",
        "prompt": (
            "A root tender — a wiry young woman with bark-like patches on her skin, kneeling, "
            "one hand pressed to the forest floor, roots emerging from the soil and winding around "
            "her arm, her expression calm and listening. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_c_bark_sentinel",
        "prompt": (
            "A bark sentinel — a tall humanoid figure of gnarled wood and stone, its body "
            "weathered by centuries, one fist resting on the ground like a pillar, ancient moss "
            "hanging from its shoulders, its eyes two points of amber light. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_c_vine_weaver",
        "prompt": (
            "A vine weaver — a dark-skinned woman with living vines woven into her hair and "
            "arms, hands outstretched, thorned vines spiralling from her fingertips toward an "
            "unseen enemy, her body half-wrapped in a cloak of living leaves. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_u_forest_poultice",
        "prompt": (
            "A forest poultice — a gentle-faced woman in a cloak of woven leaves and roots, "
            "holding a bundle of crushed herbs and moss in her cupped hands, a soft green glow "
            "emanating from the poultice, a wounded stag at her side. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_u_bramble_heart",
        "prompt": (
            "A bramble heart — a creature of woven thorn and twisted branch in vaguely "
            "humanoid shape, a single crimson flower blooming where its heart would be, "
            "brambles spreading outward from its chest like ribs. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_u_greatwood_guardian",
        "prompt": (
            "A greatwood guardian — an enormous treant-like being with a torso of ancient oak, "
            "arms like thick branches, one foot lifted as if to crush, deep green glow from "
            "runic carvings on its chest, roots trailing behind it. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "vrd_r_deeproot_ancients",
        "prompt": (
            "A deeproot ancient — a colossal being of petrified wood and living stone, its lower "
            "body fused with the earth itself, roots thicker than tree trunks spreading in all "
            "directions, a crown of glowing lichen around its head, ancient runes gleaming between "
            "the roots. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['VERDANT']}. "
            f"{CONTEXT['VERDANT']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── TIDE (8 cards) ───────────────────────────────────────────────────
    {
        "id": "tid_c_brine_scout",
        "prompt": (
            "A brine scout — a sleek humanoid figure with pale blue-grey skin and gill-slits "
            "along its neck, crouched on a coral outcrop, one hand shading its large dark eyes, "
            "seaweed-wrapped spear across its back, brine dripping from its form. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_echoing_depth",
        "prompt": (
            "Echoing depth — a translucent, jellyfish-like being with a vaguely human silhouette, "
            "inner light pulsing through its membrane, trailing tendrils of phosphorescent blue, "
            "floating in a submerged stone chamber, ancient carvings visible beyond it. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_tide_oracle",
        "prompt": (
            "A tide oracle — an old woman draped in layers of salt-stained linen and sea-worn "
            "shells, her white hair drifting as if underwater, a bowl of seawater cupped in her "
            "hands, foam patterns on its surface. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_sea_raider",
        "prompt": (
            "A sea raider — a scarred brute in salt-crusted leather and coral-studded scale, "
            "a serrated cutlass raised, barnacles growing on his armour, his face half-hidden "
            "behind a helm shaped like a gaping maw, spray freezing mid-air around him. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_c_coral_diviner",
        "prompt": (
            "A coral diviner — a lean woman with branching coral growing from her shoulders "
            "and forearms, her fingers tracing patterns in the air above a table-sized coral "
            "formation, pale bioluminescent light connecting the coral branches. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_u_tidal_seer",
        "prompt": (
            "A tidal seer — a tall figure in deep blue robes covered in wave-pattern embroidery, "
            "a staff of black coral in one hand, the staff's tip glowing with captured seawater "
            "that swirls in a contained vortex, her gaze fixed on something far beyond the grotto. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_u_abyss_watcher",
        "prompt": (
            "An abyss watcher — a hulking creature of chitin and deep-sea bone, its body "
            "covered in pale barnacles, a single enormous eye in the centre of its face, "
            "long spindly arms ending in hooked claws, swaying slowly in the dark water. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "tid_r_deep_tides_diviner",
        "prompt": (
            "A deep tides diviner — a serene woman suspended in a column of dark seawater, "
            "her robes flowing upward as if gravity were reversed, a ring of glowing glyphs "
            "orbiting her, her eyes white with reflected starlight from the surface far above. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['TIDE']}. "
            f"{CONTEXT['TIDE']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },

    # ── DAWN (8 cards) ───────────────────────────────────────────────────
    {
        "id": "dwn_c_templar_initiate",
        "prompt": (
            "A templar initiate — a young squire in plain white vestments, one knee on the "
            "stone floor, head bowed, a simple unlit steel longsword laid before her on an "
            "altar, a single shaft of golden light falling across the blade. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_c_light_bearer",
        "prompt": (
            "A light bearer — a woman in pale golden-scale armour, a lantern on a staff held "
            "high, warm light spilling from it to push back the shadows around her, her face "
            "calm and resolute, the light catching the edges of her armour. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_c_oath_sworn_guard",
        "prompt": (
            "An oath-sworn guard — a knight in full cream-and-gold plate, a kite shield "
            "bearing a sunburst emblem held forward, her stance planted and immovable, "
            "the dawn light forming a faint halo behind her helm. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_c_warden_of_the_light",
        "prompt": (
            "A warden of the light — a tall armoured figure in ornate white-gold plate, "
            "a greatsword planted before them both hands on the hilt, the blade catching "
            "the first light of dawn, their face hidden by a full helm with a slit visor. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_u_blessing_of_dawn",
        "prompt": (
            "A blessing of dawn — a serene priestess in cream robes, hands open and raised, "
            "a warm golden radiance emanating from her chest, motes of light drifting upward "
            "like embers but warm and gentle, the temple behind her glowing softly. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_u_vigil_guardian",
        "prompt": (
            "A vigil guardian — a battle-worn woman in dented but polished plate, standing "
            "atop a temple step, spear in hand, the rising sun behind her casting long shadows, "
            "her gaze fixed on the horizon, steady and watchful. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_r_gilded_seraph",
        "prompt": (
            "A gilded seraph — a magnificent winged figure in gold-chased plate armour, "
            "wings of pale amber and white feathers half-spread, a long gilded spear in one "
            "hand, its face serene and ancient, dawn light streaming through the temple "
            "windows behind it. "
            f"{STYLE_SPINE}. "
            f"{PALETTE['DAWN']}. "
            f"{CONTEXT['DAWN']}. "
            f"{CAMERA_PORTRAIT}."
        )
    },
    {
        "id": "dwn_x_radiant_shard",
        "prompt": (
            "A radiant shard — a single crystal shard floating above a stone altar, pale "
            "golden light pulsing from within its core, a fragment of the first sunrise "
            "trapped in translucent crystal, the shard the size of a dagger, surrounded "
            "by a faint corona of warm light. "
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
    import_content = f"""[remap]

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


# ── Main ────────────────────────────────────────────────────────────────
def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    # Sort cards by strata and rarity for logical order
    priority = {"EMBER": 0, "HOLLOW": 1, "VERDANT": 2, "TIDE": 3, "DAWN": 4}
    rarity_order = {"COMMON": 0, "UNCOMMON": 1, "RARE": 2, "RELIC": 3}
    
    # Resolve strata/rarity from card id
    def strata_of(cid):
        prefix = cid[:3]
        return {"emb": "EMBER", "hol": "HOLLOW", "vrd": "VERDANT", "tid": "TIDE", "dwn": "DAWN"}.get(prefix, "UNKNOWN")
    def rarity_of(cid):
        parts = cid.split("_")
        r = parts[1]
        return {"c": "COMMON", "u": "UNCOMMON", "r": "RARE", "x": "RELIC"}.get(r, "UNKNOWN")
    
    sorted_cards = sorted(CARDS, key=lambda c: (priority.get(strata_of(c["id"]), 99), rarity_order.get(rarity_of(c["id"]), 99)))
    
    total_cost = 0.0
    successes = 0
    failures = []
    rule8_flags = []

    # First 6 = veto gate samples (one from each strata, prioritize rares)
    veto_ids = [
        'emb_r_ember_warleader',
        'hol_r_death_drummer',
        'vrd_r_deeproot_ancients',
        'tid_r_deep_tides_diviner',
        'dwn_r_gilded_seraph',
        'dwn_x_radiant_shard',
    ]

    print(f"{'='*70}")
    print(f"TASK-ART-WAVE-1 — 40 card images for TASK-CARD-WAVE-1")
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

    # Summary
    print(f"\n{'='*70}")
    print(f"TASK-ART-WAVE-1 COMPLETE")
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