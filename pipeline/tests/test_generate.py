#!/usr/bin/env python3
"""Tests for pipeline/modules/generate.py — P6-02 Generate module."""

import json
import os
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch, MagicMock

# Add pipeline to path
HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE))

from modules.generate import (
    build_prompt,
    load_examples,
    load_existing_names,
    repair_json,
    write_output,
    main,
    SYSTEM_RULES,
)


def test_load_examples():
    """Should load 6 example cards from the schema."""
    examples = load_examples()
    assert len(examples) == 6, f"Expected 6, got {len(examples)}"
    for ex in examples:
        assert "name" in ex
        assert "strata" in ex
        assert "abilities" in ex


def test_load_existing_names():
    """Should return existing EMBER card names."""
    names = load_existing_names("EMBER")
    assert len(names) > 0, "Should find existing EMBER cards"
    assert "Cinder Runner" in names


def test_build_prompt_contains_seed_fields():
    """The generated prompt should include seed config."""
    seed = {
        "batch_id": "b_test_01",
        "count": 10,
        "strata": "EMBER",
        "type_mix": {"CREATURE": 7, "RITUAL": 2, "RELIC": 1},
        "cost_curve": {"2": 4, "3": 3, "4": 2, "5": 1},
        "rarity_mix": {"COMMON": 5, "UNCOMMON": 3, "RARE": 1, "RELIC": 1},
        "theme": "Test theme",
        "mechanic_emphasis": ["SWIFT"],
        "forbidden_mechanics": ["EXCAVATE"],
    }
    examples = load_examples()
    names = load_existing_names("EMBER")
    prompt = build_prompt(seed, examples, names)

    assert "batch_id" in prompt
    assert "b_test_01" in prompt
    assert "EMBER" in prompt
    assert "Test theme" in prompt
    assert "SWIFT" in prompt
    assert "EXCAVATE" in prompt
    assert "10 cards" in prompt or "10 card" in prompt
    assert "Cinder Runner" in prompt  # from avoidance list


def test_build_prompt_includes_examples():
    """Prompt should contain all 6 example card names."""
    seed = {
        "batch_id": "b_test_02",
        "count": 5,
        "strata": "HOLLOW",
        "type_mix": {"CREATURE": 5},
        "cost_curve": {"2": 2, "3": 3},
        "rarity_mix": {"COMMON": 3, "UNCOMMON": 2},
        "theme": "Darkness",
        "mechanic_emphasis": [],
        "forbidden_mechanics": [],
    }
    examples = load_examples()
    prompt = build_prompt(seed, examples, [])
    # All 6 example cards should appear
    for ex in examples:
        assert ex["name"] in prompt, f"Example '{ex['name']}' missing from prompt"


def test_build_prompt_no_avoidance_when_empty():
    """Should not have an avoidance section when no existing names."""
    seed = {"batch_id": "b_test_03", "count": 3, "strata": "DAWN",
            "type_mix": {"CREATURE": 3}, "cost_curve": {"3": 3},
            "rarity_mix": {"COMMON": 3}, "theme": "test",
            "mechanic_emphasis": [], "forbidden_mechanics": []}
    prompt = build_prompt(seed, [], [])
    assert "EXISTING NAMES" not in prompt


def test_repair_json_plain_array():
    """Should pass through a clean JSON array unchanged."""
    raw = '[{"name": "Test", "id": "tst_c_test"}]'
    result = repair_json(raw)
    assert json.loads(result) == [{"name": "Test", "id": "tst_c_test"}]


def test_repair_json_with_fences():
    """Should strip markdown code fences."""
    raw = '```json\n[{"name": "Test"}]\n```'
    result = repair_json(raw)
    parsed = json.loads(result)
    assert parsed == [{"name": "Test"}]


def test_repair_json_with_preamble():
    """Should extract JSON array from text with preamble."""
    raw = 'Here are your cards:\n\n[{"name": "Card One"}]\n\nThat was fun.'
    result = repair_json(raw)
    parsed = json.loads(result)
    assert parsed == [{"name": "Card One"}]


def test_repair_json_empty_returns_brackets():
    """Should return '[]' when no array found."""
    result = repair_json("Sorry, I cannot generate cards right now.")
    assert json.loads(result) == []


