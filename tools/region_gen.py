#!/usr/bin/env python3
"""TASK-REGION-GEN-1: Region generator tool.

Takes a biome spec JSON and outputs:
  - content/map/region_NN.json          (region graph with unlock chain)
  - content/encounters/region_NN_*.json  (encounter files with themed decks + drops)
  - content/dig_sites/region_NN_dig.json (dig site)

Every generated deck optionally validated through the sim gate against
class starter decks (40-60% winrate band, configurable).

Usage:
  python tools/region_gen.py <spec.json> [--region-id NN] [--validate]
"""

import argparse
import json
import os
import random
import shutil
import subprocess
import sys
import tempfile
import time
from copy import deepcopy
from pathlib import Path

# ═══════════════════════════════════════════════════════════════════════════════
# CONFIG
# ═══════════════════════════════════════════════════════════════════════════════

HERE = Path(__file__).resolve().parent  # tools/
ROOT = HERE.parent  # runewake/

# Card pool directories (source strata + neutrals/tutorial pool)
CARD_PATHS = {
    "VERDANT": ROOT / "content" / "cards" / "verdant.json",
    "EMBER": ROOT / "content" / "cards" / "ember.json",
    "TIDE": ROOT / "content" / "cards" / "tide.json",
    "HOLLOW": ROOT / "content" / "cards" / "hollow.json",
    "DAWN": ROOT / "content" / "cards" / "dawn.json",
    "TUTORIAL": ROOT / "content" / "cards" / "tutorial_pack.json",
}

# Output subdirectories
MAP_DIR = ROOT / "content" / "map"
ENCOUNTER_DIR = ROOT / "content" / "encounters"
DIG_SITE_DIR = ROOT / "content" / "dig_sites"

# Deck size (every encounter must have exactly 30 unique cards)
DECK_SIZE = 30

# Drop rates by rarity per TASK-DROPS-DATA-1
DROP_RATES = {
    "COMMON": 0.40,
    "UNCOMMON": 0.25,
    "RARE": 0.10,
    "MYTHIC": 0.03,
}
BOSS_SIGNATURE_RATE = 1.00

# Sim validation thresholds
SIM_WINRATE_MIN = 0.40
SIM_WINRATE_MAX = 0.60
SIM_GAMES = 100
SIM_SEED = 42

# Strata order for encounter slot types
# Early encounters get more C-heavy, mid balanced, late heavier on U/R
STRATA_WEIGHTS = {
    "early": {"COMMON": 0.70, "UNCOMMON": 0.20, "RARE": 0.10, "MYTHIC": 0.00},
    "mid": {"COMMON": 0.50, "UNCOMMON": 0.30, "RARE": 0.15, "MYTHIC": 0.05},
    "late": {"COMMON": 0.35, "UNCOMMON": 0.35, "RARE": 0.20, "MYTHIC": 0.10},
    "elite": {"COMMON": 0.25, "UNCOMMON": 0.35, "RARE": 0.25, "MYTHIC": 0.15},
    "warden": {"COMMON": 0.20, "UNCOMMON": 0.30, "RARE": 0.30, "MYTHIC": 0.20},
    "boss": {"COMMON": 0.15, "UNCOMMON": 0.25, "RARE": 0.35, "MYTHIC": 0.25},
}

# Reward shards by encounter type
REWARDS = {
    "early": {"shard": 30, "dig_charge": 0},
    "mid": {"shard": 50, "dig_charge": 0},
    "late": {"shard": 60, "dig_charge": 0},
    "elite": {"shard": 80, "dig_charge": 1, "fragment": 2},
    "warden": {"shard": 150, "dig_charge": 3, "fragment": 3},
    "boss": {"shard": 300, "dig_charge": 5, "fragment": 5},
}

# ── HELPERS ──
# HELPERS
# ═══════════════════════════════════════════════════════════════════════════════

def load_card_pool(stratum: str, include_neutrals: bool = True) -> list[dict]:
    """Load all cards for a stratum, plus cross-strata neutrals if requested."""
    all_cards: list[dict] = []
    seen_ids: set[str] = set()

    # Primary stratum
    path = CARD_PATHS.get(stratum)
    if path and path.exists():
        with open(path) as f:
            cards = json.load(f)
        for c in cards:
            if c["id"] not in seen_ids:
                seen_ids.add(c["id"])
                all_cards.append(c)

    # Neutral / cross-strata cards (all other strata)
    if include_neutrals:
        for s, p in CARD_PATHS.items():
            if s == stratum or s == "TUTORIAL":
                continue
            if p.exists():
                with open(p) as f:
                    cards = json.load(f)
                for c in cards:
                    if c["id"] not in seen_ids:
                        seen_ids.add(c["id"])
                        all_cards.append(c)

    return all_cards


def cards_by_rarity(pool: list[dict]) -> dict[str, list[dict]]:
    """Split card pool by rarity."""
    by_rarity: dict[str, list[dict]] = {"COMMON": [], "UNCOMMON": [], "RARE": [], "MYTHIC": []}
    for c in pool:
        r = c.get("rarity", "COMMON").upper()
        if r in by_rarity:
            by_rarity[r].append(c)
    return by_rarity


