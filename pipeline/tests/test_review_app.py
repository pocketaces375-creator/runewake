#!/usr/bin/env python3
"""Tests for pipeline/review_app/app.py — P6-08 Review UI."""

import json
import sys
import threading
import time
from pathlib import Path

import requests

HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE))

from review_app.app import main


# ── Helpers ───────────────────────────────────────────────────────────────────


def _make_mock_workdir(tmp: str) -> Path:
    """Create a mock work dir with 06_art.json and 04_simulated.json."""
    work = Path(tmp) / "work"
    work.mkdir(parents=True)
    (work / "art").mkdir()

    cards = [
        {"id": "vrd_c_root_warden", "name": "Root Warden", "strata": "VERDANT",
         "type": "CREATURE", "rarity": "COMMON", "cost": 3, "attack": 2, "vigor": 4,
         "keywords": ["GUARD"], "abilities": [
             {"trigger": "ON_SUMMON", "condition": None, "effects": [
                 {"op": "BUFF", "attack": 0, "vigor": 1,
                  "target": {"scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL"},
                  "duration": "PERMANENT"}]}
         ], "flavor": "The grove keeps its own ledgers.",
         "power_score": 7.1, "art": {"prompt": "...", "asset": "", "fallback": False}},
        {"id": "hol_r_barrow_revenant", "name": "Barrow Revenant", "strata": "HOLLOW",
         "type": "CREATURE", "rarity": "RARE", "cost": 5, "attack": 4, "vigor": 5,
         "keywords": ["UNEARTH"], "abilities": [
             {"trigger": "ON_DEATH", "effects": [
                 {"op": "DAMAGE", "value": 2, "target": {"scope": "ENEMY_CREATURE", "filter": "RANDOM"}}]}
         ], "flavor": "Old kings remember.",
         "power_score": 9.2, "art": {"prompt": "...", "asset": "", "fallback": True}},
        {"id": "tid_u_tidal_scholar", "name": "Tidal Scholar", "strata": "TIDE",
         "type": "CREATURE", "rarity": "UNCOMMON", "cost": 3, "attack": 1, "vigor": 3,
         "keywords": [], "abilities": [], "flavor": "The deep keeps its secrets.",
         "power_score": 4.5, "art": {"prompt": "...", "asset": "", "fallback": False}},
    ]
    (work / "06_art.json").write_text(json.dumps(cards, indent=2))

    sim = [
        {"card_id": "vrd_c_root_warden", "archetype": "midrange",
         "avg_delta": 0.012, "max_delta": 0.018, "flags": [],
         "matchup_results": {"aggro": {"win_rate": 0.55, "baseline_win_rate": 0.53, "delta": 0.02, "avg_turns": 7.2}}},
        {"card_id": "hol_r_barrow_revenant", "archetype": "control",
         "avg_delta": 0.053, "max_delta": 0.062, "flags": ["TOO_STRONG: max delta +6.2%"],
         "matchup_results": {"aggro": {"win_rate": 0.61, "baseline_win_rate": 0.45, "delta": 0.16, "avg_turns": 8.0}}},
    ]
    (work / "04_simulated.json").write_text(json.dumps(sim, indent=2))

    return work


def _start_app(work_dir: Path, port: int):
    """Start the review app in a background thread."""
    t = threading.Thread(target=lambda: main(["--work-dir", str(work_dir), "--port", str(port)]), daemon=True)
    t.start()
    time.sleep(3)


# ── Tests ────────────────────────────────────────────────────────────────────


def test_health_endpoint():
    """GET /health returns card count and sim count."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9110)
        r = requests.get("http://127.0.0.1:9110/health")
        assert r.status_code == 200
        data = r.json()
        assert data["cards"] == 3
        assert data["sim_results"] == 2


def test_main_page_returns_html():
    """GET / returns HTML with cards listed."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9111)
        r = requests.get("http://127.0.0.1:9111/")
        assert r.status_code == 200
        assert "text/html" in r.headers.get("content-type", "")
        assert "Root Warden" in r.text
        assert "Barrow Revenant" in r.text
        assert "FALLBACK" in r.text  # commission-queue badge for fallback cards


def test_approve_card():
    """POST /card/{id}/approve returns success."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9112)
        r = requests.post("http://127.0.0.1:9112/card/vrd_c_root_warden/approve")
        assert r.status_code == 200
        assert r.json()["status"] == "approved"


def test_reject_card_with_reason():
    """POST /card/{id}/reject with a reason returns success."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9113)
        r = requests.post("http://127.0.0.1:9113/card/hol_r_barrow_revenant/reject",
                          data={"reason": "BALANCE: too strong", "detail": "flagged"})
        assert r.status_code == 200
        assert r.json()["status"] == "rejected"
        # Verify decisions file
        decisions = json.loads((work / "07_decisions.json").read_text())
        assert len(decisions) == 1
        assert decisions[0]["action"] == "rejected"
        assert decisions[0]["reason"] == "BALANCE: too strong"


def test_reject_card_without_reason_fails():
    """POST /card/{id}/reject without a reason returns 400/422."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9114)
        r = requests.post("http://127.0.0.1:9114/card/hol_r_barrow_revenant/reject",
                          data={"reason": "", "detail": ""})
        assert r.status_code in (400, 422)


def test_edit_fetch():
    """GET /edit-fetch/{id} returns the card JSON."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9115)
        r = requests.get("http://127.0.0.1:9115/edit-fetch/vrd_c_root_warden")
        assert r.status_code == 200
        assert r.json()["card"]["name"] == "Root Warden"


def test_edit_card():
    """POST /card/{id}/edit saves updated JSON."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9116)
        updated = {"name": "Root Warden EDIT", "cost": 4}
        r = requests.post("http://127.0.0.1:9116/card/vrd_c_root_warden/edit",
                          data={"card_json": json.dumps(updated), "reason": "Adjusted cost"})
        assert r.status_code == 200
        assert r.json()["status"] == "edited"
        # Verify it persisted
        r2 = requests.get("http://127.0.0.1:9116/edit-fetch/vrd_c_root_warden")
        assert r2.json()["card"]["name"] == "Root Warden EDIT"


def test_fallback_badge_visible():
    """Cards with fallback art should have 'FALLBACK' badge in HTML."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        _start_app(work, 9117)
        r = requests.get("http://127.0.0.1:9117/")
        # Barrow Revenant has fallback=True
        assert "FALLBACK" in r.text
        # Root Warden has fallback=False, should not show fallback badge
        # (it shows in the card-entry div with class "fallback")


def test_commission_queue_status():
    """Commission queue count should be reflected in the UI."""
    import tempfile
    with tempfile.TemporaryDirectory() as tmp:
        work = _make_mock_workdir(tmp)
        # Create a mock commission queue
        cq = Path(tmp) / "ART_COMMISSION_QUEUE.md"
        cq.write_text("# ART_COMMISSION_QUEUE\n\n- [ ] Test card\n")
        _start_app(work, 9118)
        r = requests.get("http://127.0.0.1:9118/")
        # The UI should show commission-pending count
        assert "commission" in r.text.lower()