def test_write_output_creates_files():
    """write_output should create 01_raw.json, rejects, and summary."""
    with tempfile.TemporaryDirectory() as tmp:
        work_dir = Path(tmp)
        cards = [{"name": "A", "id": "tst_c_a"}, {"name": "B", "id": "tst_c_b"}]
        rejects = [{"reason": "Test reject"}]
        write_output(work_dir, "b_test", cards, rejects)

        raw = work_dir / "01_raw.json"
        assert raw.exists()
        loaded = json.loads(raw.read_text())
        assert len(loaded) == 2
        assert loaded[0]["name"] == "A"

        rej_dir = work_dir / "rejects"
        assert rej_dir.exists()
        rej_files = list(rej_dir.glob("*.json"))
        assert len(rej_files) == 1

        summary = work_dir / "01_summary.json"
        assert summary.exists()
        s = json.loads(summary.read_text())
        assert s["accepted"] == 2
        assert s["rejected"] == 1


@patch("modules.generate.call_llm")
def test_main_with_mock_llm(mock_call_llm):
    """End-to-end test with mocked LLM returning valid cards."""
    valid_cards = [
        {
            "id": "emb_c_test_card",
            "set": "buried_age",
            "name": "Test Card",
            "strata": "EMBER",
            "type": "CREATURE",
            "rarity": "COMMON",
            "cost": 2,
            "attack": 3,
            "vigor": 2,
            "keywords": [],
            "abilities": [],
            "flavor": "A test card.",
            "power_score": 5.0,
            "content_version": 1,
        }
    ]
    mock_call_llm.return_value = json.dumps(valid_cards)

    with tempfile.TemporaryDirectory() as tmp:
        work_dir = Path(tmp)
        seed_path = work_dir / "seed.json"
        seed = {
            "batch_id": "b_test_e2e",
            "count": 1,
            "strata": "EMBER",
            "type_mix": {"CREATURE": 1},
            "cost_curve": {"2": 1},
            "rarity_mix": {"COMMON": 1},
            "theme": "Test",
            "mechanic_emphasis": [],
            "forbidden_mechanics": [],
        }
        seed_path.write_text(json.dumps(seed))

        exit_code = main([
            "--seed", str(seed_path),
            "--work-dir", str(work_dir / "output"),
            "--api-key", "test-key",
        ])

        assert exit_code == 0, f"Expected 0, got {exit_code}"
        raw_file = work_dir / "output" / "01_raw.json"
        assert raw_file.exists(), "01_raw.json should exist"
        loaded = json.loads(raw_file.read_text())
        assert len(loaded) == 1
        assert loaded[0]["name"] == "Test Card"


@patch("modules.generate.call_llm")
def test_main_rejects_wrong_strata(mock_call_llm):
    """Cards with wrong strata should be rejected."""
    invalid_cards = [
        {
            "id": "vrd_c_wrong",
            "set": "buried_age",
            "name": "Wrong Strata Card",
            "strata": "VERDANT",  # Should be EMBER
            "type": "CREATURE",
            "rarity": "COMMON",
            "cost": 2,
            "attack": 2,
            "vigor": 2,
            "keywords": [],
            "abilities": [],
            "flavor": "Wrong.",
            "power_score": 4.0,
            "content_version": 1,
        }
    ]
    mock_call_llm.return_value = json.dumps(invalid_cards)

    with tempfile.TemporaryDirectory() as tmp:
        work_dir = Path(tmp)
        seed_path = work_dir / "seed.json"
        seed = {
            "batch_id": "b_test_reject",
            "count": 1,
            "strata": "EMBER",
            "type_mix": {"CREATURE": 1},
            "cost_curve": {"2": 1},
            "rarity_mix": {"COMMON": 1},
            "theme": "Test",
            "mechanic_emphasis": [],
            "forbidden_mechanics": [],
        }
        seed_path.write_text(json.dumps(seed))

        exit_code = main([
            "--seed", str(seed_path),
            "--work-dir", str(work_dir / "output"),
            "--api-key", "test-key",
        ])

        # 0 cards accepted, returns 2 for partial failure
        assert exit_code == 2, f"Expected 2 (rejects only), got {exit_code}"
        raw_file = work_dir / "output" / "01_raw.json"
        assert raw_file.exists()
        loaded = json.loads(raw_file.read_text())
        assert len(loaded) == 0, "No cards should be accepted"
        rej_files = list((work_dir / "output" / "rejects").glob("*.json"))
        assert len(rej_files) == 1


