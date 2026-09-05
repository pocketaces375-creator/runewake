#!/usr/bin/env python3
"""TASK-FUN-SIM-1: Simulate 4 variants across 7 classes, 500 mirrors each.

Variants:
  (a) StartingVigor 20 instead of 25
  (b) INVOKE — artifact charges held until tapped (tactician evaluates tap)
  (c) ALTAR — lane 2 War Altar, edge lanes 0/4 hedge (no Pierce carry-through)
  (d) a + b + c combined

Reports per variant: P0 win%, avg game length in turns, avg turn of first
creature death, win% gap fastest vs slowest class.

Usage:
    python sim/fun_sim.py [--games N] [--seed N]

Results written to sim/fun_sim_report.json and printed as a table to stdout.
"""

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

# Config
GAMES_PER_CLASS = 500
BASE_SEED = 42

# Sim class names (as used by the sim CLI)
SIM_CLASSES = [
    "warrior",
    "mage",
    "thief",
    "cleric",
    "ranger",
    "necromancer",
    "runesmith",
]

VARIANTS = [
    {
        "id": "a",
        "name": "StartingVigor 20",
        "args": ["--starting-vigor-20"],
    },
    {
        "id": "b",
        "name": "INVOKE",
        "args": ["--invoke-mode"],
    },
    {
        "id": "c",
        "name": "ALTAR",
        "args": ["--altar-mode"],
    },
    {
        "id": "d",
        "name": "Combined (a+b+c)",
        "args": ["--starting-vigor-20", "--invoke-mode", "--altar-mode"],
    },
]

HERE = Path(__file__).resolve().parent  # sim/
ROOT = HERE.parent  # runewake-lane3/
SIM_BIN = ROOT / "sim" / "bin" / "Debug" / "net8.0" / "Runewake.Sim"
ARTIFACTS_PATH = ROOT / "content" / "artifacts" / "launch_artifacts.json"


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
    """Write a standard deck pack file using the midrange archetype."""
    baselines_path = ROOT / "pipeline" / "baselines" / "global_archetypes.json"
    with open(baselines_path) as f:
        baselines = json.load(f)

    midrange_ids = baselines["midrange"]["cards"]
    cards = []
    for cid in midrange_ids:
        if cid in registry:
            cards.append(registry[cid])
        else:
            print(f"[fun_sim] WARNING: card '{cid}' not found, skipping", file=sys.stderr)

    with open(output_path, "w") as f:
        json.dump(cards, f)

    return output_path


def run_mirror(class_name: str, games: int, seed: int,
               variant_args: list[str], deck_path: Path) -> dict | None:
    """Run mirror matches for a single class with a variant."""
    try:
        result = subprocess.run(
            [str(SIM_BIN), "run",
             "--deck-a", str(deck_path),
             "--deck-b", str(deck_path),
             "--games", str(games),
             "--seed", str(seed),
             "--artifacts-path", str(ARTIFACTS_PATH),
             "--class-a", class_name,
             "--class-b", class_name,
             *variant_args],
            capture_output=True,
            text=True,
            timeout=600,
            cwd=str(ROOT),
        )
    except FileNotFoundError:
        print(f"[fun_sim] ERROR: CLI not found at {SIM_BIN}", file=sys.stderr)
        return None
    except subprocess.TimeoutExpired:
        print(f"[fun_sim] ERROR: CLI timed out", file=sys.stderr)
        return None

    if result.returncode != 0:
        print(f"[fun_sim] ERROR: CLI returned {result.returncode}", file=sys.stderr)
        print(f"  stderr: {result.stderr[:500]}", file=sys.stderr)
        return None

    for line in result.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            return json.loads(line)

    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        print(f"[fun_sim] ERROR: could not parse CLI output", file=sys.stderr)
        print(f"  stdout: {result.stdout[:200]}", file=sys.stderr)
        return None


