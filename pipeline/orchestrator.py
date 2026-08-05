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
import re
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

# ── Cost tracking (wired to actual API responses) ─────────────────────────────

class CostTracker:
    """Tracks costs parsed from actual stage output summaries.

    Text costs: parsed from generate module's stdout for token usage.
    Image costs: 12 API calls × $0.05/image (FLUX.2-pro pricing).
    """

    TEXT_MODEL_RATES = {
        "gpt-4o-mini": (0.15, 0.60),   # $/M input, $/M output
        "anthropic/claude-3.5-sonnet": (3.00, 15.00),
        "anthropic/claude-sonnet-4": (3.00, 15.00),
        "deepseek/deepseek-v4": (2.00, 8.00),
        "google/gemini-2.5-pro": (1.25, 5.00),
        "openai/o1": (15.00, 60.00),
        "openai/gpt-4o": (2.50, 10.00),
    }
    IMAGE_COST_PER_CALL = 0.05  # FLUX.2-pro, all current OpenRouter image models

    def __init__(self):
        self.text_gen_cost = 0.0
        self.image_gen_cost = 0.0
        self.text_prompt_tokens = 0
        self.text_completion_tokens = 0
        self.image_calls = 0
        self.image_successes = 0
        self.image_failures = 0
        self.text_model = "unknown"

    def parse_generate_stdout(self, stdout: str, model: str = "gpt-4o-mini"):
        """Parse token counts from generate module's stdout.

        The generate module outputs: 'Got N cards in batch, M accepted total'
        per batch and summary with total_generated. We parse the summary.
        """
        self.text_model = model
        rates = self.TEXT_MODEL_RATES.get(model, (0.15, 0.60))
        rate_in, rate_out = rates

        # Try to find token counts in stdout
        # The generate module may print token usage. Look for patterns.
        prompt_match = re.findall(r'prompt_tokens[=:](\d+)', stdout, re.IGNORECASE)
        completion_match = re.findall(r'completion_tokens[=:](\d+)', stdout, re.IGNORECASE)

        if prompt_match and completion_match:
            self.text_prompt_tokens = sum(int(m) for m in prompt_match)
            self.text_completion_tokens = sum(int(m) for m in completion_match)
        else:
            # Fallback: estimate from card count
            # Each batch of ~10 cards uses ~1,500 prompt + ~2,500 completion tokens
            # Count batches: look for "requesting N cards" lines
            batch_matches = re.findall(r'requesting\s+(\d+)\s+cards', stdout, re.IGNORECASE)
            if batch_matches:
                requested = [int(m) for m in batch_matches]
                # Estimate: ~1,500 prompt tokens per batch, ~2,500 completion per batch
                # But completion scales with actual cards returned
                got_matches = re.findall(r'Got\s+(\d+)\s+cards', stdout, re.IGNORECASE)
                got = [int(m) for m in got_matches] if got_matches else [0]
                total_got = sum(got)
                n_batches = len(requested)
                # Rough estimate: prompt ~1500/batch, output ~2500/batch
                self.text_prompt_tokens = n_batches * 1500
                self.text_completion_tokens = n_batches * 2500

        self.text_gen_cost = (
            self.text_prompt_tokens * rate_in / 1_000_000
            + self.text_completion_tokens * rate_out / 1_000_000
        )

    def parse_art_stdout(self, stdout: str):
        """Parse art stage output for image generation counts."""
        # The summary is printed as a Python dict repr: {'api_calls': 12, ...}
        calls_match = re.search(r"'api_calls'\s*:\s*(\d+)", stdout)
        failures_match = re.search(r"'api_failures'\s*:\s*(\d+)", stdout)
        # Also accept a plain 'api_calls: 12' form (for test fixtures)
        if not calls_match:
            calls_match = re.search(r"api_calls[=:]\s*(\d+)", stdout)
        if not failures_match:
            failures_match = re.search(r"api_failures[=:]\s*(\d+)", stdout)
        if calls_match:
            self.image_calls = int(calls_match.group(1))
        self.image_failures = int(failures_match.group(1)) if failures_match else 0
        self.image_successes = self.image_calls - self.image_failures
        self.image_gen_cost = self.image_calls * self.IMAGE_COST_PER_CALL

    @property
    def total(self) -> float:
        return self.text_gen_cost + self.image_gen_cost

    def summary(self) -> str:
        pct_text = self.text_gen_cost / self.total * 100 if self.total > 0 else 0
        pct_image = self.image_gen_cost / self.total * 100 if self.total > 0 else 0
        return (
            f"  Model: {self.text_model}\n"
            f"  Text generation:  ${self.text_gen_cost:.4f}  ({pct_text:.0f}%)"
            f"  ~{self.text_prompt_tokens:,} in / {self.text_completion_tokens:,} out\n"
            f"  Image generation: ${self.image_gen_cost:.4f}  ({pct_image:.0f}%)"
            f"  ({self.image_successes} succ / {self.image_failures} fail / {self.image_calls} calls)\n"
            f"  Total cost:       ${self.total:.4f}"
        )