@patch("modules.generate.call_llm")
def test_main_retry_on_parse_failure(mock_call_llm):
    """Should retry once when LLM returns unparseable output."""
    # First call returns garbage, second returns valid JSON
    mock_call_llm.side_effect = [
        "Sorry, here are some cards I made up...",
        json.dumps([{
            "id": "emb_c_retried",
            "set": "buried_age",
            "name": "Retried Card",
            "strata": "EMBER",
            "type": "CREATURE",
            "rarity": "COMMON",
            "cost": 1,
            "attack": 2,
            "vigor": 1,
            "keywords": [],
            "abilities": [],
            "flavor": "I was retried.",
            "power_score": 3.0,
            "content_version": 1,
        }]),
    ]

    with tempfile.TemporaryDirectory() as tmp:
        work_dir = Path(tmp)
        seed_path = work_dir / "seed.json"
        seed = {
            "batch_id": "b_test_retry",
            "count": 1,
            "strata": "EMBER",
            "type_mix": {"CREATURE": 1},
            "cost_curve": {"1": 1},
            "rarity_mix": {"COMMON": 1},
            "theme": "Test",
            "mechanic_emphasis": [],
            "forbidden_mechanics": [],
        }
        seed_path.write_text(json.dumps(seed))

        exit_code = main([
            "--seed", str(seed_path),
            "--work-dir", str(work_dir / "output"),
            "--api-key", "test-key",
        ])

        assert exit_code == 0, f"Expected 0, got {exit_code}"
        raw_file = work_dir / "output" / "01_raw.json"
        assert raw_file.exists()
        loaded = json.loads(raw_file.read_text())
        assert len(loaded) == 1
        assert loaded[0]["name"] == "Retried Card"
        # verify LLM was called twice (initial + retry)
        assert mock_call_llm.call_count == 2


@patch("modules.generate.call_llm")
def test_main_handles_empty_response(mock_call_llm):
    """Should handle an empty array gracefully."""
    mock_call_llm.return_value = "[]"

    with tempfile.TemporaryDirectory() as tmp:
        work_dir = Path(tmp)
        seed_path = work_dir / "seed.json"
        seed = {
            "batch_id": "b_test_empty",
            "count": 3,
            "strata": "EMBER",
            "type_mix": {"CREATURE": 3},
            "cost_curve": {"1": 3},
            "rarity_mix": {"COMMON": 3},
            "theme": "Test",
            "mechanic_emphasis": [],
            "forbidden_mechanics": [],
        }
        seed_path.write_text(json.dumps(seed))

        exit_code = main([
            "--seed", str(seed_path),
            "--work-dir", str(work_dir / "output"),
            "--api-key", "test-key",
        ])

        assert exit_code == 0
        raw_file = work_dir / "output" / "01_raw.json"
        loaded = json.loads(raw_file.read_text())
        assert len(loaded) == 0


def test_system_rules_contains_closed_vocabulary():
    """System rules should document all enums."""
    assert "STRATA" in SYSTEM_RULES
    assert "VERDANT" in SYSTEM_RULES
    assert "EMBER" in SYSTEM_RULES
    assert "TRIGGERS" in SYSTEM_RULES
    assert "ON_SUMMON" in SYSTEM_RULES
    assert "OPS" in SYSTEM_RULES
    assert "DAMAGE" in SYSTEM_RULES
    assert "SCOPES" in SYSTEM_RULES
    assert "SELF" in SYSTEM_RULES
    assert "FILTERS" in SYSTEM_RULES
    assert "ADJACENT" in SYSTEM_RULES
    assert "CONDITION_OPS" in SYSTEM_RULES
    assert "ALLY_COUNT_GTE" in SYSTEM_RULES
    assert "KEYWORDS" in SYSTEM_RULES
    assert "GUARD" in SYSTEM_RULES
    assert "SEALED" in SYSTEM_RULES


def test_system_rules_contains_constraints():
    """System rules should document all hard constraints."""
    assert "Max 2 abilities" in SYSTEM_RULES
    assert "Creature cards MUST have attack and vigor" in SYSTEM_RULES
    assert "RELIC type cards may have identify_condition" in SYSTEM_RULES
    assert "Costs: 0-10" in SYSTEM_RULES