#!/usr/bin/env python3
"""Tests for pipeline/modules/validate.py — P6-03 Validate module."""

import json
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch, MagicMock

HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE))

from modules.validate import (
    load_schema,
    validate_json_schema,
    validate_csharp,
    main,
    SCHEMA_PATH,
)


def test_load_schema():
    """Should load the card.schema.json successfully."""
    schema = load_schema()
    assert "$defs" in schema
    assert "card_def" in schema["$defs"]
    assert schema["title"] == "Runewake Card Definition"


def test_valid_card_passes_schema():
    """A well-formed creature card should pass JSON Schema."""
    schema = load_schema()
    card = {
        "id": "tst_c_test_card",
        "set": "buried_age",
        "name": "Test Card",
        "strata": "EMBER",
        "type": "CREATURE",
        "rarity": "COMMON",
        "cost": 3,
        "attack": 2,
        "vigor": 4,
        "keywords": [],
        "abilities": [],
        "flavor": "A test card.",
        "power_score": 5.0,
        "content_version": 1,
    }
    errors = validate_json_schema(card, schema)
    assert errors == [], f"Expected no errors, got: {errors}"


def test_valid_ritual_passes_schema():
    """A ritual card without attack/vigor should pass."""
    schema = load_schema()
    card = {
        "id": "tst_r_flame_burst",
        "set": "buried_age",
        "name": "Flame Burst",
        "strata": "EMBER",
        "type": "RITUAL",
        "rarity": "COMMON",
        "cost": 2,
        "keywords": [],
        "abilities": [
            {
                "trigger": "RESOLVE",
                "effects": [
                    {"op": "DAMAGE", "target": {"scope": "PLAYER_ENEMY"}, "amount": 3}
                ],
            }
        ],
        "flavor": "Burn.",
        "power_score": 4.0,
        "content_version": 1,
    }
    errors = validate_json_schema(card, schema)
    assert errors == [], f"Expected no errors, got: {errors}"


