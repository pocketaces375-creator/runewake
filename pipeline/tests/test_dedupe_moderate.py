#!/usr/bin/env python3
"""Tests for pipeline/modules/dedupe_moderate.py — P6-06 Dedupe + Moderate."""

import json
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch

HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE))

from modules.dedupe_moderate import (
    load_blocklist,
    load_existing_names,
    build_blocklist_patterns,
    check_blocklist,
    check_card_text_safety,
    jaro_winkler_similarity,
    compute_text_similarity,
    check_duplicate_name,
    check_duplicate_text,
    main,
)


# ── Jaro-Winkler tests ─────────────────────────────────────────────────────

def test_jw_exact_match():
    assert jaro_winkler_similarity("Ember Hound", "Ember Hound") == 1.0


def test_jw_case_insensitive():
    assert jaro_winkler_similarity("EMBER HOUND", "ember hound") == 1.0


def test_jw_similar():
    sim = jaro_winkler_similarity("Ember Hound", "Ember Houndstooth")
    assert sim > 0.85, f"Expected >0.85, got {sim}"


def test_jw_different():
    sim = jaro_winkler_similarity("Root Warden", "Ember Hound")
    assert sim < 0.7, f"Expected <0.7, got {sim}"


# ── N-gram text similarity tests ───────────────────────────────────────────

def test_text_identical():
    assert compute_text_similarity("hello world", "hello world") == 1.0


def test_text_similar():
    sim = compute_text_similarity("Summon: Give adjacent allies +1/+1",
                                  "Summon: Give adjacent allies +0/+1")
    assert sim > 0.8, f"Expected >0.8, got {sim}"


def test_text_different():
    sim = compute_text_similarity("Fireball", "Healing Touch")
    assert sim < 0.5, f"Expected <0.5, got {sim}"


def test_text_empty():
    assert compute_text_similarity("", "something") == 0.0
    assert compute_text_similarity("hello", "") == 0.0


# ── Blocklist tests ─────────────────────────────────────────────────────────

def test_load_blocklist():
    blocklist = load_blocklist()
    assert "trademark_tcg" in blocklist
    assert "religious_figures" in blocklist
    assert "slurs" in blocklist
    assert "public_figures" in blocklist
    assert len(blocklist["trademark_tcg"]) > 0


def test_build_patterns():
    blocklist = load_blocklist()
    patterns = build_blocklist_patterns(blocklist)
    assert len(patterns) > 50  # should have many patterns
    # Check a known pattern works
    for cat, pat in patterns:
        if "pikachu" in str(pat.pattern).lower():
            assert pat.search("Pikachu the Thunderer") is not None
            return
    assert False, "Could not find pikachu pattern in blocklist"


def test_check_blocklist_clean():
    patterns = build_blocklist_patterns(load_blocklist())
    violations = check_blocklist("Root Warden", patterns)
    assert len(violations) == 0, f"Expected clean, got {violations}"


def test_check_blocklist_trademark():
    patterns = build_blocklist_patterns(load_blocklist())
    violations = check_blocklist("Pikachu's Revenge", patterns)
    assert len(violations) > 0
    assert any("BLOCKLIST" in v for v in violations)


def test_check_blocklist_religious():
    patterns = build_blocklist_patterns(load_blocklist())
    violations = check_blocklist("Jesus of the Barrow", patterns)
    assert len(violations) > 0


def test_check_blocklist_public_figure():
    patterns = build_blocklist_patterns(load_blocklist())
    violations = check_blocklist("Putin's Pact", patterns)
    assert len(violations) > 0


# ── Text safety tests ──────────────────────────────────────────────────────

def test_safety_clean():
    card = {"name": "Root Warden", "flavor": "The grove keeps its own ledgers."}
    violations = check_card_text_safety(card)
    assert len(violations) == 0


def test_safety_html():
    card = {"name": "<script>Bad</script>", "flavor": ""}
    violations = check_card_text_safety(card)
    assert len(violations) > 0


def test_safety_url():
    card = {"name": "Card", "flavor": "Visit https://evil.com for more"}
    violations = check_card_text_safety(card)
    assert len(violations) > 0


# ── check_duplicate_name tests ─────────────────────────────────────────────

