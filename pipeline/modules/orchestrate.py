#!/usr/bin/env python3
"""P6-10/P6-11: Pipeline orchestration — runs all stages end-to-end for a seed file.

Usage:
    python -m modules.orchestrate --seed pipeline/seeds/ember_01.json \\
        --work-dir pipeline/work/b_2026_ember_01 [--skip-api]

Each stage is invoked as a subprocess matching the per-stage CLI pattern.
On stage failure, logs the error and stops the pipeline.

Stages in order: GENERATE → VALIDATE → SCORE → SIMULATE → DEDUPE → MODERATE → MERGE → ART → PUBLISH
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
    "moderate": "05_moderated.json",
    "merge": "05_deduplicated.json",  # merge rewrites the same file
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
    "merge",
    "art",
    "publish",
]

STAGE_MODULES = {
    "generate": "modules.generate",
    "validate": "modules.validate",
    "score": "modules.score",
    "simulate": "modules.simulate",
    "dedupe": "modules.dedupe_moderate",
    "moderate": "modules.dedupe_moderate",
    "merge": "__internal__",  # handled internally, not as subprocess
    "art": "modules.art",
    "publish": "modules.publish",
}

DEDUPE_MODERATE_INPUTS = {"dedupe": "04_simulated.json", "moderate": "05_deduplicated.json"}


# ── Card-def merge (Fix 1: P6-11 schema continuity) ───────────────────────────

def _merge_card_defs(work_dir: Path) -> None:
    """
    Merge full CardDef fields from 02_valid.json into 05_deduplicated.json.
    The simulate and dedupe stages output result-only objects keyed by card_id.
    ART and PUBLISH need the full CardDef. This merge restores it.
    Result: each entry in 05_deduplicated.json becomes the full CardDef with
    nested 'simulation' and 'dedupe' fields attached.
    """
    valid_path = work_dir / "02_valid.json"
    deduped_path = work_dir / "05_deduplicated.json"
    if not valid_path.exists() or not deduped_path.exists():
        return

    try:
        valid_cards = json.loads(valid_path.read_text())
        deduped = json.loads(deduped_path.read_text())
    except (json.JSONDecodeError, OSError):
        return

    if not isinstance(valid_cards, list) or not isinstance(deduped, list):
        return

    # Build lookup by id
    card_by_id: dict[str, dict] = {}
    for c in valid_cards:
        if isinstance(c, dict) and "id" in c:
            card_by_id[c["id"]] = c

    merged: list[dict] = []
    for entry in deduped:
        if not isinstance(entry, dict):
            merged.append(entry)  # type: ignore[arg-type]
            continue
        card_id = entry.get("id") or entry.get("card_id")
        base = card_by_id.get(str(card_id) if card_id else None, {})  # type: ignore[union-attr]
        if base:
            merged_card = dict(base)  # full CardDef
            # Attach sim metadata without clobbering CardDef fields
            sim_fields = {
                k: v for k, v in entry.items()
                if k not in ("id", "name", "strata", "type", "rarity", "cost")
            }
            if "matchup_results" in entry or "avg_delta" in entry:
                merged_card["simulation"] = sim_fields
            merged_card["dedupe"] = {"passed": True}
            merged.append(merged_card)
        else:
            merged.append(entry)  # unknown id — pass through unchanged

    deduped_path.write_text(json.dumps(merged, indent=2))


# ── Report validation (Fix 2: P6-11 hardening) ────────────────────────────────

def _validate_report(report: dict[str, Any]) -> None:
    """Validate the report for impossible values.

    Asserts monotonic non-increasing card counts through the pipeline:
        validated_count <= seeded_count
        scored_count <= validated_count
        simulated_count <= scored_count
        dedupe_count <= simulated_count
        approved_count <= dedupe_count
        reject_count >= 0

    Also asserts pack metadata invariants:
        pack_version >= 1 if present
        pack_hash is a 64-char hex string if present
        cost_usd >= 0

    Raises ValueError with a description if any assertion fails.
    """
    seeded_count = report.get("seeded_count", 0)
    valid_count = report.get("valid_count", 0)
    scored_count = report.get("scored_count", 0)
    simulated_count = report.get("simulated_count", 0)
    dedupe_count = report.get("dedupe_count", 0)
    approved_count = report.get("approved_count", 0)
    reject_count = report.get("reject_count", 0)

    if seeded_count < 0:
        raise ValueError(f"IMPOSSIBLE: seeded_count={seeded_count} is negative")
    if not (valid_count <= seeded_count):
        raise ValueError(
            f"IMPOSSIBLE: valid_count={valid_count} > seeded_count={seeded_count} "
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

    # cost_usd must be >= 0
    if report.get("cost_usd", 0) < 0:
        raise ValueError(
            f"IMPOSSIBLE: cost_usd ({report['cost_usd']}) is negative"
        )

    # pack_version must be >= 1 if present
    if "pack_version" in report and report["pack_version"] < 1:
        raise ValueError(
            f"IMPOSSIBLE: pack_version ({report['pack_version']}) < 1"
        )

    # pack_hash must be 64-char hex if present
    if "pack_hash" in report:
        h = report["pack_hash"]
        if not (isinstance(h, str) and len(h) == 64
                and all(c in "0123456789abcdef" for c in h)):
            raise ValueError(
                f"IMPOSSIBLE: pack_hash '{h}' is not a valid 64-char hex SHA-256"
            )


# ── Stage runner ───────────────────────────────────────────────────────────────

class StageRunner:
    """Runs pipeline stages as subprocesses, collecting timing and output."""

    def __init__(self, work_dir: Path, seed: dict, skip_api: bool = False, games: int = 10):
        self.work_dir = work_dir
        self.seed = seed
        self.batch_id = seed.get("batch_id", f"b_{int(time.time())}")
        self.stratum = seed.get("strata", "EMBER")
        self.skip_api = skip_api
        self.games = games
        self.content_dir = PIPELINE / ".." / "content" / "packs"
        self.timing: dict[str, float] = {}
        self._failed_stage: str | None = None

    def _build_args(self, stage: str) -> list[str]:
        """Build CLI arguments for a stage module."""
        cmd = [sys.executable, "-m", STAGE_MODULES[stage]]
        if stage == "generate":
            seed_path = self.work_dir / "_seed.json"
            self.work_dir.mkdir(parents=True, exist_ok=True)
            with open(seed_path, "w") as f:
                json.dump(self.seed, f)
            cmd.extend(["--seed", str(seed_path), "--work-dir", str(self.work_dir)])
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
                "--games", str(self.games),
            ])
        elif stage == "dedupe":
            cmd.extend([
                "--input", str(self.work_dir / "04_simulated.json"),
                "--work-dir", str(self.work_dir),
            ])
        elif stage == "moderate":
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
                "--content-dir", str(self.content_dir),
            ])
        return cmd

    def run_stage(self, stage: str) -> bool:
        """Run a single stage subprocess. Returns True on success.

        The 'merge' stage is handled internally (not a subprocess).
        """
        start = time.monotonic()

        # Internal merge step
        if stage == "merge":
            print(f"\n{'=' * 64}")
            print("[merge] Merging CardDef from 02_valid.json into 05_deduplicated.json")
            print(f"{'=' * 64}")
            _merge_card_defs(self.work_dir)
            before = len(json.loads((self.work_dir / "05_deduplicated.json").read_text())) \
                if (self.work_dir / "05_deduplicated.json").exists() else 0
            elapsed = time.monotonic() - start
            self.timing[stage] = elapsed
            print(f"[merge] OK — {before} entries in {elapsed:.1f}s")
            return True

        cmd = self._build_args(stage)
        print(f"\n{'=' * 64}")
        print(f"[{stage}] Running: {' '.join(str(a) for a in cmd[:4])}...")
        print(f"{'=' * 64}")

        result = subprocess.run(cmd, capture_output=True, text=True, cwd=str(PIPELINE))
        elapsed = time.monotonic() - start
        self.timing[stage] = elapsed

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

        # For publish stage, capture pack metadata to work dir
        if stage == "publish":
            self._capture_publish_metadata()

        print(f"[{stage}] OK in {elapsed:.1f}s")
        return True

    def _capture_publish_metadata(self) -> None:
        """Read the published pack file and write a 07_published.json summary."""
        set_id = "buried_age"
        pack_path = self.content_dir / f"{set_id}.json"
        if pack_path.exists():
            try:
                pack = json.loads(pack_path.read_text())
                summary = {
                    "version": pack.get("version", 1),
                    "hash": pack.get("hash", ""),
                    "card_count": len(pack.get("cards", [])),
                    "set_id": set_id,
                    "pack_path": str(pack_path),
                }
                out_path = self.work_dir / "07_publish_metadata.json"
                out_path.write_text(json.dumps(summary, indent=2))
            except (json.JSONDecodeError, OSError):
                pass

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
    seeded_count = runner.seed.get("count", 0)

    report: dict[str, Any] = {
        "batch_id": runner.batch_id,
        "seeded_count": seeded_count,
        "valid_count": _load_cards(work_dir / "02_valid.json"),
        "scored_count": _load_cards(work_dir / "03_scored.json"),
        "simulated_count": _load_cards(work_dir / "04_simulated.json"),
        "dedupe_count": _load_cards(work_dir / "05_deduplicated.json"),
        "approved_count": _load_cards(work_dir / "06_art.json"),
        "reject_count": _load_rejects(work_dir),
        "cost_usd": 0.0,
        "duration_seconds": runner.timing.get("total", sum(runner.timing.values())),
        "stages_run": list(runner.timing.keys()),
        "failed_stage": runner.failed_stage,
    }

    # Read pack metadata from publish output
    publish_path = work_dir / "07_publish_metadata.json"
    if publish_path.exists():
        try:
            pub = json.loads(publish_path.read_text())
            report["pack_version"] = pub.get("version", 1)
            report["pack_hash"] = pub.get("hash", "")
        except (json.JSONDecodeError, OSError):
            pass

    return report


# ── CLI ────────────────────────────────────────────────────────────────────────

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — P6-10/P6-11 stage orchestration",
    )
    parser.add_argument("--seed", required=True,
                        help="Path to seed file (e.g. pipeline/seeds/ember_01.json)")
    parser.add_argument("--work-dir", default=None,
                        help="Output work directory (default: pipeline/work/<batch_id>)")
    parser.add_argument("--skip-api", action="store_true",
                        help="Skip API calls in ART stage (for testing/dry-run)")
    parser.add_argument("--games", type=int, default=10,
                        help="Games per matchup for simulate stage (default: 10)")
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
    runner = StageRunner(work_dir, seed, skip_api=args.skip_api, games=args.games)
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
    print(f"  P6-11 PIPELINE SUMMARY: {batch_id}")
    print(f"{'=' * 64}")
    print(f"  Seed:            {seed_path.name} ({report['seeded_count']} cards)")
    print(f"  Work directory:  {work_dir}")
    for key in ("valid_count", "scored_count", "simulated_count",
                "dedupe_count", "approved_count"):
        print(f"  {key:16s} {report[key]}")
    print(f"  Reject files:    {report['reject_count']}")
    print(f"  Duration:        {report['duration_seconds']:.1f}s")
    print(f"  Estimated cost:  ${report['cost_usd']:.4f}")
    if "pack_version" in report:
        print(f"  Pack version:    v{report['pack_version']}")
    if "pack_hash" in report:
        print(f"  Pack hash:       {report['pack_hash'][:16]}...")
    if report.get("failed_stage"):
        print(f"\n  ❌ FAILED at stage: {report['failed_stage']}")
    else:
        print(f"\n  ✓ All stages completed successfully")
    print("=" * 64)

    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())