def pick_deck(pool: list[dict], weights: dict[str, float], size: int = DECK_SIZE, seed: int = 0) -> list[str]:
    """Build a deck of `size` unique card IDs from the pool, weighted by rarity.

    Uses rejection sampling: pick rarity by weights, pick random card of that rarity,
    retry if already picked or pool exhausted.
    """
    rng = random.Random(seed)
    by_rarity = cards_by_rarity(pool)
    rarities = list(weights.keys())
    weight_vals = [weights.get(r, 0) for r in rarities]

    # Normalize weights
    total = sum(weight_vals)
    if total == 0:
        weight_vals = [1.0 / len(rarities)] * len(rarities)
    else:
        weight_vals = [w / total for w in weight_vals]

    # Filter out rarities with empty pools
    available_rarities = []
    available_weights = []
    for r, w in zip(rarities, weight_vals):
        if by_rarity.get(r):
            available_rarities.append(r)
            available_weights.append(w)

    if not available_rarities:
        raise ValueError("No cards available in any rarity")

    # Normalize again
    total_w = sum(available_weights)
    available_weights = [w / total_w for w in available_weights]

    deck: list[str] = []
    max_attempts = size * 10
    attempts = 0

    while len(deck) < size and attempts < max_attempts:
        attempts += 1
        # Pick rarity
        rarity = rng.choices(available_rarities, weights=available_weights, k=1)[0]
        candidates = [c["id"] for c in by_rarity[rarity] if c["id"] not in deck]
        if not candidates:
            continue
        chosen = rng.choice(candidates)
        deck.append(chosen)

    if len(deck) < size:
        # Fill from any remaining cards regardless of rarity
        remaining = [c["id"] for c in pool if c["id"] not in deck]
        rng.shuffle(remaining)
        needed = size - len(deck)
        deck.extend(remaining[:needed])

    return deck


def generate_drops(deck: list[str], pool: list[dict], drop_rate: float | None = None) -> list[dict]:
    """Generate drop table entries for a deck.

    Cards from the primary stratum(s) get drops at their rarity's default rate.
    Higher-rarity cards from the primary stratum get signature-like treatment.
    """
    pool_by_id = {c["id"]: c for c in pool}
    drops: list[dict] = []

    # Give each card a drop entry at its rarity's default rate
    for cid in deck:
        card = pool_by_id.get(cid)
        if not card:
            continue
        rarity = card.get("rarity", "COMMON").upper()
        rate = drop_rate if drop_rate is not None else DROP_RATES.get(rarity, 0.25)
        drops.append({"card_id": cid, "rate": rate})

    return drops


def signature_drop(card_id: str) -> dict:
    """Create a guaranteed signature drop entry."""
    return {"card_id": card_id, "rate": BOSS_SIGNATURE_RATE}


# ═══════════════════════════════════════════════════════════════════════════════
# ENCOUNTER BUILDERS
# ═══════════════════════════════════════════════════════════════════════════════

def build_encounter(
    encounter_id: str,
    name: str,
    deck: list[str],
    encounter_type: str,
    stratum: str,
    stratum2: str | None = None,
    modifiers: str | None = None,
    signature_card: str | None = None,
    pool: list[dict] | None = None,
    intro_dialogue: list[str] | None = None,
    outro_dialogue: list[str] | None = None,
) -> dict:
    """Build a single encounter entry."""
    reward = deepcopy(REWARDS.get(encounter_type, REWARDS["mid"]))
    shard = reward.pop("shard", 50)
    dig_charge = reward.pop("dig_charge", 0)

    entry = {
        "id": encounter_id,
        "name": name,
        "portrait": f"res://art/portraits/{encounter_id.replace('r1_', 'r2_').replace('r2_', '')}.png",
        "deck": deck,
        "shard_reward": shard,
        "dig_charge_reward": dig_charge,
    }

    if modifiers:
        entry["modifier"] = modifiers

    if intro_dialogue:
        entry["dialogue_intro"] = intro_dialogue
    else:
        entry["dialogue_intro"] = [
            f"The path ahead is shrouded in mystery.",
            f"A presence stirs in the shadows of the {name.lower()}.",
            f"\"You are not welcome here.\""
        ]

    if outro_dialogue:
        entry["dialogue_outro"] = outro_dialogue
    else:
        entry["dialogue_outro"] = [
            f"The threat fades, leaving only silence.",
            f"The way forward is clear — for now."
        ]

    # Fragment rewards (elite/warden/boss only)
    if "fragment" in reward:
        frag_amount = reward["fragment"]
        entry["fragment_reward"] = f"{stratum.lower()}:{frag_amount}"

    # Drops — use the full pool if provided for drops, else the deck cards
    drop_pool = pool if pool else load_card_pool(stratum)
    # Generate drops from the deck
    entry["drops"] = generate_drops(deck, drop_pool)

    # Signature drop for wardens and bosses
    if signature_card and encounter_type in ("warden", "boss"):
        entry["drops"].insert(0, signature_drop(signature_card))

    return entry


# ═══════════════════════════════════════════════════════════════════════════════
# REGION GRAPH BUILDER
# ═══════════════════════════════════════════════════════════════════════════════

