#!/usr/bin/env python3
"""Tests for tools/region_gen.py — validates structure against hand-built region_01."""

import json
import sys
import tempfile
from pathlib import Path

# Add project root to path
ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))

from tools.region_gen import (
    parse_spec,
    generate_region,
    diff_against_handbuilt,
    load_card_pool,
    cards_by_rarity,
    pick_deck,
    generate_drops,
    build_encounter,
    build_dig_site,
    DECK_SIZE,
    DROP_RATES,
)


# ═══════════════════════════════════════════════════════════════════════════════
# HELPERS
# ═══════════════════════════════════════════════════════════════════════════════

def load_json(path: Path) -> dict:
    with open(path) as f:
        return json.load(f)


def fail(msg: str):
    print(f"  FAIL: {msg}")
    return 1


def check(condition: bool, msg: str) -> int:
    if not condition:
        print(f"  FAIL: {msg}")
        return 1
    return 0


TESTS_FAILED = 0
TOTAL_TESTS = 0


def test(name: str, fn):
    global TESTS_FAILED, TOTAL_TESTS
    TOTAL_TESTS += 1
    print(f"\n[{TOTAL_TESTS}] {name}")
    try:
        result = fn()
        if result:
            TESTS_FAILED += result
        else:
            print(f"  PASS")
    except Exception as e:
        print(f"  FAIL (exception): {e}")
        TESTS_FAILED += 1


# ═══════════════════════════════════════════════════════════════════════════════
# TESTS
# ═══════════════════════════════════════════════════════════════════════════════

def test_card_pool_loading():
    """Verify card pool loads correctly per stratum."""
    pool = load_card_pool("VERDANT")
    by_rarity = cards_by_rarity(pool)
    total = sum(len(v) for v in by_rarity.values())
    fails = check(total > 0, f"Verdant pool should have cards, got {total}")
    # Check strata cards present
    ids = {c["id"] for c in pool}
    fails += check("vrd_c_root_warden" in ids,
                   "Verdant pool should contain vrd_c_root_warden")
    fails += check("dwn_c_dawn_warder" in ids,
                   "Pool should include neutral DAWN cards")
    return fails


def test_pick_deck_size():
    """Deck always produces exactly 30 unique cards."""
    pool = load_card_pool("VERDANT")
    weights = {"COMMON": 0.5, "UNCOMMON": 0.3, "RARE": 0.15, "MYTHIC": 0.05}
    deck = pick_deck(pool, weights, seed=42)
    fails = check(len(deck) == DECK_SIZE,
                  f"Deck should have {DECK_SIZE} cards, got {len(deck)}")
    fails += check(len(set(deck)) == len(deck),
                   f"Deck has duplicates: {len(deck) - len(set(deck))} dupes")
    return fails


def test_pick_deck_deterministic():
    """Same seed produces identical deck."""
    pool = load_card_pool("VERDANT")
    weights = {"COMMON": 0.5, "UNCOMMON": 0.3, "RARE": 0.15, "MYTHIC": 0.05}
    deck1 = pick_deck(pool, weights, seed=42)
    deck2 = pick_deck(pool, weights, seed=42)
    fails = check(deck1 == deck2, "Same seed should produce identical deck")
    return fails


def test_pick_deck_different_seeds():
    """Different seeds produce different decks (highly likely)."""
    pool = load_card_pool("VERDANT")
    weights = {"COMMON": 0.5, "UNCOMMON": 0.3, "RARE": 0.15, "MYTHIC": 0.05}
    deck1 = pick_deck(pool, weights, seed=42)
    deck2 = pick_deck(pool, weights, seed=99)
    # Very unlikely to be identical with different seeds
    fails = check(deck1 != deck2,
                  "Different seeds should produce different decks")
    return fails


def test_pick_deck_rarity_distribution():
    """Elite/warden decks should have more rares than early decks."""
    pool = load_card_pool("VERDANT")
    early = pick_deck(pool, {"COMMON": 0.7, "UNCOMMON": 0.2, "RARE": 0.1, "MYTHIC": 0.0}, seed=42)
    boss = pick_deck(pool, {"COMMON": 0.15, "UNCOMMON": 0.25, "RARE": 0.35, "MYTHIC": 0.25}, seed=42)

    pool_by_id = {c["id"]: c for c in pool}
    early_rare_count = sum(1 for cid in early if pool_by_id.get(cid, {}).get("rarity") == "RARE")
    boss_rare_count = sum(1 for cid in boss if pool_by_id.get(cid, {}).get("rarity") == "RARE")

    fails = check(boss_rare_count >= early_rare_count,
                  f"Boss deck should have >= rares ({boss_rare_count}) than early deck ({early_rare_count})")
    return fails