def main():
    parser = argparse.ArgumentParser(description="TASK-FUN-SIM-1 study")
    parser.add_argument("--games", type=int, default=GAMES_PER_CLASS,
                        help=f"Games per class per variant (default: {GAMES_PER_CLASS})")
    parser.add_argument("--seed", type=int, default=BASE_SEED,
                        help=f"Base seed (default: {BASE_SEED})")
    args = parser.parse_args()

    games = args.games
    seed = args.seed

    if not SIM_BIN.exists():
        print(f"[fun_sim] ERROR: Sim binary not found at {SIM_BIN}", file=sys.stderr)
        sys.exit(1)

    if not ARTIFACTS_PATH.exists():
        print(f"[fun_sim] ERROR: Artifacts file not found at {ARTIFACTS_PATH}", file=sys.stderr)
        sys.exit(1)

    # Create deck pack
    registry = load_card_registry()
    deck_path = HERE / ".fun_sim_deck.json"
    write_deck_pack(registry, deck_path)
    print(f"[fun_sim] Created deck pack: {deck_path} ({len(registry)} cards in registry)")

    # Run all variants × classes
    # results[class][variant_id] = {stats dict}
    results: dict[str, dict[str, dict]] = {c: {} for c in SIM_CLASSES}
    total_runs = len(SIM_CLASSES) * len(VARIANTS)
    completed = 0

    print(f"[fun_sim] Running {total_runs} runs, {games} games each, seed={seed}")
    print()

    for cls in SIM_CLASSES:
        for variant in VARIANTS:
            vid = variant["id"]
            vname = variant["name"]
            vargs = variant["args"]
            completed += 1
            print(f"[fun_sim] [{completed}/{total_runs}] {cls} / {vname}...", end=" ")
            sys.stdout.flush()

            report = run_mirror(cls, games, seed, vargs, deck_path)
            if report is not None:
                entry = {
                    "win_rate_p0": report.get("win_rate_p0", 0.5),
                    "p0_wins": report.get("p0_wins", 0),
                    "p1_wins": report.get("p1_wins", 0),
                    "total_games": report.get("total_games", 0),
                    "avg_turns": report.get("avg_turns", 0),
                    "avg_turns_first_creature_death": report.get("avg_turns_first_creature_death", 0),
                    "avg_final_p0_vigor": report.get("avg_final_p0_vigor", 0),
                    "avg_final_p1_vigor": report.get("avg_final_p1_vigor", 0),
                }
                results[cls][vid] = entry
                wr = entry["win_rate_p0"]
                avg_t = entry["avg_turns"]
                avg_fd = entry["avg_turns_first_creature_death"]
                print(f"P0 wr: {wr:.1%}, avg turns: {avg_t:.1f}, first death: {avg_fd:.1f}")
            else:
                results[cls][vid] = {}
                print("FAILED")

    # Write results to JSON
    output_json = HERE / "fun_sim_report.json"
    output_data = {"variants": [], "classes": SIM_CLASSES, "results": results, "games": games, "seed": seed}
    with open(output_json, "w") as f:
        json.dump(output_data, f, indent=2)
    print(f"\n[fun_sim] Results written to {output_json}")

    # Print summary table
    print()
    print("=" * 100)
    print("TASK-FUN-SIM-1 — REPORT")
    print("=" * 100)
    print(f"Games per class per variant: {games}")
    print(f"Base seed: {seed}")
    print()

    for variant in VARIANTS:
        vid = variant["id"]
        vname = variant["name"]
        print(f"─" * 100)
        print(f"  Variant {vid}: {vname}")
        print(f"─" * 100)
        print(f"  {'Class':<20} {'P0 Win%':>10} {'Avg Turns':>10} {'First Death':>11} {'Wins':>6} {'Games':>6}")
        print(f"  {'─'*20} {'─'*10} {'─'*10} {'─'*11} {'─'*6} {'─'*6}")

        total_wins = 0
        total_games = 0
        sum_turns = 0.0
        sum_first_death = 0.0
        count_first_death = 0
        class_wrs = []

        for cls in SIM_CLASSES:
            d = results.get(cls, {}).get(vid, {})
            pw = d.get("p0_wins", 0)
            tg = d.get("total_games", 0)
            wr = pw / tg if tg > 0 else 0.0
            avg_t = d.get("avg_turns", 0)
            avg_fd = d.get("avg_turns_first_creature_death", 0)

            class_wrs.append((cls, wr))
            total_wins += pw
            total_games += tg
            sum_turns += avg_t * tg if tg > 0 else 0
            if avg_fd > 0:
                sum_first_death += avg_fd
                count_first_death += 1

            print(f"  {cls:<20} {wr:>9.1%}  {avg_t:>9.1f}  {avg_fd:>9.1f}t  {pw:>5}  {tg:>5}")

        overall_wr = total_wins / total_games if total_games > 0 else 0.0
        overall_avg_turns = sum_turns / total_games if total_games > 0 else 0.0
        overall_avg_fd = sum_first_death / count_first_death if count_first_death > 0 else 0.0

        # Sort by win rate to find fastest and slowest
        class_wrs.sort(key=lambda x: x[1])
        fastest_cls, fastest_wr = class_wrs[-1]
        slowest_cls, slowest_wr = class_wrs[0]
        gap = fastest_wr - slowest_wr

        # Also find the class with the shortest avg turns (fastest to finish)
        # and longest avg turns (slowest to finish)
        class_turns = [(cls, results.get(cls, {}).get(vid, {}).get("avg_turns", 0)) for cls in SIM_CLASSES]
        class_turns.sort(key=lambda x: x[1])
        fastest_turn_cls, fastest_turns = class_turns[0]
        slowest_turn_cls, slowest_turns = class_turns[-1]
        turn_gap = slowest_turns - fastest_turns

        print(f"  {'─'*73}")
        print(f"  {'OVERALL':<20} {overall_wr:>9.1%}  {overall_avg_turns:>9.1f}  {overall_avg_fd:>9.1f}t  {total_wins:>5}  {total_games:>5}")
        print(f"  {'Fastest (by win%)':<20} {fastest_cls:<12} {fastest_wr:.1%}")
        print(f"  {'Slowest (by win%)':<20} {slowest_cls:<12} {slowest_wr:.1%}")
        print(f"  {'Win% Gap (fast - slow)':<20} {gap:.1%}")
        print(f"  {'Fastest finish':<20} {fastest_turn_cls:<12} {fastest_turns:.1f} avg turns")
        print(f"  {'Slowest finish':<20} {slowest_turn_cls:<12} {slowest_turns:.1f} avg turns")
        print(f"  {'Turn gap (slow - fast)':<20} {turn_gap:.1f} turns")
        print()

    # Cleanup
    deck_path.unlink(missing_ok=True)
    sys.exit(0)


if __name__ == "__main__":
    main()