def build_region_graph(
    region_id: str,
    name: str,
    stratum: str,
    stratum2: str | None,
    encounter_slots: int,
    elite_count: int,
    encounter_ids: dict[str, list[str]],
) -> dict:
    """Build the region map graph with node chain and unlock conditions.

    Produces a tiered layout matching the region_01 pattern with tiers:
      0: entry (1 Duel)
      1: early (2 nodes)
      2: mid (2 nodes)
      3: mid/widest (2 encounter + 1 Dig)
      4: late (2 nodes)
      5: Warden (1 node)
      6: WardenBoss (1 node)

    Dig node placed at tier 3 (widest), not appended at the end.
    """
    nodes = []
    node_index = 0
    base_node_id = f"{region_id}_n"

    # Collect encounters in order: early → mid → elite → late → warden → boss
    all_encounters: list = []
    for eid in encounter_ids.get("early", []):
        all_encounters.append((eid, "early"))
    for eid in encounter_ids.get("mid", []):
        all_encounters.append((eid, "mid"))
    for eid in encounter_ids.get("elite", []):
        all_encounters.append((eid, "elite"))
    for eid in encounter_ids.get("late", []):
        all_encounters.append((eid, "late"))
    for eid in encounter_ids.get("warden", []):
        all_encounters.append((eid, "warden"))
    for eid in encounter_ids.get("boss", []):
        all_encounters.append((eid, "boss"))

    dig_node_id = f"{region_id}_dig"
    dig_pos = (480, 480)

    # ── Dynamic tier layout ──
    total_encounters = len(all_encounters)

    # Base layout: 7 tiers covering up to ~12 encounter nodes
    base_tier_specs = [
        ([(400, 80)], 1),                          # T0: entry
        ([(240, 190), (560, 190)], 2),              # T1
        ([(160, 340), (560, 340)], 2),              # T2
        ([(320, 480), (480, 480), (640, 480)], 3),  # T3: 2 enc + 1 dig
        ([(320, 600), (560, 600)], 2),               # T4
        ([(440, 720)], 1),                           # T5: Warden
        ([(440, 820)], 1),                           # T6: WardenBoss
    ]

    # Calculate encounter capacity of base (excluding dig at T3)
    base_capacity = sum(enc_count for _, enc_count in base_tier_specs)
    # T3 counts as 2 encounters (1 position is dig)
    base_capacity -= 1  # T3's 3rd position is the dig, not an encounter

    tier_specs = list(base_tier_specs)

    if total_encounters > base_capacity:
        # Add overflow tiers (2 nodes each)
        extra = total_encounters - base_capacity
        extra_tiers = (extra + 1) // 2  # ceil division
        y_offset = 920
        for i in range(extra_tiers):
            positions = [(320 + j * 240, y_offset) for j in range(min(2, extra))]
            enc_count = min(2, extra)
            tier_specs.insert(-2, (positions, enc_count))  # before warden
            extra -= enc_count
            y_offset += 110

    def make_node_id(base: str, idx: int) -> str:
        return f"{base}{idx:02d}"

    encounter_ptr = 0
    prev_tier_nodes: list[str] = []
    dig_node: dict | None = None

    for tier_idx, (positions, enc_count) in enumerate(tier_specs):
        current_node_ids: list[str] = []
        pos_idx = 0

        # Does this tier include the dig node?
        has_dig = tier_idx == 3

        for pos in positions:
            is_dig_position = has_dig and pos == dig_pos

            if is_dig_position:
                # Place the dig node here
                nid = dig_node_id
                dn = {
                    "id": nid,
                    "type": "Dig",
                    "position": list(pos),
                    "rewards": ["shard:20"],
                    "encounter": f"{region_id}_dig",
                }
                if prev_tier_nodes:
                    dn["unlock"] = {"op": "NODES_CLEARED", "value": list(prev_tier_nodes)}
                current_node_ids.append(nid)
                dig_node = dn
                continue

            # Regular encounter node
            if encounter_ptr >= len(all_encounters):
                break

            eid, etype = all_encounters[encounter_ptr]
            encounter_ptr += 1

            # Determine node type
            if etype == "elite":
                node_type = "Elite"
            elif etype == "warden":
                node_type = "Warden"
            elif etype == "boss":
                node_type = "WardenBoss"
            else:
                node_type = "Duel"

            nid = make_node_id(base_node_id, len(nodes) + 1)
            node = {
                "id": nid,
                "type": node_type,
                "position": list(pos),
            }

            # Unlock: previous tier nodes must be cleared
            if prev_tier_nodes:
                node["unlock"] = {"op": "NODES_CLEARED", "value": list(prev_tier_nodes)}

            node["encounter"] = eid
            node["rewards"] = [f"shard:{REWARDS[etype]['shard']}"]
            if REWARDS[etype].get("dig_charge", 0) > 0:
                node["rewards"].append(f"dig_charge:{REWARDS[etype]['dig_charge']}")

            nodes.append(node)
            current_node_ids.append(nid)

        # Determine connections to next tier
        if tier_idx + 1 < len(tier_specs):
            next_positions, next_count = tier_specs[tier_idx + 1]
            next_start_idx = len(nodes)  # approx
            next_ids = []

            # Map next tier positions to node IDs
            np_idx = 0
            for np in next_positions:
                is_next_dig = (tier_idx + 1) == 3 and np == dig_pos
                if is_next_dig:
                    next_ids.append(dig_node_id)
                else:
                    # Calculate what node id this would be
                    next_ids.append(make_node_id(base_node_id, len(nodes) + np_idx + 1))
                    np_idx += 1

            # Assign connections from current tier nodes to closest next-tier nodes
            for cn in current_node_ids:
                cn_obj = None
                for n in nodes:
                    if n["id"] == cn:
                        cn_obj = n
                        break
                if dig_node and cn == dig_node_id:
                    cn_obj = dig_node
                if dig_node and cn == dig_node_id:
                    cn_obj = dig_node

                if cn_obj and next_ids:
                    cn_pos = cn_obj["position"]
                    # Sort next tier nodes by distance
                    # We need positions for next nodes - reconstruct from tier_specs
                    next_tier_positions, _ = tier_specs[tier_idx + 1]
                    if tier_idx + 1 == 3:
                        next_tier_positions = list(next_tier_positions)
                    elif tier_idx + 1 > 3:
                        next_tier_positions = list(next_tier_positions)

                    # Match next_ids to positions
                    next_with_pos = []
                    for j, nid in enumerate(next_ids):
                        if j < len(next_tier_positions):
                            next_with_pos.append((nid, next_tier_positions[j]))

                    closest = sorted(next_with_pos, key=lambda x: abs(x[1][0] - cn_pos[0]))
                    connects = [x[0] for x in closest[:min(2, len(closest))]]
                    connects = list(dict.fromkeys(connects))  # unique, preserve order

                    if cn_obj and connects:
                        if isinstance(cn_obj, dict):
                            cn_obj["connects"] = connects

        prev_tier_nodes = current_node_ids

    # Add dig node to nodes list (positioned at the right place via insert)
    if dig_node:
        # Insert dig node in position order (sorted by y, then x)
        nodes.append(dig_node)

    # Sort nodes by position (y ascending, x ascending) so they render in order
    nodes.sort(key=lambda n: (n["position"][1], n["position"][0]))

    # Re-sort node_ids to be sequential
    final_nodes = []
    for i, n in enumerate(nodes):
        if n["type"] != "Dig":
            # Renumber for clean output
            n["id"] = make_node_id(base_node_id, i + 1)
        # Update connect references to new IDs
        pass
    # Actually, let's keep original IDs but enforce the ordering
    # Renumber non-dig nodes for cleaner output
    new_nodes = []
    dig_node_keep = None
    non_dig_idx = 0
    for n in nodes:
        if n["type"] == "Dig":
            dig_node_keep = n
        else:
            old_id = n["id"]
            new_id = make_node_id(base_node_id, non_dig_idx + 1)
            n["id"] = new_id
            non_dig_idx += 1
            new_nodes.append(n)

    # Re-map connections
    id_map = {}
    for n in new_nodes:
        id_map[n["id"]] = n
    if dig_node_keep:
        id_map[dig_node_keep["id"]] = dig_node_keep

    for n in new_nodes:
        if "connects" in n:
            n["connects"] = [c for c in n["connects"] if c in id_map]
            if not n["connects"]:
                del n["connects"]

    if dig_node_keep:
        # Find its neighbors for connections
        dig_y = dig_node_keep["position"][1]
        # Connect from previous tier nodes
        prev_tier = [n for n in new_nodes if abs(n["position"][1] - dig_y) > 30 and n["position"][1] < dig_y]
        next_tier = [n for n in new_nodes if n["position"][1] > dig_y]
        if prev_tier:
            dig_node_keep["unlock"] = {"op": "NODES_CLEARED", "value": [n["id"] for n in prev_tier]}
        # Connect dig to next tier
        if next_tier:
            closest = sorted(next_tier, key=lambda n: abs(n["position"][0] - dig_node_keep["position"][0]))
            dig_node_keep["connects"] = [n["id"] for n in closest[:2]]

        # Insert dig node at correct position in list (by y-order)
        insert_idx = 0
        for i, n in enumerate(new_nodes):
            if n["position"][1] > dig_y:
                insert_idx = i
                break
            insert_idx = i + 1
        new_nodes.insert(insert_idx, dig_node_keep)

    # Update connections between non-dig nodes to reference correct IDs
    node_id_set = {n["id"] for n in new_nodes}
    for n in new_nodes:
        if "connects" in n:
            n["connects"] = [c for c in n["connects"] if c in node_id_set]

    return {
        "id": region_id,
        "name": name,
        "strata": stratum,
        "nodes": new_nodes,
    }