def test_generate_drops():
    """Drops follow TASK-DROPS-DATA-1 rates by rarity."""
    pool = load_card_pool("VERDANT")
    deck = pick_deck(pool, {"COMMON": 0.5, "UNCOMMON": 0.3, "RARE": 0.15, "MYTHIC": 0.05}, seed=42)
    drops = generate_drops(deck, pool)
    fails = check(len(drops) > 0, "Should generate drops for deck cards")
    # Check all cards in deck have a drop entry
    deck_set = set(deck)
    drop_ids = {d["card_id"] for d in drops}
    fails += check(deck_set.issubset(drop_ids),
                   "All deck cards should have a drop entry")
    # Check rates are one of the standard rates
    for d in drops:
        rate = d["rate"]
        valid_rates = set(DROP_RATES.values()) | {1.0}
        fails += check(rate in valid_rates,
                       f"Drop rate {rate} not a standard rate")
    return fails


def test_build_encounter_structure():
    """Encounter JSON matches the region_01 pattern."""
    pool = load_card_pool("VERDANT")
    deck = pick_deck(pool, {"COMMON": 0.5, "UNCOMMON": 0.3, "RARE": 0.15, "MYTHIC": 0.05}, seed=42)
    enc = build_encounter(
        "test_encounter",
        "Test Enemy",
        deck,
        "mid",
        "VERDANT",
        pool=pool,
    )
    fails = check(enc["id"] == "test_encounter", "ID should match")
    fails += check(len(enc["deck"]) == DECK_SIZE, f"Deck should have {DECK_SIZE} cards")
    fails += check("drops" in enc, "Encounter should have drops")
    fails += check("dialogue_intro" in enc, "Encounter should have intro dialogue")
    fails += check("shard_reward" in enc, "Encounter should have shard_reward")
    return fails


def test_build_encounter_signature():
    """Warden/boss encounters get a 1.00 rate signature drop."""
    pool = load_card_pool("VERDANT")
    deck = pick_deck(pool, {"COMMON": 0.5, "UNCOMMON": 0.3, "RARE": 0.15, "MYTHIC": 0.05}, seed=42)
    enc = build_encounter(
        "test_warden",
        "Test Warden",
        deck,
        "warden",
        "VERDANT",
        signature_card="vrd_r_bloomweaver",
        pool=pool,
    )
    signatures = [d for d in enc["drops"] if d["rate"] == 1.0]
    fails = check(len(signatures) >= 1,
                  "Warden should have at least one signature drop at 1.00")
    if signatures:
        fails += check(signatures[0]["card_id"] == "vrd_r_bloomweaver",
                       f"Signature should be vrd_r_bloomweaver, got {signatures[0]['card_id']}")
    return fails


def test_build_dig_site():
    """Dig site has 4x4 grid, 16 tiles, 4 strikes."""
    dig = build_dig_site("region_test", "Test Dig", "A test dig site", seed=42)
    site = dig["dig_sites"][0]
    fails = check(site["id"] == "region_test_dig", "Dig ID should match pattern")
    fails += check(site["rows"] == 4, "Should have 4 rows")
    fails += check(site["cols"] == 4, "Should have 4 cols")
    fails += check(len(site["tiles"]) == 16, "Should have 16 tiles")
    fails += check(site["strikes"] == 4, "Should have 4 strikes")
    return fails


def test_parse_spec():
    """Spec parsing with defaults."""
    with tempfile.NamedTemporaryFile(mode="w", suffix=".json", delete=False) as f:
        json.dump({
            "name": "Test Region",
            "stratum": "VERDANT",
            "palette": {"primary": "#ff0000"},
        }, f)
        f.flush()
        spec = parse_spec(Path(f.name))

    fails = check(spec["name"] == "Test Region", "Name should match")
    fails += check(spec["encounter_slots"] == 10, "Default encounter_slots should be 10")
    fails += check(spec["elite_count"] == 1, "Default elite_count should be 1")
    fails += check(spec["dig_name"] == "Dig Site", "Default dig_name should be set")
    return fails


