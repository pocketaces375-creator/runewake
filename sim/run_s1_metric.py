#!/usr/bin/env python3
"""TASK-S1: Run the logged sim suite across all 7 classes (each vs each, fixed seeds).

Reports per-class winrate matrix and % of non-empty combat turns where the
chosen attack set != "all legal attackers attack" (target >= 25%).

Usage:
    python sim/run_s1_metric.py [--games N] [--seed N] [--deck-file PATH]

Results written to sim/artifact_metrics.md.
"""

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent  # sim/
ROOT = HERE.parent  # runewake/
SIM_BIN = ROOT / "sim" / "bin" / "Debug" / "net8.0" / "Runewake.Sim"
ARTIFACTS_PATH = ROOT / "content" / "artifacts" / "launch_artifacts.json"

CLASSES = [
    "warrior",
    "mage",
    "thief",
    "cleric",
    "ranger",
    "necromancer",
    "runesmith",
]

DEFAULT_GAMES = 200
DEFAULT_SEED = 42


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


def write_deck_pack(registry: dict, output_path: Path):
    """Write a standard deck pack file using a balanced card pool.

    Uses the midrange archetype from baselines as a well-balanced deck.
    """
    # Load the baselines for the midrange deck
    baselines_path = ROOT / "pipeline" / "baselines" / "global_archetypes.json"
    with open(baselines_path) as f:
        baselines = json.load(f)

    midrange_ids = baselines["midrange"]["cards"]
    cards = []
    for cid in midrange_ids:
        if cid in registry:
            cards.append(registry[cid])
        else:
            print(f"[s1_metric] WARNING: card '{cid}' not found, skipping", file=sys.stderr)

    with open(output_path, "w") as f:
        json.dump(cards, f)

    return output_path


def run_matchup(deck_path: Path, class_a: str, class_b: str,
                games: int, seed: int) -> dict | None:
    """Run a single class-vs-class matchup and return parsed report."""
    try:
        result = subprocess.run(
            [str(SIM_BIN), "run",
             "--deck-a", str(deck_path),
             "--deck-b", str(deck_path),
             "--games", str(games),
             "--seed", str(seed),
             "--artifacts-path", str(ARTIFACTS_PATH),
             "--class-a", class_a,
             "--class-b", class_b],
            capture_output=True,
            text=True,
            timeout=600,
            cwd=str(ROOT),
        )
    except FileNotFoundError:
        print(f"[s1_metric] ERROR: CLI not found at {SIM_BIN}", file=sys.stderr)
        return None
    except subprocess.TimeoutExpired:
        print(f"[s1_metric] ERROR: CLI timed out", file=sys.stderr)
        return None

    if result.returncode != 0:
        print(f"[s1_metric] ERROR: CLI returned {result.returncode}", file=sys.stderr)
        print(f"  stderr: {result.stderr[:500]}", file=sys.stderr)
        return None

    # Parse the JSON report from stdout
    for line in result.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            return json.loads(line)

    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        print(f"[s1_metric] ERROR: could not parse CLI output", file=sys.stderr)
        print(f"  stdout: {result.stdout[:200]}", file=sys.stderr)
        return None


