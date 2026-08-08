"""Tests for pipeline/modules/orchestrate.py — stage orchestration."""

import json
import time
from pathlib import Path
from unittest.mock import patch, MagicMock

import pytest

import sys
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from modules.orchestrate import (
    StageRunner,
    build_report,
    _validate_report,
    STAGE_ORDER,
    STAGE_FILES,
    main,
)


# ── Fixtures ───────────────────────────────────────────────────────────────────


@pytest.fixture
def mock_seed():
    return {
        "batch_id": "b_test_orchestrate",
        "count": 10,
        "strata": "EMBER",
        "type_mix": {"CREATURE": 7, "RITUAL": 2, "RELIC": 1},
        "cost_curve": {"2": 4, "3": 3, "4": 2, "5": 1},
        "rarity_mix": {"COMMON": 5, "UNCOMMON": 3, "RARE": 1, "RELIC": 1},
        "theme": "Test theme",
        "mechanic_emphasis": ["SWIFT"],
        "forbidden_mechanics": ["EXCAVATE"],
    }


@pytest.fixture
def mock_work_dir(tmp_path):
    d = tmp_path / "work" / "b_test_orchestrate"
    d.mkdir(parents=True)
    return d


def make_stage_file(work_dir: Path, stage: str, cards: list[dict]):
    """Write a stage output file with the given cards."""
    path = work_dir / STAGE_FILES[stage]
    if not path.parent.exists():
        path.parent.mkdir(parents=True)
    path.write_text(json.dumps(cards))
    return path


# ── _validate_report tests ────────────────────────────────────────────────────


class TestValidateReport:
    """_validate_report raises ValueError on impossible values."""

    def test_valid_report_passes(self):
        """Valid monotonic counts should not raise."""
        report = {
            "seed_count": 60,
            "valid_count": 55,
            "scored_count": 40,
            "simulated_count": 40,
            "dedupe_count": 38,
            "approved_count": 35,
            "reject_count": 25,
        }
        _validate_report(report)

    def test_valid_count_exceeds_seed(self):
        report = {
            "seed_count": 60,
            "valid_count": 70,
            "scored_count": 40,
            "simulated_count": 40,
            "dedupe_count": 38,
            "approved_count": 35,
            "reject_count": 25,
        }
        with pytest.raises(ValueError, match="valid_count.*>.*seed_count"):
            _validate_report(report)

    def test_scored_exceeds_valid(self):
        report = {
            "seed_count": 60,
            "valid_count": 40,
            "scored_count": 50,
            "simulated_count": 50,
            "dedupe_count": 38,
            "approved_count": 35,
            "reject_count": 25,
        }
        with pytest.raises(ValueError, match="scored_count.*>.*valid_count"):
            _validate_report(report)

    def test_simulated_exceeds_scored(self):
        report = {
            "seed_count": 60,
            "valid_count": 40,
            "scored_count": 30,
            "simulated_count": 35,
            "dedupe_count": 30,
            "approved_count": 25,
            "reject_count": 25,
        }
        with pytest.raises(ValueError, match="simulated_count.*>.*scored_count"):
            _validate_report(report)

    def test_dedupe_exceeds_simulated(self):
        report = {
            "seed_count": 60,
            "valid_count": 40,
            "scored_count": 30,
            "simulated_count": 30,
            "dedupe_count": 40,
            "approved_count": 25,
            "reject_count": 25,
        }
        with pytest.raises(ValueError, match="dedupe_count.*>.*simulated_count"):
            _validate_report(report)

    def test_approved_exceeds_dedupe(self):
        report = {
            "seed_count": 60,
            "valid_count": 40,
            "scored_count": 30,
            "simulated_count": 30,
            "dedupe_count": 25,
            "approved_count": 30,
            "reject_count": 25,
        }
        with pytest.raises(ValueError, match="approved_count.*>.*dedupe_count"):
            _validate_report(report)

    def test_reject_negative(self):
        report = {
            "seed_count": 60,
            "valid_count": 55,
            "scored_count": 40,
            "simulated_count": 40,
            "dedupe_count": 38,
            "approved_count": 35,
            "reject_count": -1,
        }
        with pytest.raises(ValueError, match="negative"):
            _validate_report(report)