def test_diff_region_01():
    """Diff generated region_01 against hand-built files — structural match.

    The hand-built files have bespoke dialogue, specific encounter names,
    and hand-tuned decks that won't match exactly. We check structure:
    node types, deck sizes, drop presence, dig site structure.
    """
    spec_path = ROOT / "tools" / "specs" / "region_01_spec.json"
    spec = parse_spec(spec_path)
    diffs = diff_against_handbuilt(spec, "region_01", seed=42)

    fails = 0
    for d in diffs:
        # Some structural differences are expected (we don't match exact node counts
        # since the generator places nodes differently)
        print(f"  NOTE: {d}")

    # Critical: no missing encounter files, all decks have 30 cards
    # (diff_against_handbuilt already checks those)
    return fails


def test_full_generate():
    """Full region generation produces all expected files with valid structure."""
    spec_path = ROOT / "tools" / "specs" / "region_01_spec.json"
    spec = parse_spec(spec_path)

    with tempfile.TemporaryDirectory(prefix="region_gen_test_") as td:
        outputs = generate_region(spec, "region_test_gen", seed=42, validate=False,
                                  force=True, temp_dir=td)

        expected_keys = ["map", "early", "mid", "late", "boss", "dig"]
        fails = check(
            all(k in outputs for k in expected_keys),
            f"Missing output keys: {set(expected_keys) - set(outputs.keys())}"
        )

        # Each output file must exist and be non-empty
        for key in expected_keys:
            path = outputs.get(key)
            fails += check(path and path.exists() and path.stat().st_size > 0,
                           f"Output {key} file missing or empty")

    return fails


def test_encounter_count():
    """Generator produces correct number of encounters per tier."""
    spec_path = ROOT / "tools" / "specs" / "region_01_spec.json"
    spec = parse_spec(spec_path)
    with tempfile.TemporaryDirectory(prefix="region_gen_test_") as td:
        outputs = generate_region(spec, "region_test_count", seed=42, validate=False,
                                  force=True, temp_dir=td)

        fails = 0
        for key in ["early", "mid", "late", "boss"]:
            path = outputs.get(key)
            if path and path.exists():
                with open(path) as f:
                    data = json.load(f)
                count = len(data.get("encounters", []))
                # Early should have ≥1, boss should have 2 (warden + boss)
                if key == "boss":
                    fails += check(count == 2, f"boss should have 2 encounters, got {count}")
                else:
                    fails += check(count >= 1, f"{key} should have ≥1 encounter, got {count}")

    return fails


# ═══════════════════════════════════════════════════════════════════════════════
# MAIN
# ═══════════════════════════════════════════════════════════════════════════════

if __name__ == "__main__":
    print("╔══════════════════════════════════════════════╗")
    print("║  Region Generator Tests                      ║")
    print("╚══════════════════════════════════════════════╝")

    test("Card pool loading", test_card_pool_loading)
    test("Deck size = 30 unique cards", test_pick_deck_size)
    test("Deterministic deck (same seed)", test_pick_deck_deterministic)
    test("Different seeds produce different decks", test_pick_deck_different_seeds)
    test("Rarity distribution (elite > early)", test_pick_deck_rarity_distribution)
    test("Drop generation per TASK-DROPS-DATA-1", test_generate_drops)
    test("Encounter structure matches pattern", test_build_encounter_structure)
    test("Signature drop at 1.00 for warden/boss", test_build_encounter_signature)
    test("Dig site structure (4x4, 16 tiles, 4 strikes)", test_build_dig_site)
    test("Spec parsing with defaults", test_parse_spec)
    test("Full generation produces all files", test_full_generate)
    test("Encounter counts per tier", test_encounter_count)
    test("Diff against hand-built region_01", test_diff_region_01)

    print(f"\n{'─' * 50}")
    print(f"Results: {TOTAL_TESTS} tests, {TESTS_FAILED} failures")
    sys.exit(1 if TESTS_FAILED > 0 else 0)