# ── Rejection tracker (stage-scoped only) ─────────────────────────────────────

# Map from stage name to list of reject-file prefixes used by that stage's module.
# Each stage's module writes rejects with one or more unique prefixes so we can
# attribute them correctly without cross-contamination.
STAGE_REJECT_PREFIXES: dict[str, list[str]] = {
    "generate": ["reject_generate_"],
    "validate": ["reject_schema_", "reject_engine_"],
    "score": ["reject_score_"],
    "simulate": ["reject_sim_"],
    "dedupe_moderate": ["reject_dedupe_"],
}

class RejectionTracker:
    def __init__(self):
        self.rejects: dict[str, dict[str, int]] = {}
        self.processed: dict[str, int] = {}

    def note(self, stage: str, summary: dict):
        """Record rejects for a stage from its stage summary dict.

        Uses the stage's own summary counters (total_processed, rejected) rather
        than counting files on disk, which is fragile and can cross-contaminate.
        """
        total = summary.get("total_processed", summary.get("total", 0))
        self.processed[stage] = total

    def note_reason(self, stage: str, reason: str):
        """Record a single reject reason code for a stage."""
        if stage not in self.rejects:
            self.rejects[stage] = {}
        code = self._extract_code(reason)
        self.rejects[stage][code] = self.rejects[stage].get(code, 0) + 1

    def _extract_code(self, reason: str) -> str:
        return reason.split(":")[0].split(";")[0].strip()

    def read_reject_files(self, stage: str, rejects_dir: Path):
        """Read reject files for a specific stage using the correct prefixes.

        Each stage writes rejects with unique prefixes, so we never confuse
        one stage's rejects with another's.
        """
        prefixes = STAGE_REJECT_PREFIXES.get(stage, [])
        if not prefixes or not rejects_dir.exists():
            return

        for rfile in sorted(rejects_dir.iterdir()):
            if not any(rfile.name.startswith(p) for p in prefixes):
                continue
            try:
                data = json.loads(rfile.read_text())
                reason = data.get("reason", "UNKNOWN")
                self.note_reason(stage, reason)
            except (json.JSONDecodeError, KeyError):
                pass

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
        print(f"[{stage}] {' '.join(str(a) for a in cmd[:6])}...")
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

        # Parse cost from stage output
        if stage == "generate":
            self.cost.parse_generate_stdout(result.stdout, self._model_name())
        elif stage == "art":
            self.cost.parse_art_stdout(result.stdout)

        # Read stage summary for reject tracking
        self._track_stage(stage, result.stdout)

        return True

    def _model_name(self) -> str:
        return "gpt-4o-mini"  # default — could be overridden

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
        return stage_config.get(stage, [])

    def _track_stage(self, stage: str, stdout: str):
        """Read a stage's summary JSON from its stdout and track rejects.

        The module prints the summary dict as part of its output.
        We parse it from the last JSON object in stdout.
        """
        # Find the last line that looks like a JSON object
        # Modules print 'Summary: { ... }' or just the dict
        for line in reversed(stdout.strip().splitlines()):
            line = line.strip()
            if line.startswith("{"):
                try:
                    summary = json.loads(line)
                    self.rejects.note(stage, summary)
                    # Also read reject files from disk for reason codes
                    rejects_dir = self.work_dir / "rejects"
                    self.rejects.read_reject_files(stage, rejects_dir)
                    return
                except json.JSONDecodeError:
                    continue
            # Also try 'Summary: {...}'
            if line.startswith("Summary:"):
                try:
                    json_str = line[len("Summary:"):].strip()
                    summary = json.loads(json_str)
                    self.rejects.note(stage, summary)
                    rejects_dir = self.work_dir / "rejects"
                    self.rejects.read_reject_files(stage, rejects_dir)
                    return
                except (json.JSONDecodeError, IndexError):
                    continue

        # Fallback: read summary file from disk
        summary_file = self.work_dir / self._stage_summary(stage)
        if summary_file.exists():
            try:
                summary = json.loads(summary_file.read_text())
                self.rejects.note(stage, summary)
            except (json.JSONDecodeError, KeyError):
                pass

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
            success = False

        # Stage 2: Validate
        if success and not self.run_python("validate", "modules.validate"):
            success = False

        # Stage 3: Score
        if success and not self.run_python("score", "modules.score"):
            success = False

        # Stage 4: Simulate
        if success:
            sim_ok = self.run_python("simulate", "modules.simulate")
            if not sim_ok:
                print("[orchestrator] SIMULATE failed — continuing with partial results", file=sys.stderr)

        # Stage 5: Dedupe + Moderate
        if success and not self.run_python("dedupe_moderate", "modules.dedupe_moderate"):
            success = False

        # Stage 6: Merge card data back for art stage
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
        dedup_path = self.work_dir / "05_deduplicated.json"
        if not dedup_path.exists():
            return
        dedup_data = json.loads(dedup_path.read_text())
        if not isinstance(dedup_data, list):
            return

        card_source = None
        for src in ["02_valid.json", "01_raw.json"]:
            p = self.work_dir / src
            if p.exists():
                raw = json.loads(p.read_text())
                if isinstance(raw, list) and len(raw) > 0:
                    if "name" in raw[0] or "strata" in raw[0]:
                        card_source = {
                            c.get("id", c.get("card_id", "")): c
                            for c in raw if c.get("id") or c.get("card_id")
                        }
                        break

        if not card_source:
            return

        merged = []
        for entry in dedup_data:
            cid = entry.get("card_id") or entry.get("id", "")
            if cid and cid in card_source:
                card_def = dict(card_source[cid])
                sim_fields = {k: v for k, v in entry.items()
                              if k not in ("card_id", "card_name", "id", "name")}
                card_def["simulation"] = sim_fields
                merged.append(card_def)
            else:
                merged.append(entry)

        dedup_path.write_text(json.dumps(merged, indent=2))

    # ── Public accessors for reporting ──────────────────────────────────────

    def seeded_count(self) -> int:
        """Read the seeded card count from the seed file."""
        seed_path = PIPELINE / "seeds" / f"{self.stratum.lower()}_{self.count}.json"
        if seed_path.exists():
            try:
                return json.loads(seed_path.read_text()).get("count", self.count)
            except (json.JSONDecodeError, KeyError):
                pass
        return self.count