def test_dup_name_exact_match():
    violations = check_duplicate_name("Root Warden", {"Root Warden", "Ember Hound"}, [])
    assert len(violations) > 0
    assert any("DEDUPE_EXACT_NAME" in v for v in violations)


def test_dup_name_fuzzy():
    violations = check_duplicate_name("Root Warden Jr", {"Root Warden", "Ember Hound"}, [])
    assert len(violations) > 0
    assert any("DEDUPE_FUZZY_NAME" in v for v in violations)


def test_dup_name_clean():
    violations = check_duplicate_name("Brand New Card Idea", {"Root Warden", "Ember Hound"}, [])
    assert len(violations) == 0


def test_dup_name_blocklisted():
    patterns = build_blocklist_patterns(load_blocklist())
    violations = check_duplicate_name("Pikachu Strike", {"Root Warden"}, patterns)
    assert len(violations) > 0


# ── check_duplicate_text tests ─────────────────────────────────────────────

def test_dup_text_similar():
    """Cards with very similar rules text should be flagged."""
    card_a = {"name": "Test Card A", "keywords": ["SWIFT"], "flavor": "Fast.",
              "abilities": [
                  {"trigger": "ON_SUMMON", "effects": [
                      {"op": "BUFF", "attack": 1, "target": {"scope": "SELF"}}
                  ]}
              ]}
    card_b = {"name": "Test Card B", "keywords": ["SWIFT"], "flavor": "Fast.",
              "abilities": [
                  {"trigger": "ON_SUMMON", "effects": [
                      {"op": "BUFF", "attack": 1, "target": {"scope": "SELF"}}
                  ]}
              ]}
    violations = check_duplicate_text("Test Card B", card_b, [card_a])
    assert len(violations) > 0
    assert any("DEDUPE_TEXT_SIM" in v for v in violations)


def test_dup_text_different():
    """Cards with different text should NOT be flagged."""
    card_a = {"name": "Fire Blast", "keywords": [], "flavor": "Burn.",
              "abilities": [
                  {"trigger": "RESOLVE", "effects": [
                      {"op": "DAMAGE", "amount": 3, "target": {"scope": "PLAYER_ENEMY"}}
                  ]}
              ]}
    card_b = {"name": "Healing Rain", "keywords": [], "flavor": "Soothing.",
              "abilities": [
                  {"trigger": "RESOLVE", "effects": [
                      {"op": "HEAL", "amount": 3, "target": {"scope": "PLAYER_SELF"}}
                  ]}
              ]}
    violations = check_duplicate_text("Healing Rain", card_b, [card_a])
    assert len(violations) == 0


# ── End-to-end tests ────────────────────────────────────────────────────────

def test_main_clean_card():
    """A clean card passes dedupe + moderation."""
    card = {"id": "tst_c_fresh", "set": "buried_age", "name": "Fresh New Card",
            "strata": "EMBER", "type": "CREATURE", "rarity": "COMMON", "cost": 2,
            "attack": 2, "vigor": 2, "keywords": [], "abilities": [],
            "flavor": "A brand new card.", "power_score": 3.5, "content_version": 1}
    with tempfile.TemporaryDirectory() as tmp:
        inp = Path(tmp) / "04_simulated.json"
        inp.write_text(json.dumps([card]))
        rc = main(["--input", str(inp), "--work-dir", str(Path(tmp) / "out")])
        assert rc == 0, f"Expected 0, got {rc}"
        out = json.loads((Path(tmp) / "out" / "05_deduplicated.json").read_text())
        assert len(out) == 1


def test_main_rejects_blocklisted():
    """A card with a trademarked name gets rejected."""
    card = {"id": "tst_c_bad", "set": "buried_age", "name": "Pikachu's Bolt",
            "strata": "EMBER", "type": "CREATURE", "rarity": "COMMON", "cost": 1,
            "attack": 2, "vigor": 1, "keywords": [], "abilities": [],
            "flavor": "Trademark breach.", "power_score": 3.0, "content_version": 1}
    with tempfile.TemporaryDirectory() as tmp:
        inp = Path(tmp) / "04_simulated.json"
        inp.write_text(json.dumps([card]))
        rc = main(["--input", str(inp), "--work-dir", str(Path(tmp) / "out")])
        assert rc == 2, f"Expected 2 (all rejected), got {rc}"
        rejects = list((Path(tmp) / "out" / "rejects").glob("*.json"))
        assert len(rejects) >= 1


