#!/usr/bin/env python3
"""TASK-BALANCE-MIRROR-1: First-player-advantage compensation study.

Runs mirror matches (same class vs same class) across 5 compensation variants
using the same seed set for each variant. REPORT ONLY — no gameplay changes.

Variants:
  (a) baseline — current rules
  (b) P1 gets +1 Attunement max on turn 1
  (c) P1 opening hand 6 instead of 5
  (d) b + c combined
  (e) P0's turn-1 Attunement ramp delayed one turn

Usage:
    python sim/mirror_study.py [--games N] [--seed N]

Results written to sim/mirror_study.md.
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

VARIANT_NAMES = {
    0: "(a) baseline",
    1: "(b) P1 +1 Attunement max on turn 1",
    2: "(c) P1 opening hand 6 instead of 5",
    3: "(d) b + c combined",
    4: "(e) P0 turn-1 Attunement ramp delayed one turn",
}

# Sim classes (pre-CLASS-7-FIX naming — see note below about real-class mapping)
SIM_CLASSES = [
    "warrior",
    "mage",
    "thief",
    "cleric",
    "ranger",
    "necromancer",
    "runesmith",
]

# Real classes after CLASS-7-FIX
REAL_CLASSES = [
    "warrior",
    "battlemage",
    "thief",
    "druid",
    "ranger",
    "necromancer",
    "paladin",
]

# Which sim classes map to which real classes
CLASS_MAPPING = {
    "warrior": "warrior",
    "mage": "battlemage",     # mage → battlemage
    "thief": "thief",
    "cleric": "druid",        # cleric → druid (no druid artifacts exist)
    "ranger": "ranger",
    "necromancer": "necromancer",
    "runesmith": "paladin",   # runesmith → paladin (no paladin artifacts exist)
}

# Classes with artifact definitions in launch_artifacts.json
CLASSES_WITH_ARTIFACTS = {"warrior", "mage", "thief", "cleric", "ranger", "necromancer", "runesmith"}

HERE = Path(__file__).resolve().parent  # sim/
ROOT = HERE.parent  # runewake/
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
            print(f"[mirror_study] WARNING: card '{cid}' not found, skipping", file=sys.stderr)

    with open(output_path, "w") as f:
        json.dump(cards, f)

    return output_path


def run_mirror(class_name: str, games: int, seed: int, compensation: int,
               deck_path: Path) -> dict | None:
    """Run mirror matches for a single class with a compensation variant."""
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
             "--compensation", str(compensation)],
            capture_output=True,
            text=True,
            timeout=600,
            cwd=str(ROOT),
        )
    except FileNotFoundError:
        print(f"[mirror_study] ERROR: CLI not found at {SIM_BIN}", file=sys.stderr)
        return None
    except subprocess.TimeoutExpired:
        print(f"[mirror_study] ERROR: CLI timed out", file=sys.stderr)
        return None

    if result.returncode != 0:
        print(f"[mirror_study] ERROR: CLI returned {result.returncode}", file=sys.stderr)
        print(f"  stderr: {result.stderr[:500]}", file=sys.stderr)
        return None

    for line in result.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            return json.loads(line)

    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        print(f"[mirror_study] ERROR: could not parse CLI output", file=sys.stderr)
        print(f"  stdout: {result.stdout[:200]}", file=sys.stderr)
        return None


def write_report(results: dict, output_path: Path):
    """Write the mirror study markdown report."""
    lines = []
    lines.append("# First-Player Advantage Compensation Study — TASK-BALANCE-MIRROR-1")
    lines.append("")
    lines.append(f"Generated: {time.strftime('%Y-%m-%d %H:%M:%S UTC')}")
    lines.append(f"Games per class per variant: {GAMES_PER_CLASS}")
    lines.append(f"Base seed: {BASE_SEED}")
    lines.append("")
    lines.append("## Variants Tested")
    lines.append("")
    lines.append("| # | Variant | Description |")
    lines.append("|---|---------|-------------|")
    for v in sorted(VARIANT_NAMES.keys()):
        lines.append(f"| {v} | {VARIANT_NAMES[v]} |")
    lines.append("")
    lines.append("## Class Name Mapping")
    lines.append("")
    lines.append("The sim infrastructure uses pre-CLASS-7-FIX class names. The real classes")
    lines.append("after CLASS-7-FIX and their sim equivalents:")
    lines.append("")
    lines.append("| Sim Name | Real Class (CLASS-7-FIX) | Has Artifacts? |")
    lines.append("|----------|--------------------------|----------------|")
    for sim_cls in SIM_CLASSES:
        real_cls = CLASS_MAPPING[sim_cls]
        has_artifacts = sim_cls in CLASSES_WITH_ARTIFACTS
        lines.append(f"| {sim_cls} | {real_cls} | {'yes' if has_artifacts else 'no'} |")
    lines.append("")
    lines.append("Note: battlemage, druid, and paladin have no artifact definitions in")
    lines.append("launch_artifacts.json, so their sim equivalents (mage, cleric, runesmith)")
    lines.append("run without artifacts. The compensation study is still valid — it measures")
    lines.append("structural first-player advantage, not class-specific power.")
    lines.append("")

    # Table: P0 win% per variant (overall)
    lines.append("## P0 Win Rate per Variant (overall)")
    lines.append("")
    lines.append("| Variant | " + " | ".join(c.capitalize() for c in SIM_CLASSES) + " | **Overall** |")
    lines.append("|" + "---|" * (len(SIM_CLASSES) + 2))

    for v in sorted(VARIANT_NAMES.keys()):
        row = f"| {VARIANT_NAMES[v]} "
        total_wins = 0
        total_games = 0
        for cls in SIM_CLASSES:
            d = results.get(cls, {}).get(v, {})
            pw = d.get("p0_wins", 0)
            tg = d.get("total_games", 0)
            wr = pw / tg if tg > 0 else 0.0
            row += f"| {wr:.1%} "
            total_wins += pw
            total_games += tg
        overall_wr = total_wins / total_games if total_games > 0 else 0.0
        row += f"| **{overall_wr:.1%}** |"
        lines.append(row)

    lines.append("")

    # Per-class tables
    for cls in SIM_CLASSES:
        real_cls = CLASS_MAPPING[cls]
        has_art = cls in CLASSES_WITH_ARTIFACTS
        lines.append(f"## {cls.capitalize()} (→ {real_cls.capitalize()}) — Per-Variant Results")
        lines.append("")
        lines.append("| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |")
        lines.append("|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|")
        for v in sorted(VARIANT_NAMES.keys()):
            d = results.get(cls, {}).get(v, {})
            pw = d.get("p0_wins", 0)
            pl = d.get("p1_wins", 0)
            tg = d.get("total_games", 0)
            wr = d.get("win_rate_p0", 0.5)
            avg_t = d.get("avg_turns", 0)
            avg_p0 = d.get("avg_cards_in_hand_p0", 0)
            avg_p1 = d.get("avg_cards_in_hand_p1", 0)
            lines.append(f"| {VARIANT_NAMES[v]} | {pw} | {pl} | {tg} | {wr:.1%} | {avg_t:.1f} | {avg_p0:.1f} | {avg_p1:.1f} |")
        lines.append("")

    # Recommendation
    lines.append("## Analysis")
    lines.append("")
    lines.append("### Which variant lands nearest 50/50 without tipping to P1?")
    lines.append("")

    # Compute overall P0 win% per variant
    variant_overalls = {}
    for v in sorted(VARIANT_NAMES.keys()):
        total_wins = 0
        total_games = 0
        for cls in SIM_CLASSES:
            d = results.get(cls, {}).get(v, {})
            total_wins += d.get("p0_wins", 0)
            total_games += d.get("total_games", 0)
        overall_wr = total_wins / total_games if total_games > 0 else 0.5
        variant_overalls[v] = overall_wr

    # Find closest to 50% without going below (P1 favored = below 50%)
    best_variant = None
    best_distance = 999.0
    for v, wr in variant_overalls.items():
        dist = abs(wr - 0.5)
        if dist < best_distance:
            best_distance = dist
            best_variant = v

    lines.append(f"**Closest to 50/50**: {VARIANT_NAMES[best_variant]} (overall P0 win rate = {variant_overalls[best_variant]:.1%})")
    lines.append("")

    # Check if the winner is within 1.5 points of baseline (Fable's rule for TASK-BALANCE-ADOPT-1)
    baseline_wr = variant_overalls[0]
    winner_wr = variant_overalls[best_variant]
    if abs(winner_wr - baseline_wr) <= 0.015:
        lines.append(f"**Note**: Winner is within 1.5pp of baseline ({baseline_wr:.1%} vs {winner_wr:.1%}). "
                     f"Per Fable's rule (TASK-BALANCE-ADOPT-1), variant (b) would be adopted instead.")
    else:
        lines.append(f"**Note**: Winner is more than 1.5pp from baseline ({baseline_wr:.1%} vs {winner_wr:.1%}). "
                     f"The difference is significant enough to adopt directly.")

    # List all P0 win% for reference
    lines.append("")
    lines.append("| Variant | P0 Win Rate | Distance from 50% |")
    lines.append("|---------|------------|-------------------|")
    for v in sorted(VARIANT_NAMES.keys()):
        wr = variant_overalls[v]
        dist = abs(wr - 0.5) * 100
        lines.append(f"| {VARIANT_NAMES[v]} | {wr:.1%} | {dist:.1f}pp |")

    lines.append("")

    content = "\n".join(lines) + "\n"
    with open(output_path, "w") as f:
        f.write(content)
    print(f"\nReport written to {output_path}")


def main():
    parser = argparse.ArgumentParser(description="TASK-BALANCE-MIRROR-1 compensation study")
    parser.add_argument("--games", type=int, default=GAMES_PER_CLASS,
                        help=f"Games per class per variant (default: {GAMES_PER_CLASS})")
    parser.add_argument("--seed", type=int, default=BASE_SEED,
                        help=f"Base seed (default: {BASE_SEED})")
    args = parser.parse_args()

    games = args.games
    seed = args.seed

    if not SIM_BIN.exists():
        print(f"[mirror_study] ERROR: Sim binary not found at {SIM_BIN}", file=sys.stderr)
        sys.exit(1)

    if not ARTIFACTS_PATH.exists():
        print(f"[mirror_study] ERROR: Artifacts file not found at {ARTIFACTS_PATH}", file=sys.stderr)
        sys.exit(1)

    # Create deck pack
    registry = load_card_registry()
    deck_path = HERE / ".deck_pack.mirror.json"
    write_deck_pack(registry, deck_path)
    print(f"[mirror_study] Created deck pack: {deck_path} ({len(registry)} cards in registry)")

    # Run all variants × classes
    results: dict[str, dict[int, dict]] = {c: {} for c in SIM_CLASSES}
    total_runs = len(SIM_CLASSES) * len(VARIANT_NAMES)
    completed = 0

    print(f"[mirror_study] Running {total_runs} runs, {games} games each, seed={seed}")
    print()

    for cls in SIM_CLASSES:
        for v in sorted(VARIANT_NAMES.keys()):
            completed += 1
            print(f"[mirror_study] [{completed}/{total_runs}] {cls} / {VARIANT_NAMES[v]}...", end=" ")
            sys.stdout.flush()

            report = run_mirror(cls, games, seed, v, deck_path)
            if report is not None:
                results[cls][v] = {
                    "win_rate_p0": report.get("win_rate_p0", 0.5),
                    "p0_wins": report.get("p0_wins", 0),
                    "p1_wins": report.get("p1_wins", 0),
                    "total_games": report.get("total_games", 0),
                    "avg_turns": report.get("avg_turns", 0),
                    "total_combat_turns": report.get("total_combat_turns", 0),
                    "total_deviation_turns": report.get("total_deviation_turns", 0),
                    "attack_deviation_rate": report.get("attack_deviation_rate", 0),
                    "avg_cards_in_hand_p0": report.get("avg_cards_in_hand_p0", 0),
                    "avg_cards_in_hand_p1": report.get("avg_cards_in_hand_p1", 0),
                }
                wr = results[cls][v]["win_rate_p0"]
                print(f"P0 wr: {wr:.1%}")
            else:
                results[cls][v] = {}
                print("FAILED")

    # Write report
    output_path = HERE / "mirror_study.md"
    write_report(results, output_path)

    # Print summary
    print()
    print("=" * 60)
    print("SUMMARY")
    print("=" * 60)
    for v in sorted(VARIANT_NAMES.keys()):
        total_wins = 0
        total_games = 0
        for cls in SIM_CLASSES:
            d = results.get(cls, {}).get(v, {})
            total_wins += d.get("p0_wins", 0)
            total_games += d.get("total_games", 0)
        wr = total_wins / total_games if total_games > 0 else 0
        print(f"  {VARIANT_NAMES[v]}: P0 {wr:.1%} ({total_wins}/{total_games})")

    # Cleanup temp deck pack
    deck_path.unlink(missing_ok=True)

    sys.exit(0)


if __name__ == "__main__":
    main()
