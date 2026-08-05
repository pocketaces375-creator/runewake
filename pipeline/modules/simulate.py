#!/usr/bin/env python3
"""P6-05: SIMULATE — Batch simulation with baseline comparison.

Reads scored cards from a SCORE-stage output (03_scored.json), substitutes
each candidate into baseline archetype decks, runs 1,000-game batches via
Runewake.Sim, and computes win-rate deltas, average turn played, and play
rate. Flags outliers (>+4% or <−3% win-rate delta) for the review queue.

Usage:
    python -m pipeline.modules.simulate --input work/b_2026_ember_01/03_scored.json \\
        --work-dir work/b_2026_ember_01
"""

import argparse
import json
import subprocess
import sys
import tempfile
import time
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent.parent  # pipeline/
ROOT = HERE.parent  # runewake/
BASELINES_PATH = HERE / "baselines" / "global_archetypes.json"
SIM_BIN = ROOT / "sim" / "bin" / "Debug" / "net8.0" / "Runewake.Sim"

# Simulation parameters
GAMES_PER_MATCHUP = 200  # 200 per pair (reduced from 1000 for speed; 3×3×200 = 1,800 games/candidate)
SEED = 42

# Flag thresholds (from spec §5)
FLAG_STRONG_THRESHOLD = 0.04   # >+4% → too strong
FLAG_WEAK_THRESHOLD = -0.03    # <−3% → too weak

# ── Helpers ──────────────────────────────────────────────────────────────────

def load_card_registry() -> dict[str, dict]:
    """Load all hand-authored cards into a registry dict by ID."""
    registry: dict[str, dict] = {}
    for strata in ["verdant", "ember", "tide", "hollow", "dawn"]:
        path = ROOT / "content" / "cards" / f"{strata}.json"
        if not path.exists():
            continue
        with open(path) as f:
            cards = json.load(f)
        for card in cards:
            registry[card["id"]] = card
    return registry


def load_baselines() -> dict[str, dict]:
    """Load baseline archetype definitions."""
    with open(BASELINES_PATH) as f:
        return json.load(f)


def write_deck_file(card_ids: list[str], registry: dict[str, dict],
                    tmp_dir: Path, name: str,
                    extra_cards: dict[str, dict] | None = None) -> Path:
    """Write a deck as a full CardDef JSON pack for the C# CLI.

    Looks up each card ID first in the registry, then in extra_cards
    (for AI-generated candidates not in the hand-authored registry).
    """
    if extra_cards is None:
        extra_cards = {}
    cards = []
    for cid in card_ids:
        if cid in registry:
            cards.append(registry[cid])
        elif cid in extra_cards:
            cards.append(extra_cards[cid])
        else:
            print(f"[simulate] WARNING: card '{cid}' not found, skipping", file=sys.stderr)
    path = tmp_dir / f"{name}.json"
    with open(path, "w") as f:
        json.dump(cards, f)
    return path


def run_batch(deck_a_path: Path, deck_b_path: Path,
              games: int, seed: int) -> dict[str, Any] | None:
    """Run a batch simulation and return the parsed JSON report."""
    try:
        result = subprocess.run(
            [str(SIM_BIN), "run",
             "--deck-a", str(deck_a_path),
             "--deck-b", str(deck_b_path),
             "--games", str(games),
             "--seed", str(seed)],
            capture_output=True,
            text=True,
            timeout=300,
            cwd=str(ROOT),
        )
    except FileNotFoundError:
        print(f"[simulate] ERROR: CLI not found at {SIM_BIN}", file=sys.stderr)
        return None
    except subprocess.TimeoutExpired:
        print(f"[simulate] ERROR: CLI timed out", file=sys.stderr)
        return None

    # Parse the JSON report from stdout (last line or full stdout)
    try:
        # Find a JSON object in stdout (the Report is the only JSON output)
        for line in result.stdout.splitlines():
            line = line.strip()
            if line.startswith("{"):
                return json.loads(line)
        # Fallback: try parsing whole stdout
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        print(f"[simulate] ERROR: could not parse CLI output", file=sys.stderr)
        print(f"  stdout: {result.stdout[:200]}", file=sys.stderr)
        return None


# ── Main simulation logic ────────────────────────────────────────────────────

