#!/usr/bin/env python3
"""Tests for pipeline/modules/simulate.py — P6-05 Simulate module."""

import json
import sys
import tempfile
from pathlib import Path
from unittest.mock import patch, MagicMock

HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE))

from modules.simulate import (
    load_card_registry,
    load_baselines,
    write_deck_file,
    run_batch,
    substitute_and_simulate,
    main,
)


# ── Data loading tests ──────────────────────────────────────────────────────

def test_load_card_registry():
    reg = load_card_registry()
    assert len(reg) >= 60, f"Expected >=60 cards, got {len(reg)}"
    assert "emb_c_ember_hound" in reg
    assert "vrd_c_root_warden" in reg


def test_load_baselines():
    bases = load_baselines()
    assert len(bases) == 3
    for name in ("aggro", "midrange", "control"):
        assert name in bases
        assert len(bases[name]["cards"]) == 30
        assert "description" in bases[name]


def test_write_deck_file():
    reg = load_card_registry()
    with tempfile.TemporaryDirectory() as tmp:
        p = write_deck_file(["emb_c_ember_hound"], reg, Path(tmp), "test")
        assert p.exists()
        data = json.loads(p.read_text())
        assert len(data) == 1
        assert data[0]["id"] == "emb_c_ember_hound"
        assert data[0]["name"] == "Ember Hound"


def test_write_deck_file_skips_missing():
    reg = load_card_registry()
    with tempfile.TemporaryDirectory() as tmp:
        p = write_deck_file(["nonexistent_card_id"], reg, Path(tmp), "missing")
        data = json.loads(p.read_text())
        assert len(data) == 0


# ── run_batch tests ─────────────────────────────────────────────────────────

@patch("modules.simulate.subprocess.run")
def test_run_batch_success(mock_run):
    mock_report = '{"config":{},"results":[{"game":0,"winner":0,"turns":10}],"p0Wins":1,"p1Wins":0,"totalGames":1,"avgTurns":10.0,"winRateP0":1.0}'
    mock_run.return_value = MagicMock(stdout=mock_report, stderr="", returncode=0)
    # Mock the SIM_BIN path to exist
    with patch("modules.simulate.SIM_BIN", Path("/fake/Runewake.Sim")):
        result = run_batch(Path("/fake/a"), Path("/fake/b"), 1, 42)
    assert result is not None
    assert result.get("winRateP0") == 1.0
    assert result.get("avgTurns") == 10.0


@patch("modules.simulate.subprocess.run")
def test_run_batch_no_binary(mock_run):
    mock_run.side_effect = FileNotFoundError()
    with patch("modules.simulate.SIM_BIN", Path("/nonexistent/Runewake.Sim")):
        result = run_batch(Path("/fake/a"), Path("/fake/b"), 1, 42)
    assert result is None


# ── substitute_and_simulate tests ───────────────────────────────────────────

@patch("modules.simulate.subprocess.run")
def test_substitute_and_simulate(mock_run):
    """End-to-end test of candidate substitution."""
    mock_report = '{"config":{},"results":[],"p0Wins":55,"p1Wins":45,"totalGames":100,"avgTurns":12.0,"winRateP0":0.55}'
    mock_run.return_value = MagicMock(stdout=mock_report, stderr="", returncode=0)

    reg = load_card_registry()
    bases = load_baselines()
    ref = {"aggro": {"aggro": 0.5, "midrange": 0.5, "control": 0.5}}

    candidate = {"id": "emb_c_ember_hound", "name": "Ember Hound Test"}
    with tempfile.TemporaryDirectory() as tmp:
        with patch("modules.simulate.SIM_BIN", Path(__file__)):
            result = substitute_and_simulate(
                candidate, "aggro", bases["aggro"]["cards"],
                reg, bases, ref, Path(tmp), 0,
            )
    assert result is not None
    assert result["card_id"] == "emb_c_ember_hound"
    assert "matchup_results" in result
    assert "avg_delta" in result
    assert 0.55 - 0.5 == result["avg_delta"]


@patch("modules.simulate.subprocess.run")
def test_substitute_no_id(mock_run):
    """Candidate without id returns None."""
    with tempfile.TemporaryDirectory() as tmp:
        result = substitute_and_simulate(
            {}, "aggro", ["emb_c_ember_hound"],
            {}, {"aggro": {"cards": []}}, {"aggro": {"aggro": 0.5}},
            Path(tmp), 0,
        )
    assert result is None


# ── Main entry point tests ──────────────────────────────────────────────────

@patch("modules.simulate.subprocess.run")
def test_main_empty_input(mock_run):
    """Empty or no cards should produce empty output."""
    with tempfile.TemporaryDirectory() as tmp:
        inp = Path(tmp) / "03_scored.json"
        inp.write_text(json.dumps([]))
        with patch("modules.simulate.SIM_BIN", Path(__file__)):
            rc = main(["--input", str(inp), "--work-dir", str(Path(tmp) / "out"), "--games", "1"])
        assert rc == 0
        out = json.loads((Path(tmp) / "out" / "04_simulated.json").read_text())
        assert len(out) == 0


def test_main_input_not_found():
    rc = main(["--input", "/nonexistent", "--work-dir", "/tmp/out"])
    assert rc == 1


if __name__ == "__main__":
    test_load_card_registry()
    print("PASS test_load_card_registry")
    test_load_baselines()
    print("PASS test_load_baselines")
    test_write_deck_file()
    print("PASS test_write_deck_file")
    test_write_deck_file_skips_missing()
    print("PASS test_write_deck_file_skips_missing")
    test_run_batch_success()
    print("PASS test_run_batch_success")
    test_run_batch_no_binary()
    print("PASS test_run_batch_no_binary")
    test_substitute_and_simulate()
    print("PASS test_substitute_and_simulate")
    test_substitute_no_id()
    print("PASS test_substitute_no_id")
    test_main_empty_input()
    print("PASS test_main_empty_input")
    test_main_input_not_found()
    print("PASS test_main_input_not_found")
    print("\nAll tests passed!")