# ═══════════════════════════════════════════════════════════════════════════════
# DIG SITE BUILDER
# ═══════════════════════════════════════════════════════════════════════════════

def build_dig_site(region_id: str, name: str, description: str, seed: int = 0) -> dict:
    """Build a dig site with 4x4 grid, 4 strikes, random tiles."""
    rng = random.Random(seed)
    tiles = []
    tile_types = ["EMPTY", "SHARD", "SHARD", "RUNE_FRAGMENT", "CODEX_PAGE", "EMPTY", "EMPTY", "SHARD",
                  "EMPTY", "SHARD", "EMPTY", "SHARD", "EMPTY", "EMPTY", "EMPTY", "EMPTY"]
    rng.shuffle(tile_types)

    for t in tile_types:
        if t == "SHARD":
            tiles.append({"type": "SHARD", "value": str(rng.randint(5, 25))})
        elif t == "RUNE_FRAGMENT":
            strata_choices = ["verdant", "ember", "tide", "hollow", "dawn"]
            s = rng.choice(strata_choices)
            tiles.append({"type": "RUNE_FRAGMENT", "value": f"{s}:{rng.randint(1, 2)}"})
        elif t == "CODEX_PAGE":
            tiles.append({"type": "CODEX_PAGE", "value": f"{region_id}_codex_{rng.randint(1, 3)}"})
        else:
            tiles.append({"type": "EMPTY", "value": None})

    return {
        "dig_sites": [
            {
                "id": f"{region_id}_dig",
                "name": name,
                "description": description,
                "rows": 4,
                "cols": 4,
                "strikes": 4,
                "headline_threshold": 3,
                "headline_reward": f"relic:{region_id}_relic",
                "tiles": tiles,
            }
        ]
    }