# ── StageRunner tests ─────────────────────────────────────────────────────────


class TestStageRunner:

    def test_stages_run_in_order(self, mock_work_dir, mock_seed):
        runner = StageRunner(mock_work_dir, mock_seed, skip_api=True)
        assert STAGE_ORDER == [
            "generate", "validate", "score", "simulate",
            "dedupe", "moderate", "art", "publish",
        ]

    def test_failure_halts_pipeline(self, mock_work_dir, mock_seed):
        runner = StageRunner(mock_work_dir, mock_seed, skip_api=True)

        def fake_run(stage):
            if stage == "simulate":
                return False
            return True

        with patch.object(runner, "run_stage", side_effect=fake_run):
            result = runner.run()
            assert result is False
            assert runner.failed_stage == "simulate"

    def test_skip_api_passed_to_art(self, mock_work_dir, mock_seed):
        runner = StageRunner(mock_work_dir, mock_seed, skip_api=True)

        with patch.object(runner, "run_stage", return_value=True):
            for stage in STAGE_ORDER:
                args = runner._build_args(stage)
                if stage == "art":
                    assert "--skip-api" in args, f"Art args missing --skip-api: {args}"


# ── build_report tests ────────────────────────────────────────────────────────


class TestBuildReport:

    def test_all_stages_present(self, mock_work_dir, mock_seed):
        for i in range(1, 7):
            make_stage_file(mock_work_dir, STAGE_ORDER[i], [{"id": f"card_{i}"}])

        runner = StageRunner(mock_work_dir, mock_seed, skip_api=True)
        report = build_report(mock_work_dir, runner)

        expected_keys = [
            "batch_id", "seed_count", "valid_count", "scored_count",
            "simulated_count", "dedupe_count", "approved_count",
            "reject_count", "cost_usd", "duration_seconds",
            "stages_run", "failed_stage",
        ]
        for key in expected_keys:
            assert key in report, f"Missing report key: {key}"

        assert report["batch_id"] == "b_test_orchestrate"
        assert report["seed_count"] == 10

    def test_empty_work_dir(self, mock_work_dir, mock_seed):
        runner = StageRunner(mock_work_dir, mock_seed)
        report = build_report(mock_work_dir, runner)

        assert report["valid_count"] == 0
        assert report["scored_count"] == 0
        assert report["simulated_count"] == 0
        assert report["dedupe_count"] == 0
        assert report["approved_count"] == 0
        assert report["reject_count"] == 0

    def test_rejects_counted(self, mock_work_dir, mock_seed):
        rejects_dir = mock_work_dir / "rejects"
        rejects_dir.mkdir()
        (rejects_dir / "reject_000.json").write_text(json.dumps({"reason": "test"}))
        (rejects_dir / "reject_001.json").write_text(json.dumps({"reason": "test"}))

        runner = StageRunner(mock_work_dir, mock_seed)
        report = build_report(mock_work_dir, runner)

        assert report["reject_count"] == 2

    def test_monotonic_card_counts(self, mock_work_dir, mock_seed):
        make_stage_file(mock_work_dir, "generate", [{"id": "c1"}])
        make_stage_file(mock_work_dir, "validate", [{"id": "c1"}])
        make_stage_file(mock_work_dir, "score", [{"id": "c1"}])
        make_stage_file(mock_work_dir, "simulate", [{"id": "c1"}])
        make_stage_file(mock_work_dir, "dedupe", [{"id": "c1"}])
        make_stage_file(mock_work_dir, "art", [{"id": "c1"}])

        runner = StageRunner(mock_work_dir, mock_seed, skip_api=True)
        report = build_report(mock_work_dir, runner)

        assert report["valid_count"] <= report["seed_count"]
        assert report["scored_count"] <= report["valid_count"]
        assert report["simulated_count"] <= report["scored_count"]
        assert report["dedupe_count"] <= report["simulated_count"]
        assert report["approved_count"] <= report["dedupe_count"]


# ── Integration: main() with mocks ────────────────────────────────────────────


