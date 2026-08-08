#!/usr/bin/env python3
"""P6-10: Pipeline orchestration — runs all stages end-to-end for a seed file.

Usage:
    python -m modules.orchestrate --seed pipeline/seeds/ember_01.json \\
        --work-dir pipeline/work/b_2026_ember_01 [--skip-api]

Each stage is invoked as a subprocess matching the per-stage CLI pattern.
On stage failure, logs the error and stops the pipeline.

Stages in order: GENERATE → VALIDATE → SCORE → SIMULATE → DEDUPE → MODERATE → ART → PUBLISH
"""

import argparse
import json
import os
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
PIPELINE = HERE.parent  # pipeline/


# ── Stage output filenames ─────────────────────────────────────────────────────

STAGE_FILES = {
    "generate": "01_raw.json",
    "validate": "02_valid.json",
    "score": "03_scored.json",
    "simulate": "04_simulated.json",
    "dedupe": "05_deduplicated.json",
    "moderate": "05_moderated.json",  # moderate is a step within dedupe_moderate
    "art": "06_art.json",
    "publish": "07_published.json",
}

STAGE_ORDER = [
    "generate",
    "validate",
    "score",
    "simulate",
    "dedupe",
    "moderate",
    "art",
    "publish",
]

STAGE_MODULES = {
    "generate": "modules.generate",
    "validate": "modules.validate",
    "score": "modules.score",
    "simulate": "modules.simulate",
    "dedupe": "modules.dedupe_moderate",
    "moderate": "modules.dedupe_moderate",  # same module, different handling
    "art": "modules.art",
    "publish": "modules.publish",
}

# Which stages share a dedupe+moderate output file
DEDUPE_MODERATE_INPUTS = {"dedupe": "04_simulated.json", "moderate": "05_deduplicated.json"}


# ── Report validation ──────────────────────────────────────────────────────────

def _validate_report(report: dict[str, Any]) -> None:
    """Validate the report for impossible values.

    Asserts monotonic non-increasing card counts through the pipeline:
        valid_count <= seed_count
        scored_count <= valid_count
        simulated_count <= scored_count
        dedupe_count <= simulated_count
        approved_count <= dedupe_count
        reject_count >= 0

    Raises ValueError with a description if any assertion fails.
    """
    seed_count = report.get("seed_count", 0)
    valid_count = report.get("valid_count", 0)
    scored_count = report.get("scored_count", 0)
    simulated_count = report.get("simulated_count", 0)
    dedupe_count = report.get("dedupe_count", 0)
    approved_count = report.get("approved_count", 0)
    reject_count = report.get("reject_count", 0)

    if seed_count < 0:
        raise ValueError(f"IMPOSSIBLE: seed_count={seed_count} is negative")
    if not (valid_count <= seed_count):
        raise ValueError(
            f"IMPOSSIBLE: valid_count={valid_count} > seed_count={seed_count} "
            f"(cards cannot exceed the number seeded)"
        )
    if not (scored_count <= valid_count):
        raise ValueError(
            f"IMPOSSIBLE: scored_count={scored_count} > valid_count={valid_count} "
            f"(scored cards cannot exceed valid cards)"
        )
    if not (simulated_count <= scored_count):
        raise ValueError(
            f"IMPOSSIBLE: simulated_count={simulated_count} > scored_count={scored_count} "
            f"(simulated cards cannot exceed scored cards)"
        )
    if not (dedupe_count <= simulated_count):
        raise ValueError(
            f"IMPOSSIBLE: dedupe_count={dedupe_count} > simulated_count={simulated_count} "
            f"(deduplicated cards cannot exceed simulated cards)"
        )
    if not (approved_count <= dedupe_count):
        raise ValueError(
            f"IMPOSSIBLE: approved_count={approved_count} > dedupe_count={dedupe_count} "
            f"(approved cards cannot exceed deduplicated cards)"
        )
    if reject_count < 0:
        raise ValueError(f"IMPOSSIBLE: reject_count={reject_count} is negative")


# ── Stage runner ───────────────────────────────────────────────────────────────