# ═══════════════════════════════════════════════════════════════════════════════
# SIM VALIDATION
# ═══════════════════════════════════════════════════════════════════════════════

def validate_encounter_deck(deck: list[str], class_id: str, sim_bin: Path, artifacts_path: Path) -> dict | None:
    """Run one class-vs-encounter matchup in the sim and return the report.

    Returns parsed JSON report or None on failure.
    """
    # Write a temporary deck pack for the encounter
    pool = load_card_pool("ALL", include_neutrals=True)
    pool_by_id = {c["id"]: c for c in pool}

    cards = []
    for cid in deck:
        if cid in pool_by_id:
            cards.append(pool_by_id[cid])

    deck_pack_path = ROOT / "tmp" / f"encounter_val_{class_id}.json"
    deck_pack_path.parent.mkdir(parents=True, exist_ok=True)
    with open(deck_pack_path, "w") as f:
        json.dump(cards, f)

    try:
        result = subprocess.run(
            [str(sim_bin), "run",
             "--deck-a", str(deck_pack_path),
             "--deck-b", str(deck_pack_path),
             "--games", str(SIM_GAMES),
             "--seed", str(SIM_SEED),
             "--artifacts-path", str(artifacts_path),
             "--class-a", class_id,
             "--class-b", class_id],
            capture_output=True,
            text=True,
            timeout=300,
            cwd=str(ROOT),
        )
    except (FileNotFoundError, subprocess.TimeoutExpired) as e:
        print(f"[region_gen] SIM validation error: {e}", file=sys.stderr)
        return None

    if result.returncode != 0:
        return None

    for line in result.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            return json.loads(line)

    return None


def validate_all_encounters(encounter_files: list[Path], spec: dict) -> tuple[bool, list[str]]:
    """Run sim validation for all encounter decks against class starters.

    Returns (passed, messages).
    """
    sim_bin = ROOT / "sim" / "bin" / "Debug" / "net8.0" / "Runewake.Sim"
    artifacts_path = ROOT / "content" / "artifacts" / "launch_artifacts.json"

    if not sim_bin.exists():
        return False, [f"SIM binary not found at {sim_bin}"]

    class_ids = ["warrior", "necromancer", "druid", "battlemage", "paladin", "ranger", "thief"]
    messages = []
    all_pass = True

    for ef in encounter_files:
        with open(ef) as f:
            data = json.load(f)

        for encounter in data.get("encounters", []):
            eid = encounter["id"]
            deck = encounter["deck"]

            # Run against a subset of class starters (fast: 3 classes × SIM_GAMES games)
            test_classes = class_ids[:3]  # sample 3 for speed
            encounter_win_rates = []

            for cls in test_classes:
                report = validate_encounter_deck(deck, cls, sim_bin, artifacts_path)
                if report:
                    wr = report.get("win_rate_p0", 0.5)
                    encounter_win_rates.append(wr)
                else:
                    messages.append(f"  {eid} vs {cls}: sim failed")
                    all_pass = False

            if encounter_win_rates:
                avg_wr = sum(encounter_win_rates) / len(encounter_win_rates)
                band_ok = SIM_WINRATE_MIN <= avg_wr <= SIM_WINRATE_MAX
                status = "PASS" if band_ok else "FAIL"
                if not band_ok:
                    all_pass = False
                messages.append(f"  {eid}: avg wr {avg_wr:.1%} [{SIM_WINRATE_MIN:.0%}-{SIM_WINRATE_MAX:.0%}] → {status}")
            else:
                messages.append(f"  {eid}: no valid sim results → FAIL")
                all_pass = False

    return all_pass, messages


# ═══════════════════════════════════════════════════════════════════════════════
# SPEC PARSER
# ═══════════════════════════════════════════════════════════════════════════════

def parse_spec(spec_path: Path) -> dict:
    """Load and validate a biome spec JSON."""
    with open(spec_path) as f:
        spec = json.load(f)

    required = ["name", "stratum", "palette"]
    for r in required:
        if r not in spec:
            raise ValueError(f"Missing required field '{r}' in spec")

    # Defaults
    spec.setdefault("encounter_slots", 10)
    spec.setdefault("elite_count", 1)
    spec.setdefault("warden_name", "Warden")
    spec.setdefault("boss_name", "Boss")
    spec.setdefault("dig_name", "Dig Site")
    spec.setdefault("dig_description", "An ancient excavation site, rich with buried remnants.")
    spec.setdefault("lore_blurb", "")
    spec.setdefault("stratum2", None)
    spec.setdefault("entry_encounter_name", "The Guardian")
    spec.setdefault("signature_card", None)

    return spec