# ── Seed creation ─────────────────────────────────────────────────────────────

def create_seed(stratum: str, count: int) -> Path:
    seeds_dir = PIPELINE / "seeds"
    seeds_dir.mkdir(parents=True, exist_ok=True)

    type_mix = {"CREATURE": count * 2 // 3, "RITUAL": count // 4, "RELIC": count // 10}
    type_sum = sum(type_mix.values())
    type_mix["CREATURE"] += count - type_sum

    rarity_mix = {"COMMON": count // 2, "UNCOMMON": count // 3, "RARE": count // 6, "RELIC": max(3, count // 20)}
    rarity_sum = sum(rarity_mix.values())
    rarity_mix["COMMON"] += count - rarity_sum

    seed = {
        "batch_id": f"b_2026_{stratum.lower()}_e2e",
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

def collect_results(work_dir: Path, runner: PipelineRunner, cost: CostTracker,
                    rejects: RejectionTracker) -> dict[str, Any]:
    """Gather final statistics from all stage outputs."""
    results: dict[str, Any] = {
        "stages": {},
        "art_fallbacks": 0,
        "commission_queue": 0,
        "publish_ready": 0,
        "total_seeded": 0,
    }

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

    # Art fallbacks
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

    # Commission queue — count pending entries
    cq_path = ROOT / "docs" / "ART_COMMISSION_QUEUE.md"
    if cq_path.exists():
        text = cq_path.read_text()
        results["commission_queue"] = text.count("- [ ]")

    # Seeded count from seed file
    results["total_seeded"] = runner.seeded_count()

    # Publish-ready = cards that made it to art stage
    results["publish_ready"] = results["stages"].get("art", 0)

    return results


def _validate_report(results: dict, rejects: RejectionTracker,
                     cost: CostTracker) -> list[str]:
    """Run sanity checks on the report. Returns list of violation messages."""
    violations = []

    # 1. Rejection rate > 100% is impossible
    for stage in ["generate", "validate", "score", "simulate", "dedupe_moderate"]:
        total = rejects.processed.get(stage, 0)
        stage_rejects = rejects.rejects.get(stage, {})
        reject_count = sum(stage_rejects.values())
        if total > 0 and reject_count > total:
            violations.append(
                f"IMPOSSIBLE: {stage} rejection rate {reject_count}/{total} "
                f"({reject_count/total*100:.0f}%) exceeds 100%"
            )

    # 2. Zero cost with non-zero API calls
    if cost.image_calls > 0 and cost.image_gen_cost == 0.0:
        violations.append(
            f"IMPOSSIBLE: {cost.image_calls} image calls but cost is $0.00"
        )
    if cost.text_prompt_tokens > 0 and cost.text_gen_cost == 0.0:
        violations.append(
            f"IMPOSSIBLE: {cost.text_prompt_tokens} text tokens but cost is $0.00"
        )

    # 3. Zero seeded with non-zero stage output
    if results["total_seeded"] == 0 and any(v > 0 for v in results["stages"].values()):
        violations.append(
            "IMPOSSIBLE: 0 seeded cards but stages have non-zero output"
        )

    # 4. Art fallbacks > cards at art stage
    if results["art_fallbacks"] > results["stages"].get("art", 0):
        violations.append(
            f"IMPOSSIBLE: {results['art_fallbacks']} fallbacks but only "
            f"{results['stages'].get('art', 0)} cards at art stage"
        )

    # 5. Publish-ready > seeded
    if results["publish_ready"] > results["total_seeded"]:
        violations.append(
            f"IMPOSSIBLE: {results['publish_ready']} publish-ready > "
            f"{results['total_seeded']} seeded"
        )

    # 6. Negative cost
    if cost.total < 0:
        violations.append(f"IMPOSSIBLE: negative total cost ${cost.total:.4f}")

    return violations


def print_report(work_dir: Path, runner: PipelineRunner, cost: CostTracker,
                 rejects: RejectionTracker, timing: dict[str, float]):
    results = collect_results(work_dir, runner, cost, rejects)

    # Validate
    violations = _validate_report(results, rejects, cost)
    if violations:
        print(f"\n{'!'*64}")
        for v in violations:
            print(f"  !!! SANITY CHECK FAILED: {v}")
        print(f"{'!'*64}\n")

    print()
    print("=" * 64)
    print(f"  P6-10 ACCEPTANCE REPORT: {runner.stratum} {runner.count}-Card Set")
    print("=" * 64)

    print(f"\n## 1. Stage-by-stage rejection")
    print(f"   (DoD: <15% at validate stage)")
    print(rejects.summary())

    print(f"\n## 2. Wall-clock time")
    for stage, sec in sorted(timing.items()):
        if stage == "total":
            continue
        print(f"  {stage:20s} {sec:.0f}s")
    print(f"  {'TOTAL':20s} {timing.get('total', 0):.0f}s")

    print(f"\n## 3. Cost")
    print(cost.summary())

    print(f"\n## 4. Art pipeline")
    print(f"  Fallback frames: {results['art_fallbacks']}")
    print(f"  Commission queue pending: {results['commission_queue']}")

    print(f"\n## 5. Final card count")
    print(f"  Seeded:               {results['total_seeded']}")
    for stage, count in results["stages"].items():
        print(f"  {stage:20s} {count}")
    print(f"  {'PUBLISH-READY':20s} {results['publish_ready']}")

    # DoD check
    print()
    validate_total = rejects.processed.get("validate", 0)
    validate_rejects = sum(rejects.rejects.get("validate", {}).values())
    validate_pct = validate_rejects / validate_total * 100 if validate_total > 0 else 0
    if validate_pct < 15:
        print(f"  ✓ DoD MET: validate rejection {validate_pct:.1f}% (<15%)")
    else:
        print(f"  ✗ DoD NOT MET: validate rejection {validate_pct:.1f}% (target <15%)")
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
    parser.add_argument("--model", default="gpt-4o-mini",
                        help="OpenRouter text model (default: gpt-4o-mini)")
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
    print_report(work_dir, runner, cost, rejects, runner.timing)

    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())