class TestMainIntegration:

    def test_dry_run_with_skip_api(self, mock_work_dir, mock_seed, tmp_path):
        seed_path = tmp_path / "seed.json"
        with open(seed_path, "w") as f:
            json.dump(mock_seed, f)

        call_log = []

        def fake_subprocess(*args, **kwargs):
            cmd = kwargs.get("args") or args[0]
            cmd_str = " ".join(cmd) if isinstance(cmd, list) else str(cmd)
            call_log.append(cmd_str[:60])

            result = MagicMock()
            result.returncode = 0

            stage_map = {
                "modules.generate": "generate",
                "modules.validate": "validate",
                "modules.score": "score",
                "modules.simulate": "simulate",
                "modules.dedupe_moderate": "dedupe",
                "modules.art": "art",
                "modules.publish": "publish",
            }
            found = None
            for mod, stg in stage_map.items():
                if mod in cmd_str:
                    found = stg
                    break

            if found:
                out_file = STAGE_FILES.get(found)
                if out_file:
                    out_path = mock_work_dir / out_file
                    out_path.parent.mkdir(parents=True, exist_ok=True)
                    out_path.write_text(json.dumps([{"id": f"{found}_card"}]))
                    if found == "generate":
                        (mock_work_dir / "01_summary.json").write_text(
                            json.dumps({"total_processed": 1, "accepted": 1})
                        )

            result.stdout = f"[{found}] Summary: {{'total': 1}}"
            result.stderr = ""
            return result

        argv = ["--seed", str(seed_path), "--work-dir", str(mock_work_dir), "--skip-api"]

        with patch("subprocess.run", side_effect=fake_subprocess):
            exit_code = main(argv)

        assert exit_code == 0, f"Expected exit 0, got {exit_code}"

        report_path = mock_work_dir / "report.json"
        assert report_path.exists(), "report.json should exist"
        report = json.loads(report_path.read_text())

        expected_keys = [
            "batch_id", "seed_count", "valid_count", "scored_count",
            "simulated_count", "dedupe_count", "approved_count",
            "reject_count", "cost_usd",
        ]
        for key in expected_keys:
            assert key in report, f"Missing key in report.json: {key}"

        assert report["batch_id"] == "b_test_orchestrate"
        assert report["seed_count"] == 10

    def test_failure_exit_code(self, mock_work_dir, mock_seed, tmp_path):
        seed_path = tmp_path / "seed.json"
        with open(seed_path, "w") as f:
            json.dump(mock_seed, f)

        call_count = [0]

        def fake_fail(*args, **kwargs):
            call_count[0] += 1
            result = MagicMock()
            if call_count[0] >= 2:
                result.returncode = 1
            else:
                result.returncode = 0
            result.stdout = "[test] OK"
            result.stderr = ""
            return result

        argv = ["--seed", str(seed_path), "--work-dir", str(mock_work_dir), "--skip-api"]

        with patch("subprocess.run", side_effect=fake_fail):
            exit_code = main(argv)

        assert exit_code != 0, "Should fail when a stage fails"

        report_path = mock_work_dir / "report.json"
        if report_path.exists():
            report = json.loads(report_path.read_text())
            assert report["failed_stage"] is not None


# ── Edge cases ────────────────────────────────────────────────────────────────


def test_missing_seed_file(tmp_path):
    argv = ["--seed", str(tmp_path / "nonexistent.json")]
    exit_code = main(argv)
    assert exit_code == 1


def test_validate_report_with_monotonic_violation(mock_seed, mock_work_dir):
    make_stage_file(mock_work_dir, "generate", [{"id": "c1"}, {"id": "c2"}, {"id": "c3"}])
    make_stage_file(mock_work_dir, "validate", [{"id": "c1"}, {"id": "c2"}, {"id": "c3"}])
    make_stage_file(mock_work_dir, "score", [{"id": "c1"}, {"id": "c2"}, {"id": "c3"}])
    make_stage_file(mock_work_dir, "simulate", [{"id": "c1"}, {"id": "c2"}])
    make_stage_file(mock_work_dir, "dedupe", [{"id": "c1"}, {"id": "c2"}, {"id": "c3"}])

    runner = StageRunner(mock_work_dir, mock_seed, skip_api=True)
    report = build_report(mock_work_dir, runner)

    with pytest.raises(ValueError, match="dedupe_count.*>.*simulated_count"):
        _validate_report(report)