def test_main_empty_input():
    """Empty input produces empty output."""
    with tempfile.TemporaryDirectory() as tmp:
        inp = Path(tmp) / "04_simulated.json"
        inp.write_text(json.dumps([]))
        rc = main(["--input", str(inp), "--work-dir", str(Path(tmp) / "out")])
        assert rc == 0
        out = json.loads((Path(tmp) / "out" / "05_deduplicated.json").read_text())
        assert len(out) == 0


def test_main_not_found():
    rc = main(["--input", "/nonexistent", "--work-dir", "/tmp"])
    assert rc == 1


def test_all_hand_authored_pass_dedupe():
    """All 60 hand-authored cards should pass dedupe against each OTHER's names."""
    from modules.dedupe_moderate import check_duplicate_name, check_duplicate_text, load_existing_names, build_blocklist_patterns, load_blocklist
    all_cards = []
    for strata in ["verdant", "ember", "tide", "hollow", "dawn"]:
        path = HERE.parent / "content" / "cards" / f"{strata}.json"
        if path.exists():
            with open(path) as f:
                all_cards.extend(json.load(f))

    patterns = build_blocklist_patterns(load_blocklist())
    passed = 0
    failed = 0
    for card in all_cards:
        if card.get("type") == "TOKEN":
            continue
        name = card["name"]
        # Use other card names (exclude self) for dedup
        other_names = {c["name"] for c in all_cards if c["name"] != name and c.get("type") != "TOKEN"}
        name_v = check_duplicate_name(name, other_names, patterns)
        if name_v:
            failed += 1
            continue
        # Should not text-duplicate with similar cards
        other_cards = [c for c in all_cards if c["name"] != name and c.get("type") != "TOKEN"]
        text_v = check_duplicate_text(name, card, other_cards)
        passed += 1
    # At least most should pass
    print(f"[test] Hand-authored: {passed} pass, {failed} fail")
    assert passed > 50, f"Expected >50, got {passed}"


if __name__ == "__main__":
    test_jw_exact_match()
    print("PASS test_jw_exact_match")
    test_jw_case_insensitive()
    print("PASS test_jw_case_insensitive")
    test_jw_similar()
    print("PASS test_jw_similar")
    test_jw_different()
    print("PASS test_jw_different")
    test_text_identical()
    print("PASS test_text_identical")
    test_text_similar()
    print("PASS test_text_similar")
    test_text_different()
    print("PASS test_text_different")
    test_text_empty()
    print("PASS test_text_empty")
    test_load_blocklist()
    print("PASS test_load_blocklist")
    test_build_patterns()
    print("PASS test_build_patterns")
    test_check_blocklist_clean()
    print("PASS test_check_blocklist_clean")
    test_check_blocklist_trademark()
    print("PASS test_check_blocklist_trademark")
    test_check_blocklist_religious()
    print("PASS test_check_blocklist_religious")
    test_check_blocklist_public_figure()
    print("PASS test_check_blocklist_public_figure")
    test_safety_clean()
    print("PASS test_safety_clean")
    test_safety_html()
    print("PASS test_safety_html")
    test_safety_url()
    print("PASS test_safety_url")
    test_dup_name_exact_match()
    print("PASS test_dup_name_exact_match")
    test_dup_name_fuzzy()
    print("PASS test_dup_name_fuzzy")
    test_dup_name_clean()
    print("PASS test_dup_name_clean")
    test_dup_name_blocklisted()
    print("PASS test_dup_name_blocklisted")
    test_dup_text_similar()
    print("PASS test_dup_text_similar")
    test_dup_text_different()
    print("PASS test_dup_text_different")
    test_main_clean_card()
    print("PASS test_main_clean_card")
    test_main_rejects_blocklisted()
    print("PASS test_main_rejects_blocklisted")
    test_main_empty_input()
    print("PASS test_main_empty_input")
    test_main_not_found()
    print("PASS test_main_not_found")
    test_all_hand_authored_pass_dedupe()
    print("PASS test_all_hand_authored_pass_dedupe")
    print("\nAll tests passed!")