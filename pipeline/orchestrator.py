#!/usr/bin/env python3
"""P6-10: Pipeline orchestration — runs all stages end-to-end for one 60-card set.

Usage:
    python pipeline/orchestrator.py --stratum EMBER --count 60 \\
        --work-dir work/b_2026_ember_e2e

Reports:
    - Rejection rate per stage + top reason codes
    - Total wall-clock time
    - Total cost (text gen vs. image gen)
    - Art fallback / commission queue count
    - Final publish-ready card count
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
ROOT = HERE.parent
PIPELINE = HERE

# ── Stage output files ───────────────────────────────────────────────────────

STAGE_FILES = {
    "generate": "01_raw.json",
    "validate": "02_valid.json",
    "score": "03_scored.json",
    "simulate": "04_simulated.json",
    "dedupe_moderate": "05_deduplicated.json",
    "art": "06_art.json",
}

# ── Cost tracking ─────────────────────────────────────────────────────────────

class CostTracker:
    def __init__(self):
        self.text_gen_cost = 0.0  # estimate from tokens
        self.image_gen_cost = 0.0
        self.text_prompt_tokens = 0
        self.text_completion_tokens = 0
        self.image_calls = 0
        self.image_successes = 0
        self.image_failures = 0

    def record_text(self, prompt_tokens: int, completion_tokens: int):
        """Estimate cost at ~$0.15/M input, ~$0.60/M output (GPT-4o-mini rates)."""
        self.text_prompt_tokens += prompt_tokens
        self.text_completion_tokens += completion_tokens
        input_cost = prompt_tokens * 0.15 / 1_000_000
        output_cost = completion_tokens * 0.60 / 1_000_000
        self.text_gen_cost += input_cost + output_cost

    def record_image(self, success: bool):
        """FLUX.2-pro is ~$0.05 per image."""
        self.image_calls += 1
        if success:
            self.image_successes += 1
            self.image_gen_cost += 0.05
        else:
            self.image_failures += 1
            self.image_gen_cost += 0.0  # failed calls don't cost? They might still bill...

    @property
    def total(self) -> float:
        return self.text_gen_cost + self.image_gen_cost

    def summary(self) -> str:
        return (
            f"  Text generation:  ${self.text_gen_cost:.4f} "
            f"({self.text_prompt_tokens:,} in / {self.text_completion_tokens:,} out)\n"
            f"  Image generation: ${self.image_gen_cost:.4f} "
            f"({self.image_successes} succ / {self.image_failures} fail)\n"
            f"  Total cost:       ${self.total:.4f}"
        )


# ── Rejection tracker ────────────────────────────────────────────────────────

class RejectionTracker:
    def __init__(self):
        self.rejects: dict[str, dict[str, int]] = {}  # stage -> {reason: count}
        self.processed: dict[str, int] = {}

    def note(self, stage: str, total: int, rejects: list[tuple[str, str]]):
        """Record rejects for a stage. Each tuple is (card_id, reason_code)."""
        self.processed[stage] = total
        if stage not in self.rejects:
            self.rejects[stage] = {}
        for _, reason in rejects:
            # Extract the primary reason code (first word or prefix before colon)
            code = reason.split(":")[0].split(";")[0].strip()
            self.rejects[stage][code] = self.rejects[stage].get(code, 0) + 1

    def summary(self) -> str:
        lines = []
        for stage in ["generate", "validate", "score", "simulate", "dedupe_moderate"]:
            total = self.processed.get(stage, 0)
            stage_rejects = self.rejects.get(stage, {})
            reject_count = sum(stage_rejects.values())
            rate = reject_count / total * 100 if total > 0 else 0
            lines.append(f"\n  {stage}: {reject_count}/{total} rejected ({rate:.1f}%)")
            if stage_rejects:
                for code, count in sorted(stage_rejects.items(), key=lambda x: -x[1]):
                    lines.append(f"    {code}: {count}")
        return "\n".join(lines)


# ── Stage runners ─────────────────────────────────────────────────────────────

class PipelineRunner:
    def __init__(self, work_dir: Path, stratum: str, count: int,
                 cost: CostTracker, rejects: RejectionTracker):
        self.work_dir = work_dir
        self.stratum = stratum
        self.count = count
        self.cost = cost
        self.rejects = rejects
        self.timing: dict[str, float] = {}
        self.start_time = 0.0

    def run_python(self, stage: str, module: str, extra_args: list[str] | None = None) -> bool:
        """Run a Python module stage and time it."""
        start = time.monotonic()
        cmd = [
            sys.executable, "-m", module,
            *self._args_for(stage),
            *(extra_args or []),
        ]
        print(f"\n{'='*64}")
        print(f"[{stage}] {' '.join(cmd[:6])}...")
        print(f"{'='*64}")

        result = subprocess.run(cmd, capture_output=True, text=True, cwd=str(PIPELINE))
        elapsed = time.monotonic() - start
        self.timing[stage] = elapsed

        print(result.stdout)
        if result.stderr:
            print(f"[{stage}] STDERR:", result.stderr[-2000:] if len(result.stderr) > 2000 else result.stderr, file=sys.stderr)

        if result.returncode != 0:
            print(f"[{stage}] FAILED (exit {result.returncode})", file=sys.stderr)
            return False

        # Try to parse rejections from summary or reject files
        self._collect_rejects(stage)

        return True

    def _args_for(self, stage: str) -> list[str]:
        stage_config = {
            "generate": [
                "--seed", str(PIPELINE / "seeds" / f"{self.stratum.lower()}_{self.count}.json"),
                "--work-dir", str(self.work_dir),
            ],
            "validate": [
                "--input", str(self.work_dir / STAGE_FILES["generate"]),
                "--work-dir", str(self.work_dir),
            ],
            "score": [
                "--input", str(self.work_dir / STAGE_FILES["validate"]),
                "--work-dir", str(self.work_dir),
            ],
            "simulate": [
                "--input", str(self.work_dir / STAGE_FILES["score"]),
                "--work-dir", str(self.work_dir),
                "--games", "200",
            ],
            "dedupe_moderate": [
                "--input", str(self.work_dir / STAGE_FILES["simulate"]),
                "--work-dir", str(self.work_dir),
            ],
            "art": [
                "--input", str(self.work_dir / STAGE_FILES["dedupe_moderate"]),
                "--work-dir", str(self.work_dir),
            ],
        }
        args = stage_config.get(stage, [])
        return args

    def _collect_rejects(self, stage: str):
        """Read reject files from work_dir/rejects/ for this stage."""
        rejects_dir = self.work_dir / "rejects"
        if not rejects_dir.exists():
            return
        prefix_map = {
            "generate": "reject_generate",
            "validate": "reject_",
            "score": "reject_score",
            "simulate": "reject_sim",
            "dedupe_moderate": "reject_",
        }
        prefix = prefix_map.get(stage, "")
        if not prefix:
            return
        stage_rejects: list[tuple[str, str]] = []
        for rfile in sorted(rejects_dir.iterdir()):
            if not rfile.name.startswith(prefix):
                continue
            try:
                data = json.loads(rfile.read_text())
                card_id = data.get("card", {}).get("id", "?") if "card" in data else "?"
                reason = data.get("reason", "UNKNOWN")
                stage_rejects.append((card_id, reason))
            except (json.JSONDecodeError, KeyError):
                pass

        # Also check summary if available
        summary_file = self.work_dir / f"{self._stage_summary(stage)}"
        rejected_count = len(stage_rejects)
        total = 0
        if summary_file.exists():
            try:
                summary = json.loads(summary_file.read_text())
                total = summary.get("total_processed", summary.get("total", len(stage_rejects)))
            except (json.JSONDecodeError, KeyError):
                total = len(stage_rejects)

        self.rejects.note(stage, total, stage_rejects)

    def _stage_summary(self, stage: str) -> str:
        summaries = {
            "generate": "01_summary.json",
            "validate": "02_summary.json",
            "score": "03_summary.json",
            "simulate": "04_summary.json",
            "dedupe_moderate": "05_summary.json",
        }
        return summaries.get(stage, "")

    def run(self) -> bool:
        self.start_time = time.monotonic()
        success = True

        # Stage 1: Generate
        if not self.run_python("generate", "modules.generate"):
            print("[orchestrator] GENERATE failed — halting", file=sys.stderr)
            success = False

        # Stage 2: Validate
        if success and not self.run_python("validate", "modules.validate"):
            print("[orchestrator] VALIDATE failed — halting", file=sys.stderr)
            success = False

        # Stage 3: Score
        if success and not self.run_python("score", "modules.score"):
            print("[orchestrator] SCORE failed — halting", file=sys.stderr)
            success = False

        # Stage 4: Simulate
        if success:
            sim_ok = self.run_python("simulate", "modules.simulate")
            if not sim_ok:
                print("[orchestrator] SIMULATE failed — continuing with results", file=sys.stderr)
                # Don't halt — simulation can partially succeed

        # Stage 5: Dedupe + Moderate
        if success and not self.run_python("dedupe_moderate", "modules.dedupe_moderate"):
            print("[orchestrator] DEDUPE+MODERATE failed — halting", file=sys.stderr)
            success = False

        # Stage 6: Merge card data back for art stage
        # The simulate+dudepe stages strip card definitions — we need to
        # merge the original card data (from the latest stage that had it)
        # into the deduped output so art has names, strata, and prompts.
        if success and (self.work_dir / STAGE_FILES["dedupe_moderate"]).exists():
            self._merge_card_data_for_art()

        # Stage 7: Art
        if success:
            art_ok = self.run_python("art", "modules.art")
            if not art_ok:
                print("[orchestrator] ART failed — continuing with fallback frames", file=sys.stderr)

        self.timing["total"] = time.monotonic() - self.start_time
        return success

    def _merge_card_data_for_art(self):
        """Merge original card definitions into the deduped output for the art stage.

        The simulate and dedupe stages operate on sim-result objects (which have
        card_id, card_name, matchup_results, etc.) rather than full card definitions.
        The art stage needs full card definitions (name, strata, art.prompt, etc.).

        This method merges the latest available card definitions (from 02_valid.json
        or fall back to 01_raw.json) into the dedup/sim output based on card_id.
        """
        dedup_path = self.work_dir / "05_deduplicated.json"
        if not dedup_path.exists():
            return
        dedup_data = json.loads(dedup_path.read_text())
        if not isinstance(dedup_data, list):
            return

        # Find the latest source of full card definitions
        card_source = None
        for src in ["02_valid.json", "01_raw.json"]:
            p = self.work_dir / src
            if p.exists():
                raw = json.loads(p.read_text())
                if isinstance(raw, list) and len(raw) > 0:
                    # Check if it has full card data (not sim-result format)
                    if "name" in raw[0] or "strata" in raw[0]:
                        card_source = {c.get("id", c.get("card_id", "")): c for c in raw if c.get("id") or c.get("card_id")}
                        break

        if not card_source:
            print("[orchestrator] WARNING: No card definitions found to merge for art stage",
                  file=sys.stderr)
            return

        # Merge: for each entry in dedup_data, attach full card definition
        merged = []
        for entry in dedup_data:
            cid = entry.get("card_id") or entry.get("id", "")
            if cid and cid in card_source:
                card_def = dict(card_source[cid])
                # Preserve sim results as metadata on the card
                sim_fields = {k: v for k, v in entry.items()
                              if k not in ("card_id", "card_name", "id", "name")}
                card_def["simulation"] = sim_fields
                merged.append(card_def)
            else:
                merged.append(entry)  # pass through if no match

        dedup_path.write_text(json.dumps(merged, indent=2))
        print(f"[orchestrator] Merged card definitions into 05_deduplicated.json "
              f"({len(merged)} cards from {len(card_source)} source)")


# ── Seed creation ─────────────────────────────────────────────────────────────

def create_seed(stratum: str, count: int) -> Path:
    """Create a seed file for the given stratum and card count."""
    seeds_dir = PIPELINE / "seeds"
    seeds_dir.mkdir(parents=True, exist_ok=True)

    type_mix = {"CREATURE": count * 2 // 3, "RITUAL": count // 4, "RELIC": count // 10}
    # Flatten to match exactly
    type_sum = sum(type_mix.values())
    type_mix["CREATURE"] += count - type_sum

    rarity_mix = {"COMMON": count // 2, "UNCOMMON": count // 3, "RARE": count // 6, "RELIC": max(3, count // 20)}
    rarity_sum = sum(rarity_mix.values())
    rarity_mix["COMMON"] += count - rarity_sum

    seed = {
        "batch_id": f"b_2026_ember_e2e",
        "count": count,
        "strata": stratum,
        "type_mix": type_mix,
        "cost_curve": {
            "1": 5, "2": 10, "3": 12, "4": 10, "5": 8,
            "6": 6, "7": 4, "8": 5,
        },
        "rarity_mix": rarity_mix,
        "theme": (
            "Cinderhold — forge city built in a volcanic caldera, "
            "ash, embers, molten iron, things forged in the deep. "
            "A civilization that learned to shape fire before it learned to build walls."
        ),
        "mechanic_emphasis": ["SWIFT", "PIERCE", "BURY"],
        "forbidden_mechanics": ["EXCAVATE"],
    }

    seed_path = seeds_dir / f"{stratum.lower()}_{count}.json"
    with open(seed_path, "w") as f:
        json.dump(seed, f, indent=2)
    print(f"[seed] Created: {seed_path}")
    return seed_path


# ── Summary reporter ──────────────────────────────────────────────────────────

def collect_results(work_dir: Path, cost: CostTracker, rejects: RejectionTracker,
                    timing: dict[str, float]) -> dict[str, Any]:
    """Gather final statistics from all stage outputs."""
    results: dict[str, Any] = {
        "stages": {},
        "art_fallbacks": 0,
        "commission_queue": 0,
        "publish_ready": 0,
        "total_seeded": 0,
    }

    # Count cards at each stage
    for stage, filename in STAGE_FILES.items():
        path = work_dir / filename
        if path.exists():
            try:
                data = json.loads(path.read_text())
                cards = data if isinstance(data, list) else [data]
                results["stages"][stage] = len(cards)
            except (json.JSONDecodeError, TypeError):
                results["stages"][stage] = 0
        else:
            results["stages"][stage] = 0

    # Count art fallbacks
    art_path = work_dir / "06_art.json"
    if art_path.exists():
        try:
            art_cards = json.loads(art_path.read_text())
            if isinstance(art_cards, list):
                results["art_fallbacks"] = sum(
                    1 for c in art_cards
                    if c.get("art", {}).get("fallback", False)
                )
        except (json.JSONDecodeError, TypeError):
            pass

    # Count commission queue entries
    cq_path = ROOT / "docs" / "ART_COMMISSION_QUEUE.md"
    if cq_path.exists():
        results["commission_queue"] = cq_path.read_text().count("- [ ]")

    # Total seeded
    seed_guess = rejects.processed.get("generate", 60)
    results["total_seeded"] = seed_guess

    # Publish-ready = cards that made it to art stage
    results["publish_ready"] = results["stages"].get("art", 0)

    return results


def print_report(work_dir: Path, cost: CostTracker, rejects: RejectionTracker,
                 timing: dict[str, float]):
    """Print the full P6-10 acceptance report."""
    results = collect_results(work_dir, cost, rejects, timing)

    print()
    print("=" * 64)
    print("  P6-10 ACCEPTANCE REPORT: EMBER 60-Card Set")
    print("=" * 64)

    # 1. Rejection rates
    print(f"\n## 1. Stage-by-stage rejection")
    print(f"   (DoD: <15% at validate stage)")
    print(rejects.summary())

    # 2. Timing
    print(f"\n## 2. Wall-clock time")
    for stage, sec in sorted(timing.items()):
        if stage == "total":
            continue
        print(f"  {stage:20s} {sec:.0f}s")
    print(f"  {'TOTAL':20s} {timing.get('total', 0):.0f}s")

    # 3. Cost
    print(f"\n## 3. Cost")
    print(cost.summary())

    # 4. Art summary
    print(f"\n## 4. Art pipeline")
    print(f"  Fallback frames: {results['art_fallbacks']}")
    print(f"  Commission queue: {results['commission_queue']}")

    # 5. Final count
    print(f"\n## 5. Final card count")
    print(f"  Seeded: {results['total_seeded']}")
    for stage, count in results["stages"].items():
        print(f"  {stage:20s} {count}")
    print(f"  {'PUBLISH-READY':20s} {results['publish_ready']}")

    print()
    stage6_rate = rejects.processed.get("validate", 0)
    stage6_rejects = sum(rejects.rejects.get("validate", {}).values())
    validate_reject_pct = stage6_rejects / stage6_rate * 100 if stage6_rate > 0 else 0
    if validate_reject_pct < 15:
        print(f"  ✓ DoD MET: validate rejection {validate_reject_pct:.1f}% (<15%)")
    else:
        print(f"  ✗ DoD NOT MET: validate rejection {validate_reject_pct:.1f}% (target <15%)")
        print(f"    See reasons above — fix the generation prompt, don't loosen the validator")
    print("=" * 64)
    print()


# ── CLI ───────────────────────────────────────────────────────────────────────

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — P6-10 orchestration",
    )
    parser.add_argument("--stratum", default="EMBER",
                        help="Stratum to generate (default: EMBER)")
    parser.add_argument("--count", type=int, default=60,
                        help="Number of cards to generate (default: 60)")
    parser.add_argument("--work-dir", required=True,
                        help="Work directory for this run")
    parser.add_argument("--skip-art", action="store_true",
                        help="Skip ART stage (use fallback frames only)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    work_dir = Path(args.work_dir)
    work_dir.mkdir(parents=True, exist_ok=True)

    cost = CostTracker()
    rejects = RejectionTracker()
    runner = PipelineRunner(work_dir, args.stratum, args.count, cost, rejects)

    # Create seed
    seed_path = create_seed(args.stratum, args.count)
    if not seed_path.exists():
        print(f"[orchestrator] Failed to create seed", file=sys.stderr)
        return 1

    # Run all stages
    success = runner.run()

    # Print report
    print_report(work_dir, cost, rejects, runner.timing)

    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())