class StageRunner:
    """Runs pipeline stages as subprocesses, collecting timing and output."""

    def __init__(self, work_dir: Path, seed: dict, skip_api: bool = False):
        self.work_dir = work_dir
        self.seed = seed
        self.batch_id = seed.get("batch_id", f"b_{int(time.time())}")
        self.stratum = seed.get("strata", "EMBER")
        self.skip_api = skip_api
        self.timing: dict[str, float] = {}
        self._failed_stage: str | None = None

    def _stage_input(self, stage: str) -> Path:
        """Determine the input file for a given stage."""
        if stage == "generate":
            # Generate reads from seed file directly
            return Path("__seed__")  # special marker
        if stage in DEDUPE_MODERATE_INPUTS:
            return self.work_dir / DEDUPE_MODERATE_INPUTS[stage]
        # Default: previous stage's output
        idx = STAGE_ORDER.index(stage)
        if idx == 0:
            return Path("__seed__")
        prev_stage = STAGE_ORDER[idx - 1]
        # For dedupe and moderate, use the mapped input
        if stage in DEDUPE_MODERATE_INPUTS:
            return self.work_dir / DEDUPE_MODERATE_INPUTS[stage]
        prev_file = STAGE_FILES.get(prev_stage)
        return self.work_dir / prev_file if prev_file else Path("__none__")

    def _build_args(self, stage: str) -> list[str]:
        """Build CLI arguments for a stage module."""
        cmd = [sys.executable, "-m", STAGE_MODULES[stage]]
        if stage == "generate":
            # Write seed to a temp file for the generate module
            seed_path = self.work_dir / "_seed.json"
            self.work_dir.mkdir(parents=True, exist_ok=True)
            with open(seed_path, "w") as f:
                json.dump(self.seed, f)
            cmd.extend([
                "--seed", str(seed_path),
                "--work-dir", str(self.work_dir),
            ])
        elif stage == "validate":
            cmd.extend([
                "--input", str(self.work_dir / "01_raw.json"),
                "--work-dir", str(self.work_dir),
            ])
        elif stage == "score":
            cmd.extend([
                "--input", str(self.work_dir / "02_valid.json"),
                "--work-dir", str(self.work_dir),
            ])
        elif stage == "simulate":
            cmd.extend([
                "--input", str(self.work_dir / "03_scored.json"),
                "--work-dir", str(self.work_dir),
                "--games", "200",
            ])
        elif stage == "dedupe":
            cmd.extend([
                "--input", str(self.work_dir / "04_simulated.json"),
                "--work-dir", str(self.work_dir),
            ])
        elif stage == "moderate":
            # moderate uses the same module as dedupe but operates on deduped output
            cmd.extend([
                "--input", str(self.work_dir / "05_deduplicated.json"),
                "--work-dir", str(self.work_dir),
                "--moderate-only",
            ])
        elif stage == "art":
            cmd.extend([
                "--input", str(self.work_dir / "05_deduplicated.json"),
                "--work-dir", str(self.work_dir),
            ])
            if self.skip_api:
                cmd.append("--skip-api")
        elif stage == "publish":
            cmd.extend([
                "--work-dir", str(self.work_dir),
                "--set-id", "buried_age",
                "--content-dir", str(PIPELINE / ".." / "content" / "packs"),
            ])
        return cmd

    def run_stage(self, stage: str) -> bool:
        """Run a single stage. Returns True on success."""
        start = time.monotonic()
        cmd = self._build_args(stage)

        print(f"\n{'=' * 64}")
        print(f"[{stage}] Running: {' '.join(str(a) for a in cmd[:4])}...")
        print(f"{'=' * 64}")

        result = subprocess.run(cmd, capture_output=True, text=True, cwd=str(PIPELINE))
        elapsed = time.monotonic() - start
        self.timing[stage] = elapsed

        # Print stdout (up to 50 lines)
        stdout_lines = result.stdout.strip().splitlines()
        for line in stdout_lines[-50:]:
            print(f"  {line}")
        if result.stderr:
            for line in result.stderr.strip().splitlines()[-10:]:
                print(f"  STDERR: {line}", file=sys.stderr)

        if result.returncode != 0:
            print(f"[{stage}] FAILED (exit {result.returncode}) in {elapsed:.1f}s", file=sys.stderr)
            self._failed_stage = stage
            return False

        print(f"[{stage}] OK in {elapsed:.1f}s")
        return True

    def run(self) -> bool:
        """Run all stages in order. Returns True if all succeeded."""
        self.work_dir.mkdir(parents=True, exist_ok=True)

        for stage in STAGE_ORDER:
            ok = self.run_stage(stage)
            if not ok:
                self._failed_stage = stage
                print(f"\n[orchestrate] Pipeline halted at stage '{stage}'")
                return False

        return True

    @property
    def failed_stage(self) -> str | None:
        return self._failed_stage