def write_report(results: dict[str, dict[str, dict]], output_path: Path):
    """Write the artifact metrics markdown report."""
    lines = []
    lines.append("# Artifact Metrics — TASK-S1")
    lines.append("")
    lines.append(f"Generated: {time.strftime('%Y-%m-%d %H:%M:%S UTC')}")
    lines.append("")
    lines.append("## Winrate Matrix (Row = P0 class, Column = P1 class)")
    lines.append("")
    lines.append("| P0 \\ P1 | " + " | ".join(c.capitalize() for c in CLASSES) + " |")
    lines.append("|" + "---|" * (len(CLASSES) + 1))

    for row_class in CLASSES:
        row = f"| **{row_class.capitalize()}** "
        for col_class in CLASSES:
            d = results.get(row_class, {}).get(col_class, {})
            wr = d.get("win_rate_p0", 0.5)
            row += f"| {wr:.1%} "
        row += "|"
        lines.append(row)

    lines.append("")
    lines.append("## Deviation Rate (per class, aggregated across all opponents)")
    lines.append("")
    lines.append("For each class as P0: % of non-empty combat turns where the bot chose")
    lines.append("NOT to attack with all eligible attackers (target ≥ 25%).")
    lines.append("")
    lines.append("| Class | Total Combat Turns | Deviation Turns | Deviation Rate |")
    lines.append("|-------|-------------------|----------------|----------------|")

    total_combat = 0
    total_deviation = 0
    class_deviation_details = {}

    for cls in CLASSES:
        cls_combat = 0
        cls_dev = 0
        for opp in CLASSES:
            d = results.get(cls, {}).get(opp, {})
            cls_combat += d.get("total_combat_turns", 0)
            cls_dev += d.get("total_deviation_turns", 0)
        total_combat += cls_combat
        total_deviation += cls_dev
        rate = cls_dev / cls_combat if cls_combat > 0 else 0
        class_deviation_details[cls] = {
            "combat": cls_combat,
            "deviation": cls_dev,
            "rate": rate,
        }
        marker = " ✓" if rate >= 0.25 else ""
        lines.append(f"| {cls.capitalize()} | {cls_combat} | {cls_dev} | {rate:.1%}{marker} |")

    # Also show opponent-facing deviation (P1's attack decisions)
    lines.append("")
    lines.append("### As P1 (opponent bot decisions)")
    lines.append("")
    lines.append("| Class | Total Combat Turns | Deviation Turns | Deviation Rate |")
    lines.append("|-------|-------------------|----------------|----------------|")

    for cls in CLASSES:
        cls_combat = 0
        cls_dev = 0
        for opp in CLASSES:
            d = results.get(opp, {}).get(cls, {})
            cls_combat += d.get("total_combat_turns", 0)
            cls_dev += d.get("total_deviation_turns", 0)
        rate = cls_dev / cls_combat if cls_combat > 0 else 0
        marker = " ✓" if rate >= 0.25 else ""
        lines.append(f"| {cls.capitalize()} | {cls_combat} | {cls_dev} | {rate:.1%}{marker} |")

    # Overall
    overall_rate = total_deviation / total_combat if total_combat > 0 else 0
    lines.append("")
    lines.append(f"**Overall**: {total_deviation} deviation turns out of {total_combat} combat turns ({overall_rate:.1%})")
    lines.append("Target: ≥ 25% " + ("✓ MET" if overall_rate >= 0.25 else "✗ NOT MET"))

    # Per-class winrates (overall)
    lines.append("")
    lines.append("## Per-Class Winrates (aggregated across all opponents)")
    lines.append("")
    lines.append("| Class | Wins | Games | Win Rate |")
    lines.append("|-------|------|-------|----------|")

    for cls in CLASSES:
        total_wins = 0
        total_games = 0
        for opp in CLASSES:
            d = results.get(cls, {}).get(opp, {})
            total_wins += d.get("p0_wins", 0)
            total_games += d.get("total_games", 0)
        wr = total_wins / total_games if total_games > 0 else 0
        lines.append(f"| {cls.capitalize()} | {total_wins} | {total_games} | {wr:.1%} |")

    content = "\n".join(lines) + "\n"
    with open(output_path, "w") as f:
        f.write(content)
    print(f"\nReport written to {output_path}")