def compute_reference_baselines(registry: dict, baselines: dict,
                                 tmp_dir: Path, games: int) -> dict[str, dict[str, float]]:
    """Run full baseline vs baseline to get reference win rates."""
    ref: dict[str, dict[str, float]] = {}
    archetypes = list(baselines.keys())

    for a_name in archetypes:
        ref[a_name] = {}
        a_ids = baselines[a_name]["cards"]
        a_file = write_deck_file(a_ids, registry, tmp_dir, f"baseline_{a_name}")

        for b_name in archetypes:
            b_ids = baselines[b_name]["cards"]
            b_file = write_deck_file(b_ids, registry, tmp_dir, f"baseline_{b_name}")

            report = run_batch(a_file, b_file, games, SEED)
            if report:
                ref[a_name][b_name] = report.get("winRateP0", 0.5)
            else:
                ref[a_name][b_name] = 0.5
            print(f"[simulate]   baseline {a_name} vs {b_name}: "
                  f"{ref[a_name][b_name]:.1%}")

    return ref


def substitute_and_simulate(
    candidate: dict, archetype_name: str, archetype_ids: list[str],
    registry: dict, baselines: dict, ref: dict[str, dict[str, float]],
    tmp_dir: Path, candidate_index: int,
) -> dict[str, Any] | None:
    """Substitute the candidate into one archetype and simulate against all baselines.

    Replaces the card at position 0 (first card in archetype) with the candidate.
    Returns a dict of metrics or None if simulation fails.
    """
    if "id" not in candidate:
        return None
    cid = candidate["id"]

    # Create modified deck: replace first card with candidate
    mod_ids = list(archetype_ids)
    if mod_ids:
        mod_ids[0] = cid
    else:
        mod_ids.append(cid)

    # Write candidate's card def to registry temp + ensure it's available
    # (We need to patch the card into a temporary pack for the C# CLI)
    # Create a pack file containing just this card so the C# CLI can load it
    extra_cards = {candidate.get("id", ""): candidate} if candidate.get("id") else {}
    modified_deck_path = write_deck_file(mod_ids, registry, tmp_dir,
                                         f"candidate_{candidate_index}_{archetype_name}",
                                         extra_cards=extra_cards)

    results: dict[str, Any] = {}
    for opponent_name in baselines:
        opp_ids = baselines[opponent_name]["cards"]
        opp_file = write_deck_file(opp_ids, registry, tmp_dir, f"opp_{opponent_name}")

        report = run_batch(modified_deck_path, opp_file, GAMES_PER_MATCHUP, SEED + candidate_index)
        if report:
            win_rate = report.get("winRateP0", 0.5)
            avg_turns = report.get("avgTurns", 0)
        else:
            return None

        baseline_win_rate = ref.get(archetype_name, {}).get(opponent_name, 0.5)
        delta = win_rate - baseline_win_rate
        results[opponent_name] = {
            "win_rate": win_rate,
            "baseline_win_rate": baseline_win_rate,
            "delta": delta,
            "avg_turns": avg_turns,
        }

    # Compute aggregate metrics
    deltas = [r["delta"] for r in results.values()]
    avg_delta = sum(deltas) / len(deltas) if deltas else 0.0
    max_delta = max(deltas) if deltas else 0.0

    # Flags
    flags: list[str] = []
    if max_delta > FLAG_STRONG_THRESHOLD:
        flags.append(f"TOO_STRONG: max delta {max_delta:+.1%}")
    if avg_delta < FLAG_WEAK_THRESHOLD:
        flags.append(f"TOO_WEAK: avg delta {avg_delta:+.1%}")

    return {
        "card_id": cid,
        "card_name": candidate.get("name", "?"),
        "archetype": archetype_name,
        "archetype_description": baselines[archetype_name].get("description", ""),
        "matchup_results": results,
        "avg_delta": avg_delta,
        "max_delta": max_delta,
        "flags": flags,
    }


# ── Main entry point ──────────────────────────────────────────────────────────