# ── Report builder ─────────────────────────────────────────────────────────────

def _load_cards(path: Path) -> int:
    """Count cards in a JSON file (list or dict with 'cards' key)."""
    if not path.exists():
        return 0
    try:
        data = json.loads(path.read_text())
        if isinstance(data, list):
            return len(data)
        if isinstance(data, dict) and "cards" in data:
            return len(data["cards"])
        return 1 if data else 0
    except (json.JSONDecodeError, TypeError):
        return 0


def _load_rejects(work_dir: Path) -> int:
    """Count reject files in the rejects directory."""
    rejects_dir = work_dir / "rejects"
    if not rejects_dir.exists():
        return 0
    return len(list(rejects_dir.glob("*.json")))


def build_report(work_dir: Path, runner: StageRunner) -> dict:
    """Build the report summary from stage output files."""
    seed_count = runner.seed.get("count", 0)

    report = {
        "batch_id": runner.batch_id,
        "seed_count": seed_count,
        "valid_count": _load_cards(work_dir / "02_valid.json"),
        "scored_count": _load_cards(work_dir / "03_scored.json"),
        "simulated_count": _load_cards(work_dir / "04_simulated.json"),
        "dedupe_count": _load_cards(work_dir / "05_deduplicated.json"),
        "approved_count": _load_cards(work_dir / "06_art.json"),
        "reject_count": _load_rejects(work_dir),
        "cost_usd": 0.0,  # estimated from API usage, 0 for dry-run
        "duration_seconds": runner.timing.get("total", sum(runner.timing.values())),
        "stages_run": list(runner.timing.keys()),
        "failed_stage": runner.failed_stage,
    }

    return report


# ── CLI ────────────────────────────────────────────────────────────────────────

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — P6-10 stage orchestration",
    )
    parser.add_argument("--seed", required=True,
                        help="Path to seed file (e.g. pipeline/seeds/ember_01.json)")
    parser.add_argument("--work-dir", default=None,
                        help="Output work directory (default: pipeline/work/<batch_id>)")
    parser.add_argument("--skip-api", action="store_true",
                        help="Skip API calls in ART stage (for testing/dry-run)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    # Load seed
    seed_path = Path(args.seed)
    if not seed_path.exists():
        print(f"[orchestrate] ERROR: seed file not found: {seed_path}", file=sys.stderr)
        return 1
    with open(seed_path) as f:
        seed = json.load(f)

    batch_id = seed.get("batch_id", f"b_{int(time.time())}")

    # Resolve work directory
    if args.work_dir:
        work_dir = Path(args.work_dir)
    else:
        work_dir = PIPELINE / "work" / batch_id

    # Create runner and run all stages
    runner = StageRunner(work_dir, seed, skip_api=args.skip_api)
    runner.timing["start"] = time.monotonic()

    all_ok = runner.run()

    runner.timing["total"] = time.monotonic() - runner.timing.pop("start")

    # Build and validate report
    report = build_report(work_dir, runner)
    try:
        _validate_report(report)
    except ValueError as e:
        print(f"[orchestrate] REPORT VALIDATION FAILED: {e}", file=sys.stderr)
        return 3

    # Write report.json
    report_path = work_dir / "report.json"
    with open(report_path, "w") as f:
        json.dump(report, f, indent=2)
    print(f"[orchestrate] Report written to {report_path}")

    # Print summary
    print(f"\n{'=' * 64}")
    print(f"  P6-10 PIPELINE SUMMARY: {batch_id}")
    print(f"{'=' * 64}")
    print(f"  Seed:            {seed_path.name} ({report['seed_count']} cards)")
    print(f"  Work directory:  {work_dir}")
    for key in ("valid_count", "scored_count", "simulated_count",
                "dedupe_count", "approved_count"):
        print(f"  {key:16s} {report[key]}")
    print(f"  Reject files:    {report['reject_count']}")
    print(f"  Duration:        {report['duration_seconds']:.1f}s")
    print(f"  Estimated cost:  ${report['cost_usd']:.4f}")
    if report.get("failed_stage"):
        print(f"\n  ❌ FAILED at stage: {report['failed_stage']}")
    else:
        print(f"\n  ✓ All stages completed successfully")
    print("=" * 64)

    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())