def test_valid_relic_passes_schema():
    """A relic card with identify_condition should pass."""
    schema = load_schema()
    card = {
        "id": "tst_x_sealed_relic",
        "set": "buried_age",
        "name": "Sealed Relic",
        "strata": "HOLLOW",
        "type": "RELIC",
        "rarity": "RELIC",
        "cost": 2,
        "keywords": ["SEALED"],
        "identify_condition": {"op": "DAMAGED_THIS_TURN", "value": True},
        "abilities": [
            {
                "trigger": "ON_TURN_START",
                "effects": [
                    {"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}
                ],
            }
        ],
        "flavor": "Sealed.",
        "power_score": 8.0,
        "content_version": 1,
    }
    errors = validate_json_schema(card, schema)
    assert errors == [], f"Expected no errors, got: {errors}"


def test_invalid_strata_rejected():
    """Bad strata value should fail JSON Schema."""
    schema = load_schema()
    card = {"id": "tst_c_bad", "set": "buried_age", "name": "Bad", "strata": "FIRE",
            "type": "CREATURE", "rarity": "COMMON", "cost": 1, "attack": 1, "vigor": 1,
            "keywords": [], "abilities": [], "power_score": 2.0, "content_version": 1}
    errors = validate_json_schema(card, schema)
    assert len(errors) > 0
    assert any("strata" in err.lower() or "FIRE" in err for err in errors)


def test_creature_missing_stats_rejected():
    """CREATURE without attack/vigor should fail JSON Schema."""
    schema = load_schema()
    card = {"id": "tst_c_nostats", "set": "buried_age", "name": "No Stats",
            "strata": "TIDE", "type": "CREATURE", "rarity": "COMMON", "cost": 2,
            "keywords": [], "abilities": [], "power_score": 1.0, "content_version": 1}
    errors = validate_json_schema(card, schema)
    assert len(errors) > 0


def test_relic_missing_identify_rejected():
    """RELIC without identify_condition should fail."""
    schema = load_schema()
    card = {"id": "tst_x_noident", "set": "buried_age", "name": "No Ident",
            "strata": "DAWN", "type": "RELIC", "rarity": "RELIC", "cost": 1,
            "keywords": ["SEALED"], "abilities": [],
            "power_score": 3.0, "content_version": 1}
    errors = validate_json_schema(card, schema)
    assert len(errors) > 0
    assert any("identify" in err.lower() for err in errors)


def test_out_of_range_cost_rejected():
    """Cost outside 0-10 should fail."""
    schema = load_schema()
    card = {"id": "tst_c_expensive", "set": "buried_age", "name": "Expensive",
            "strata": "EMBER", "type": "CREATURE", "rarity": "COMMON", "cost": 15,
            "attack": 5, "vigor": 5, "keywords": [], "abilities": [],
            "power_score": 10.0, "content_version": 1}
    errors = validate_json_schema(card, schema)
    assert len(errors) > 0


@patch("modules.validate.subprocess.run")
def test_validate_csharp_all_pass(mock_run):
    """C# engine bridge parses [✓] lines correctly."""
    mock_run.return_value = MagicMock(
        stdout="[✓] tst_c_test (Test Card)\n[✓] tst_r_flame (Flame)\n",
        stderr="Validated 2 cards, 0 error(s).\n",
        returncode=0,
    )
    cards = [
        {"id": "tst_c_test", "name": "Test Card"},
        {"id": "tst_r_flame", "name": "Flame"},
    ]
    results = validate_csharp(Path("/fake/cli"), cards)
    assert len(results) == 2
    for idx, err in results:
        assert err == "", f"Card {idx} should have no errors, got: {err}"


@patch("modules.validate.subprocess.run")
def test_validate_csharp_with_rejects(mock_run):
    """C# engine bridge parses [✗] with errors."""
    mock_run.return_value = MagicMock(
        stdout="[✗] tst_c_bad (Bad Card)\n    - invalid cost: 15\n[✓] tst_c_good (Good Card)\n",
        stderr="Validated 2 cards, 1 error(s).\n",
        returncode=1,
    )
    cards = [
        {"id": "tst_c_bad", "name": "Bad Card"},
        {"id": "tst_c_good", "name": "Good Card"},
    ]
    results = validate_csharp(Path("/fake/cli"), cards)
    assert len(results) == 2
    bad_idx = 0
    good_idx = 1
    assert results[bad_idx][1] != "", "Bad card should have errors"
    assert "invalid cost" in results[bad_idx][1]
    assert results[good_idx][1] == "", "Good card should have no errors"


@patch("modules.validate.subprocess.run")
def test_validate_csharp_cli_not_found(mock_run):
    """When CLI binary doesn't exist, return no errors."""
    # Simulate FileNotFoundError by making mock raise it
    from modules.validate import validate_csharp
    # Use a non-existent path
    results = validate_csharp(Path("/nonexistent/Runewake.Sim"), [{"id": "tst_c_a"}])
    assert len(results) == 1
    assert results[0][1] == "", "Should return no errors when CLI missing"


@patch("modules.validate.subprocess.run")
def test_main_end_to_end(mock_run):
    """End-to-end test: schema validation + C# bridge."""
    # Mock C# CLI to pass everything
    mock_run.return_value = MagicMock(
        stdout="[✓] tst_c_valid (Valid Card)\n",
        stderr="Validated 1 cards, 0 error(s).\n",
        returncode=0,
    )

    valid_card = {
        "id": "tst_c_valid",
        "set": "buried_age",
        "name": "Valid Card",
        "strata": "EMBER",
        "type": "CREATURE",
        "rarity": "COMMON",
        "cost": 2,
        "attack": 2,
        "vigor": 2,
        "keywords": [],
        "abilities": [],
        "flavor": "Valid.",
        "power_score": 3.5,
        "content_version": 1,
    }

    with tempfile.TemporaryDirectory() as tmp:
        input_path = Path(tmp) / "01_raw.json"
        with open(input_path, "w") as f:
            json.dump([valid_card], f)

        exit_code = main([
            "--input", str(input_path),
            "--work-dir", str(Path(tmp) / "out"),
            "--skip-csharp",  # Use skip to avoid needing real binary
        ])

        assert exit_code == 0, f"Expected 0, got {exit_code}"
        out_path = Path(tmp) / "out" / "02_valid.json"
        assert out_path.exists()
        loaded = json.loads(out_path.read_text())
        assert len(loaded) == 1
        assert loaded[0]["name"] == "Valid Card"
        assert (Path(tmp) / "out" / "02_summary.json").exists()


@patch("modules.validate.subprocess.run")
def test_main_rejects_schema_failure(mock_run):
    """Cards failing JSON Schema should go to rejects, not C# bridge."""
    with tempfile.TemporaryDirectory() as tmp:
        input_path = Path(tmp) / "01_raw.json"
        bad_card = {"id": "bad", "name": "Bad", "strata": "FIRE", "type": "CREATURE",
                    "rarity": "COMMON", "cost": 1, "attack": 1, "vigor": 1,
                    "keywords": [], "abilities": [], "power_score": 1.0, "content_version": 1}
        with open(input_path, "w") as f:
            json.dump([bad_card], f)

        exit_code = main([
            "--input", str(input_path),
            "--work-dir", str(Path(tmp) / "out"),
            "--skip-csharp",
        ])

        # Should return 2 for all-rejected
        assert exit_code == 2, f"Expected 2, got {exit_code}"
        rejects = list((Path(tmp) / "out" / "rejects").glob("*.json"))
        assert len(rejects) >= 1
        # No 02_valid.json since all failed
        assert not (Path(tmp) / "out" / "02_valid.json").exists()


def test_schema_all_example_cards_pass():
    """All 6 example cards should validate against the schema."""
    schema = load_schema()
    from modules.generate import load_examples
    examples = load_examples()
    for ex in examples:
        errors = validate_json_schema(ex, schema)
        assert errors == [], f"Example '{ex.get('name')}' failed: {errors}"
    print(f"[test] All {len(examples)} example cards pass schema")


def test_schema_rejects_bad_id_pattern():
    """ID not matching pattern should fail."""
    schema = load_schema()
    card = {"id": "BAD-ID", "set": "buried_age", "name": "Bad ID",
            "strata": "VERDANT", "type": "CREATURE", "rarity": "COMMON", "cost": 1,
            "attack": 1, "vigor": 1, "keywords": [], "abilities": [],
            "power_score": 2.0, "content_version": 1}
    errors = validate_json_schema(card, schema)
    assert len(errors) > 0


def test_schema_rejects_too_many_abilities():
    """More than 2 abilities should fail."""
    schema = load_schema()
    card = {"id": "tst_c_3abilities", "set": "buried_age", "name": "3 Abilities",
            "strata": "TIDE", "type": "CREATURE", "rarity": "COMMON", "cost": 3,
            "attack": 2, "vigor": 3, "keywords": [], "abilities": [
                {"trigger": "ON_SUMMON", "effects": [{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}]},
                {"trigger": "ON_DEATH", "effects": [{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}]},
                {"trigger": "ON_TURN_START", "effects": [{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}]},
            ], "power_score": 6.0, "content_version": 1}
    errors = validate_json_schema(card, schema)
    assert len(errors) > 0


def test_main_with_generate_output_format():
    """Should handle the output format from the GENERATE stage."""
    schema = load_schema()
    # Use a real card from example_cards
    from modules.generate import load_examples
    examples = load_examples()
    first = examples[0]
    errors = validate_json_schema(first, schema)
    assert errors == []


if __name__ == "__main__":
    # Quick self-test run
    test_load_schema()
    print("  PASS  test_load_schema")
    test_valid_card_passes_schema()
    print("  PASS  test_valid_card_passes_schema")
    test_valid_ritual_passes_schema()
    print("  PASS  test_valid_ritual_passes_schema")
    test_valid_relic_passes_schema()
    print("  PASS  test_valid_relic_passes_schema")
    test_invalid_strata_rejected()
    print("  PASS  test_invalid_strata_rejected")
    test_creature_missing_stats_rejected()
    print("  PASS  test_creature_missing_stats_rejected")
    test_relic_missing_identify_rejected()
    print("  PASS  test_relic_missing_identify_rejected")
    test_out_of_range_cost_rejected()
    print("  PASS  test_out_of_range_cost_rejected")
    test_schema_all_example_cards_pass()
    print("  PASS  test_schema_all_example_cards_pass")
    test_schema_rejects_bad_id_pattern()
    print("  PASS  test_schema_rejects_bad_id_pattern")
    test_schema_rejects_too_many_abilities()
    print("  PASS  test_schema_rejects_too_many_abilities")
    test_main_with_generate_output_format()
    print("  PASS  test_main_with_generate_output_format")
    test_validate_csharp_all_pass(None)
    print("  PASS  test_validate_csharp_all_pass")
    test_validate_csharp_with_rejects(None)
    print("  PASS  test_validate_csharp_with_rejects")
    test_main_end_to_end(None)
    print("  PASS  test_main_end_to_end")
    test_main_rejects_schema_failure(None)
    print("  PASS  test_main_rejects_schema_failure")
    print("\nAll tests passed!")