def main():
    parser = argparse.ArgumentParser(description="TASK-S1 artifact metrics")
    parser.add_argument("--games", type=int, default=DEFAULT_GAMES,
                        help=f"Games per matchup (default: {DEFAULT_GAMES})")
    parser.add_argument("--seed", type=int, default=DEFAULT_SEED,
                        help=f"Base seed (default: {DEFAULT_SEED})")
    parser.add_argument("--deck-file", type=str, default=None,
                        help="Path to a custom deck pack file (default: auto-generate)")
    args = parser.parse_args()

    if not SIM_BIN.exists():
        print(f"[s1_metric] ERROR: Sim binary not found at {SIM_BIN}", file=sys.stderr)
        sys.exit(1)

    if not ARTIFACTS_PATH.exists():
        print(f"[s1_metric] ERROR: Artifacts file not found at {ARTIFACTS_PATH}", file=sys.stderr)
        sys.exit(1)

    # Create or use deck pack
    if args.deck_file:
        deck_path = Path(args.deck_file)
        if not deck_path.exists():
            print(f"[s1_metric] ERROR: Deck file not found: {deck_path}", file=sys.stderr)
            sys.exit(1)
    else:
        registry = load_card_registry()
        deck_path = HERE / ".deck_pack.s1.json"
        write_deck_pack(registry, deck_path)
        print(f"[s1_metric] Created deck pack: {deck_path} ({len(registry)} cards in registry)")

    # Run the 7×7 matrix
    # Each pair is run once (row class as P0, col class as P1)
    results: dict[str, dict[str, dict]] = {c: {} for c in CLASSES}

    total_matchups = len(CLASSES) * len(CLASSES)
    completed = 0

    for row_class in CLASSES:
        for col_class in CLASSES:
            completed += 1
            print(f"[s1_metric] [{completed}/{total_matchups}] {row_class} vs {col_class}...", end=" ")
            sys.stdout.flush()

            report = run_matchup(deck_path, row_class, col_class, args.games, args.seed)
            if report is not None:
                results[row_class][col_class] = {
                    "win_rate_p0": report.get("win_rate_p0", 0.5),
                    "p0_wins": report.get("p0_wins", 0),
                    "p1_wins": report.get("p1_wins", 0),
                    "total_games": report.get("total_games", 0),
                    "avg_turns": report.get("avg_turns", 0),
                    "total_combat_turns": report.get("total_combat_turns", 0),
                    "total_deviation_turns": report.get("total_deviation_turns", 0),
                    "attack_deviation_rate": report.get("attack_deviation_rate", 0),
                }
                wr = results[row_class][col_class]["win_rate_p0"]
                print(f"P0 winrate: {wr:.1%}, dev: {report.get('attack_deviation_rate', 0):.1%}")
            else:
                results[row_class][col_class] = {}
                print("FAILED")

    # Write report
    output_path = HERE / "artifact_metrics.md"
    write_report(results, output_path)

    # Print summary to stdout
    print("\n" + "=" * 60)
    print("TASK-S1 SUMMARY")
    print("=" * 60)
    print()

    # Winrate matrix
    print("Winrate Matrix:")
    header = "          " + " ".join(f"{c:>10}" for c in CLASSES)
    print(header)
    for row_class in CLASSES:
        row = f"{row_class:>10}"
        for col_class in CLASSES:
            d = results[row_class].get(col_class, {})
            wr = d.get("win_rate_p0", 0.5)
            row += f" {wr:>9.1%}"
        print(row)

    print()
    print("Deviation rates:")
    total_ct = 0
    total_dt = 0
    for cls in CLASSES:
        ct = sum(results[cls].get(opp, {}).get("total_combat_turns", 0) for opp in CLASSES)
        dt = sum(results[cls].get(opp, {}).get("total_deviation_turns", 0) for opp in CLASSES)
        total_ct += ct
        total_dt += dt
        rate = dt / ct if ct > 0 else 0
        mark = " ✓" if rate >= 0.25 else ""
        print(f"  {cls:>10}: {rate:.1%} ({dt}/{ct}){mark}")

    print(f"\n  {'Overall':>10}: {total_dt/total_ct:.1%} ({total_dt}/{total_ct})")


if __name__ == "__main__":
    main()