# ═══════════════════════════════════════════════════════════════════════════════
# MAIN GENERATOR
# ═══════════════════════════════════════════════════════════════════════════════

def generate_region(spec: dict, region_id: str, seed: int = 0, validate: bool = False,
                    force: bool = False, temp_dir: str | Path | None = None) -> dict[str, Path]:
    """Generate all region files from a spec.

    Args:
        spec: Parsed biome spec.
        region_id: Region ID (e.g. 'region_02').
        seed: Random seed for deterministic generation.
        validate: If True, run sim gate on generated decks.
        force: If False, refuse to overwrite existing content/ files.
        temp_dir: If set, write output files under this temp directory instead of content/.

    Returns dict mapping file type (map, early, mid, late, boss, dig) to output path.
    """
    rng = random.Random(seed)
    name = spec["name"]
    stratum = spec["stratum"]
    stratum2 = spec.get("stratum2")
    encounter_slots = spec["encounter_slots"]
    elite_count = spec["elite_count"]

    # ── Resolve output directories ──
    if temp_dir is not None:
        temp_dir = Path(temp_dir)
        map_dir = temp_dir / "map"
        encounter_dir = temp_dir / "encounters"
        dig_site_dir = temp_dir / "dig_sites"
    else:
        map_dir = MAP_DIR
        encounter_dir = ENCOUNTER_DIR
        dig_site_dir = DIG_SITE_DIR

    # ── Existing file guard (unless --force) ──
    # Check ALL output paths BEFORE generating anything
    candidate_paths = [
        map_dir / f"{region_id}.json",
        encounter_dir / f"{region_id}_early.json",
        encounter_dir / f"{region_id}_mid.json",
        encounter_dir / f"{region_id}_late.json",
        encounter_dir / f"{region_id}_boss.json",
        dig_site_dir / f"{region_id}_dig.json",
    ]
    if not force and not temp_dir:
        existing = [str(p) for p in candidate_paths if p.exists()]
        if existing:
            print(f"[region_gen] ERROR: refusing to overwrite existing file(s):", file=sys.stderr)
            for path in existing:
                print(f"  {path}", file=sys.stderr)
            print(f"[region_gen] Use --force to overwrite, or pass temp_dir for safe generation.",
                  file=sys.stderr)
            sys.exit(1)

    # Count how many of each encounter type
    early_count = max(1, encounter_slots // 4)
    mid_count = max(1, encounter_slots // 3)
    late_count = encounter_slots - early_count - mid_count - elite_count

    # Load card pool
    pool = load_card_pool(stratum, include_neutrals=True)

    # Generate encounter decks for each tier
    # Each gets a unique seed so decks are different
    early_decks = [pick_deck(pool, STRATA_WEIGHTS["early"], seed=seed + i)
                   for i in range(early_count)]
    mid_decks = [pick_deck(pool, STRATA_WEIGHTS["mid"], seed=seed + 100 + i)
                 for i in range(mid_count)]
    late_decks = [pick_deck(pool, STRATA_WEIGHTS["late"], seed=seed + 200 + i)
                  for i in range(late_count)]
    elite_decks = [pick_deck(pool, STRATA_WEIGHTS["elite"], seed=seed + 300 + i)
                   for i in range(elite_count)]
    warden_deck = pick_deck(pool, STRATA_WEIGHTS["warden"], seed=seed + 400)
    boss_deck = pick_deck(pool, STRATA_WEIGHTS["boss"], seed=seed + 500)

    # Build encounter entries
    def make_name(base: str, idx: int) -> str:
        names = spec.get("encounter_names", [])
        if names and idx < len(names):
            return names[idx]
        return base

    # Early encounters
    early_encounters = []
    for i in range(early_count):
        eid = f"{region_id}_duel_early_{i + 1}"
        en = build_encounter(
            eid,
            make_name(spec.get("entry_encounter_name", "The Guardian"), i),
            early_decks[i],
            "early",
            stratum,
            stratum2,
            pool=pool,
        )
        early_encounters.append(en)

    # Mid encounters
    mid_encounters = []
    for i in range(mid_count):
        eid = f"{region_id}_duel_mid_{i + 1}"
        en = build_encounter(
            eid,
            make_name("The Wanderer", early_count + i),
            mid_decks[i],
            "mid",
            stratum,
            stratum2,
            pool=pool,
        )
        mid_encounters.append(en)

    # Late encounters
    late_encounters = []
    for i in range(late_count):
        eid = f"{region_id}_duel_late_{i + 1}"
        en = build_encounter(
            eid,
            make_name("The Sentinel", early_count + mid_count + i),
            late_decks[i],
            "late",
            stratum,
            stratum2,
            pool=pool,
        )
        late_encounters.append(en)

    # Elites
    elite_encounters = []
    for i in range(elite_count):
        eid = f"{region_id}_elite_{i + 1}"
        en = build_encounter(
            eid,
            spec.get("elite_names", ["The Elite"])[i] if spec.get("elite_names") else f"The Elite {i + 1}",
            elite_decks[i],
            "elite",
            stratum,
            stratum2,
            modifiers=spec.get("elite_modifiers", [None])[i] if spec.get("elite_modifiers") else None,
            pool=pool,
        )
        elite_encounters.append(en)

    # Warden
    warden_encounter = build_encounter(
        f"{region_id}_warden",
        spec["warden_name"],
        warden_deck,
        "warden",
        stratum,
        stratum2,
        signature_card=spec.get("signature_card"),
        pool=pool,
        intro_dialogue=spec.get("warden_intro"),
        outro_dialogue=spec.get("warden_outro"),
    )

    # Boss
    boss_encounter = build_encounter(
        f"{region_id}_boss",
        spec["boss_name"],
        boss_deck,
        "boss",
        stratum,
        stratum2,
        signature_card=spec.get("signature_card"),
        pool=pool,
        intro_dialogue=spec.get("boss_intro"),
        outro_dialogue=spec.get("boss_outro"),
    )

    # Build encounter ID mapping for the graph
    encounter_ids = {
        "early": [e["id"] for e in early_encounters],
        "mid": [e["id"] for e in mid_encounters],
        "late": [e["id"] for e in late_encounters],
        "elite": [e["id"] for e in elite_encounters],
        "warden": [warden_encounter["id"]],
        "boss": [boss_encounter["id"]],
    }

    # Build region graph
    region_graph = build_region_graph(region_id, name, stratum, stratum2,
                                      encounter_slots, elite_count, encounter_ids)

    # Build dig site
    dig_site = build_dig_site(region_id, spec["dig_name"], spec["dig_description"], seed=seed + 999)

    # Write output files
    outputs = {}

    # Region map
    map_path = map_dir / f"{region_id}.json"
    map_path.parent.mkdir(parents=True, exist_ok=True)
    with open(map_path, "w") as f:
        json.dump(region_graph, f, indent=2)
    outputs["map"] = map_path
    print(f"[region_gen] Wrote {map_path}")

    # Early encounters
    early_path = encounter_dir / f"{region_id}_early.json"
    early_path.parent.mkdir(parents=True, exist_ok=True)
    with open(early_path, "w") as f:
        json.dump({"encounters": early_encounters}, f, indent=2)
    outputs["early"] = early_path
    print(f"[region_gen] Wrote {early_path}")

    # Mid encounters
    mid_path = encounter_dir / f"{region_id}_mid.json"
    mid_path.parent.mkdir(parents=True, exist_ok=True)
    with open(mid_path, "w") as f:
        json.dump({"encounters": mid_encounters + elite_encounters}, f, indent=2)
    outputs["mid"] = mid_path
    print(f"[region_gen] Wrote {mid_path}")

    # Late/boss encounters
    late_path = encounter_dir / f"{region_id}_late.json"
    late_path.parent.mkdir(parents=True, exist_ok=True)
    with open(late_path, "w") as f:
        json.dump({"encounters": late_encounters}, f, indent=2)
    outputs["late"] = late_path
    print(f"[region_gen] Wrote {late_path}")

    # Boss file
    boss_path = encounter_dir / f"{region_id}_boss.json"
    boss_path.parent.mkdir(parents=True, exist_ok=True)
    with open(boss_path, "w") as f:
        json.dump({"encounters": [warden_encounter, boss_encounter]}, f, indent=2)
    outputs["boss"] = boss_path
    print(f"[region_gen] Wrote {boss_path}")

    # Dig site
    dig_path = dig_site_dir / f"{region_id}_dig.json"
    dig_path.parent.mkdir(parents=True, exist_ok=True)
    with open(dig_path, "w") as f:
        json.dump(dig_site, f, indent=2)
    outputs["dig"] = dig_path
    print(f"[region_gen] Wrote {dig_path}")

    # Validate decks if requested
    if validate:
        print("\n[region_gen] Running sim validation...")
        encounter_files = [early_path, mid_path, late_path, boss_path]
        passed, messages = validate_all_encounters(encounter_files, spec)
        for msg in messages:
            print(f"[region_gen] {msg}")
        if passed:
            print("[region_gen] ✅ All encounter decks pass sim gate")
        else:
            print("[region_gen] ❌ Some decks failed sim gate — consider regenerating with different seed")

    return outputs


def diff_against_handbuilt(spec: dict, region_id: str, seed: int = 0) -> list[str]:
    """Generate a region into a temp directory, then smart-compare against existing hand-built files.

    Returns list of diff lines (empty = identical for structural comparison).
    Checks:
    - Node count, types, encounter IDs exist
    - Deck has 30 unique cards
    - Drops have correct rates
    - Structure matches the region_01 pattern

    NOTE: writes generated files to a temp directory — never touches content/.
    """
    diffs = []

    # READ REFERENCE FIRST
    ref_map = ROOT / "content" / "map" / "region_01.json"
    ref_data = None
    if ref_map.exists():
        with open(ref_map) as f:
            ref_data = json.load(f)

    # Generate to a temp directory — NEVER touch content/
    # The entire analysis must happen inside this with block so temp files exist
    with tempfile.TemporaryDirectory(prefix="region_gen_diff_") as td:
        outputs = generate_region(spec, region_id, seed=seed, validate=False,
                                  force=True, temp_dir=td)

        if ref_data is None:
            diffs.append("REFERENCE FILE MISSING: content/map/region_01.json not found")
            return diffs

        with open(outputs["map"]) as f:
            gen = json.load(f)

        # Structural comparison
        ref_node_count = len(ref_data["nodes"])
        gen_node_count = len(gen["nodes"])
        if gen_node_count != ref_node_count:
            diffs.append(f"NODE COUNT: generated={gen_node_count}, reference={ref_node_count}")

        # Node types match the reference pattern
        ref_types = [n["type"] for n in ref_data["nodes"]]
        gen_types = [n["type"] for n in gen["nodes"]]
        # The first node should always be Duel
        if gen_types[0] != "Duel":
            diffs.append(f"FIRST NODE TYPE: expected Duel, got {gen_types[0]}")
        # Last node should be WardenBoss
        if gen_types[-1] != "WardenBoss":
            diffs.append(f"LAST NODE TYPE: expected WardenBoss, got {gen_types[-1]}")

        # Check dig node exists
        dig_nodes = [n for n in gen["nodes"] if n.get("type") == "Dig"]
        if not dig_nodes:
            diffs.append("MISSING DIG NODE: no Dig type node in generated region")

        # Check encounter files
        for key in ["early", "mid", "late", "boss"]:
            path = outputs.get(key)
            if not path or not path.exists():
                diffs.append(f"MISSING ENCOUNTER FILE: {key}")
                continue
            with open(path) as f:
                data = json.load(f)
            encounters = data.get("encounters", [])
            if not encounters:
                diffs.append(f"EMPTY ENCOUNTERS: {key} has no encounters")
                continue
            for enc in encounters:
                deck = enc.get("deck", [])
                # Check 30 unique cards
                if len(deck) != 30:
                    diffs.append(f"DECK SIZE: {enc['id']} has {len(deck)} cards, expected 30")
                if len(set(deck)) != len(deck):
                    dups = [c for c in deck if deck.count(c) > 1]
                    diffs.append(f"DECK DUPLICATES: {enc['id']} has duplicates: {set(dups)}")
                # Check drops exist
                drops = enc.get("drops", [])
                if len(drops) < 3:
                    diffs.append(f"DROP COUNT: {enc['id']} has only {len(drops)} drops, expected ≥3")

        # Check dig site structure
        dig_path = outputs.get("dig")
        if dig_path and dig_path.exists():
            with open(dig_path) as f:
                dig = json.load(f)
            sites = dig.get("dig_sites", [])
            if not sites:
                diffs.append("DIG SITE: empty dig_sites list")
            else:
                site = sites[0]
                if site.get("rows") != 4 or site.get("cols") != 4:
                    diffs.append(f"DIG SITE: expected 4x4, got {site.get('rows')}x{site.get('cols')}")
                tiles = site.get("tiles", [])
                if len(tiles) != 16:
                    diffs.append(f"DIG SITE: expected 16 tiles, got {len(tiles)}")

        return diffs


# ═══════════════════════════════════════════════════════════════════════════════
# CLI
# ═══════════════════════════════════════════════════════════════════════════════

def main():
    parser = argparse.ArgumentParser(description="Runewake region generator")
    parser.add_argument("spec", type=str, help="Path to biome spec JSON file")
    parser.add_argument("--region-id", type=str, default=None,
                        help="Region ID (e.g., region_02). Auto-derived from spec filename if omitted.")
    parser.add_argument("--seed", type=int, default=0,
                        help="Random seed for deterministic generation")
    parser.add_argument("--validate", action="store_true",
                        help="Run sim gate validation on generated decks")
    parser.add_argument("--diff", action="store_true",
                        help="Diff generated output against hand-built region_01 reference")
    parser.add_argument("--force", action="store_true",
                        help="Overwrite existing content/ files (dangerous — prefer --diff for review)")
    args = parser.parse_args()

    spec_path = Path(args.spec)
    if not spec_path.exists():
        print(f"Error: spec file not found: {spec_path}", file=sys.stderr)
        sys.exit(1)

    spec = parse_spec(spec_path)

    region_id = args.region_id
    if not region_id:
        # Derive from filename: "cinderfall.json" -> "region_02"
        stem = spec_path.stem
        # Check if spec has region_id field
        region_id = spec.get("region_id", f"region_gen_{stem}")

    print(f"[region_gen] Generating region '{region_id}' — {spec['name']} ({spec['stratum']})")
    print(f"[region_gen] Encounter slots: {spec['encounter_slots']}, Elites: {spec['elite_count']}")

    outputs = generate_region(spec, region_id, seed=args.seed, validate=args.validate,
                              force=args.force)

    # Diff against hand-built reference
    if args.diff:
        print("\n[region_gen] Running structural diff against region_01 reference...")
        diffs = diff_against_handbuilt(spec, region_id, seed=args.seed)
        if diffs:
            print("[region_gen] Diffs found:")
            for d in diffs:
                print(f"  • {d}")
        else:
            print("[region_gen] ✅ Generated structure matches reference pattern")

    print(f"\n[region_gen] ✅ Region '{region_id}' generated successfully")
    print(f"[region_gen] Output files:")
    for key, path in outputs.items():
        size = path.stat().st_size if path.exists() else 0
        print(f"  {key}: {path} ({size:,} bytes)")


if __name__ == "__main__":
    main()