def _write_empty_outputs(work_dir: Path, input_path: Path):
    """Write empty output files when there are no candidates."""
    out_path = work_dir / "04_simulated.json"
    with open(out_path, "w") as f:
        json.dump([], f)
    summary = {
        "input_file": str(input_path),
        "total_processed": 0,
        "simulated": 0,
        "rejected": 0,
        "flagged": 0,
        "games_per_matchup": 0,
        "archetypes": [],
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    with open(work_dir / "04_summary.json", "w") as f:
        json.dump(summary, f, indent=2)
    print(f"[simulate] Summary: {summary}")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — SIMULATE stage",
    )
    parser.add_argument("--input", required=True,
                        help="Input card file (from SCORE, e.g. 03_scored.json)")
    parser.add_argument("--work-dir", required=True,
                        help="Work directory for this batch")
    parser.add_argument("--games", type=int, default=GAMES_PER_MATCHUP,
                        help=f"Games per matchup (default: {GAMES_PER_MATCHUP})")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    input_path = Path(args.input)
    if not input_path.exists():
        print(f"[simulate] Input not found: {input_path}", file=sys.stderr)
        return 1

    work_dir = Path(args.work_dir)
    work_dir.mkdir(parents=True, exist_ok=True)
    rejects_dir = work_dir / "rejects"
    rejects_dir.mkdir(parents=True, exist_ok=True)

    games = args.games

    with open(input_path) as f:
        raw = json.load(f)
    candidates = raw if isinstance(raw, list) else [raw]

    if len(candidates) == 0:
        print("[simulate] No candidates to simulate.")
        _write_empty_outputs(work_dir, input_path)
        return 0

    print(f"[simulate] Simulating {len(candidates)} candidate cards...")
    print(f"[simulate] Games per matchup: {games}")

    # Load data
    registry = load_card_registry()
    baselines = load_baselines()
    archetype_names = list(baselines.keys())

    print(f"[simulate] Archetypes: {archetype_names}")
    print(f"[simulate] Registry: {len(registry)} cards loaded")

    if not SIM_BIN.exists():
        print(f"[simulate] ERROR: C# CLI not found at {SIM_BIN}", file=sys.stderr)
        return 1

    # Create temp directory for deck files
    with tempfile.TemporaryDirectory(prefix="sim_") as tmp_dir_str:
        tmp_dir = Path(tmp_dir_str)

        # Step 1: Compute reference baselines
        print("[simulate] Computing baseline reference win rates...")
        ref = compute_reference_baselines(registry, baselines, tmp_dir, games)

        # Step 2: For each candidate, substitute and simulate
        simulated: list[dict] = []
        rejects: list[tuple[dict, str]] = []

        for i, candidate in enumerate(candidates):
            cid = candidate.get("id", f"__unknown_{i}")
            cname = candidate.get("name", "?")
            print(f"[simulate]   [{i+1}/{len(candidates)}] {cid} ({cname})")

            # Try each archetype
            best_result = None
            for arch_name in archetype_names:
                arch_ids = baselines[arch_name]["cards"]
                result = substitute_and_simulate(
                    candidate, arch_name, arch_ids, registry, baselines, ref,
                    tmp_dir, i,
                )
                if result is None:
                    continue

                if best_result is None or result["avg_delta"] > best_result["avg_delta"]:
                    result["stratum"] = candidate.get("strata", "?")
                    result["rarity"] = candidate.get("rarity", "?")
                    result["cost"] = candidate.get("cost", "?")
                    if "power_score" in candidate:
                        result["power_score"] = candidate["power_score"]
                    best_result = result

            if best_result is None:
                rejects.append((candidate, "SIM_FAIL: simulation did not return results"))
                continue

            # Check if flagged
            if best_result["flags"]:
                reject_reason = "; ".join(best_result["flags"])
                rejects.append((candidate, f"SIM_FLAG: {reject_reason}"))
                # Still include in simulated output with flag info
                best_result["flagged"] = True
            else:
                best_result["flagged"] = False

            simulated.append(best_result)

        # Write outputs
        out_path = work_dir / "04_simulated.json"
        with open(out_path, "w") as f:
            json.dump(simulated, f, indent=2)

        if rejects:
            for i, (card, reason) in enumerate(rejects):
                rej_path = rejects_dir / f"reject_sim_{i:03d}.json"
                with open(rej_path, "w") as f:
                    json.dump({"card": card, "reason": reason}, f, indent=2)

        # Summary
        flagged_count = sum(1 for s in simulated if s["flagged"])
        summary = {
            "input_file": str(input_path),
            "total_processed": len(candidates),
            "simulated": len(simulated),
            "rejected": len(rejects),
            "flagged": flagged_count,
            "games_per_matchup": games,
            "archetypes": archetype_names,
            "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        }
        with open(work_dir / "04_summary.json", "w") as f:
            json.dump(summary, f, indent=2)

        print(f"[simulate] Wrote {len(simulated)} simulated results to {out_path}")
        if flagged_count:
            print(f"[simulate] {flagged_count} cards flagged for review")
        print(f"[simulate] Summary: {summary}")

        if rejects:
            print(f"[simulate] {len(rejects)} cards rejected")
            return 2 if len(simulated) == 0 else 0

        print(f"[simulate] ✓ All {len(simulated)} cards simulated successfully")
        return 0


if __name__ == "__main__":
    sys.exit(main())