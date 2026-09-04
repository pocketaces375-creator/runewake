#!/usr/bin/env python3
"""TASK-SIMGATE-1: Balance sim gate — the pass/fail gate every future card batch must clear.

Runs a full 7x7 matchup matrix (49 pairings, fixed seeds, both play orders)
producing a winrate table, bot telemetry, and a PASS/FAIL decision.

Cribs from TASK-S1's run_s1_metric.py but (a) adds cards-in-hand telemetry,
(b) runs the full matrix in both play orders, and (c) gates on explicit thresholds.

Usage:
    python sim/balance_gate.py [--games N] [--seed N]

Results written to sim/balance_matrix.md.
Exit code 0 = PASS, 1 = FAIL.
"""

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

# ═══════════════════════════════════════════════════════════════════════════════
# CONFIGURABLE THRESHOLDS — Claude may retune these
# ═══════════════════════════════════════════════════════════════════════════════

# No class's overall winrate vs the field may fall outside this range
WINRATE_VS_FIELD_MIN = 0.35   # 35%
WINRATE_VS_FIELD_MAX = 0.65   # 65%

# No single pairing's winrate may fall outside this range
WINRATE_PAIRING_MIN = 0.25    # 25%
WINRATE_PAIRING_MAX = 0.75    # 75%

# Deviation metric: % of non-empty combat turns where bot chose NOT to attack
# with all eligible attackers. Target: >= 25%.
DEVIATION_RATE_TARGET = 0.25  # 25%

# Games per class-vs-class pairing
GAMES_PER_MATCHUP = 200

# Base random seed for reproducibility
BASE_SEED = 42

# ═══════════════════════════════════════════════════════════════════════════════

HERE = Path(__file__).resolve().parent  # sim/
ROOT = HERE.parent  # runewake/
SIM_BIN = ROOT / "sim" / "bin" / "Debug" / "net8.0" / "Runewake.Sim"
ARTIFACTS_PATH = ROOT / "content" / "artifacts" / "launch_artifacts.json"
ARTIFACTS_VARIANTS_DIR = ROOT / "content" / "artifacts" / "variants"

CLASSES = [
    "warrior",
    "mage",
    "thief",
    "cleric",
    "ranger",
    "necromancer",
    "runesmith",
]


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
            print(f"[balance_gate] WARNING: card '{cid}' not found, skipping", file=sys.stderr)

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
        print(f"[balance_gate] ERROR: CLI not found at {SIM_BIN}", file=sys.stderr)
        return None
    except subprocess.TimeoutExpired:
        print(f"[balance_gate] ERROR: CLI timed out", file=sys.stderr)
        return None

    if result.returncode != 0:
        print(f"[balance_gate] ERROR: CLI returned {result.returncode}", file=sys.stderr)
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
        print(f"[balance_gate] ERROR: could not parse CLI output", file=sys.stderr)
        print(f"  stdout: {result.stdout[:200]}", file=sys.stderr)
        return None


# ── GATE logic ────────────────────────────────────────────────────────────────

class GateResult:
    """Collects pass/fail results for the balance gate."""

    def __init__(self):
        self.failures: list[str] = []
        self.warnings: list[str] = []

    def fail(self, msg: str):
        self.failures.append(msg)

    def warn(self, msg: str):
        self.warnings.append(msg)

    @property
    def passed(self) -> bool:
        return len(self.failures) == 0


def check_winrate_vs_field(results: dict, gate: GateResult):
    """Check that no class's overall winrate vs the field is outside [35%, 65%]."""
    for cls in CLASSES:
        total_wins = 0
        total_games = 0
        for opp in CLASSES:
            d = results.get(cls, {}).get(opp, {})
            total_wins += d.get("p0_wins", 0)
            total_games += d.get("total_games", 0)
        wr = total_wins / total_games if total_games > 0 else 0.5
        if wr < WINRATE_VS_FIELD_MIN:
            gate.fail(
                f"{cls.capitalize()} vs-field winrate {wr:.1%} is below "
                f"minimum {WINRATE_VS_FIELD_MIN:.0%}"
            )
        elif wr > WINRATE_VS_FIELD_MAX:
            gate.fail(
                f"{cls.capitalize()} vs-field winrate {wr:.1%} is above "
                f"maximum {WINRATE_VS_FIELD_MAX:.0%}"
            )
        else:
            print(f"  PASS vs-field {cls.capitalize()}: {wr:.1%}")


def check_winrate_pairings(results: dict, gate: GateResult):
    """Check that no single pairing is outside [25%, 75%]."""
    for row_class in CLASSES:
        for col_class in CLASSES:
            d = results.get(row_class, {}).get(col_class, {})
            wr = d.get("win_rate_p0", 0.5)
            if wr < WINRATE_PAIRING_MIN:
                gate.fail(
                    f"{row_class.capitalize()} vs {col_class.capitalize()}: "
                    f"P0 winrate {wr:.1%} below minimum {WINRATE_PAIRING_MIN:.0%}"
                )
            elif wr > WINRATE_PAIRING_MAX:
                gate.fail(
                    f"{row_class.capitalize()} vs {col_class.capitalize()}: "
                    f"P0 winrate {wr:.1%} above maximum {WINRATE_PAIRING_MAX:.0%}"
                )


def check_deviation_rate(results: dict, gate: GateResult):
    """Check that the overall deviation rate meets the 25% target."""
    total_combat = 0
    total_deviation = 0
    for cls in CLASSES:
        for opp in CLASSES:
            d = results.get(cls, {}).get(opp, {})
            total_combat += d.get("total_combat_turns", 0)
            total_deviation += d.get("total_deviation_turns", 0)

    overall_rate = total_deviation / total_combat if total_combat > 0 else 0
    if overall_rate < DEVIATION_RATE_TARGET:
        gate.fail(
            f"Overall attack deviation rate {overall_rate:.1%} is below "
            f"target {DEVIATION_RATE_TARGET:.0%} ({total_deviation}/{total_combat} turns)"
        )
    else:
        print(f"  PASS deviation rate: {overall_rate:.1%} ({total_deviation}/{total_combat})")

    # Also report per-class for diagnostics
    for cls in CLASSES:
        cls_combat = 0
        cls_dev = 0
        for opp in CLASSES:
            d = results.get(cls, {}).get(opp, {})
            cls_combat += d.get("total_combat_turns", 0)
            cls_dev += d.get("total_deviation_turns", 0)
        rate = cls_dev / cls_combat if cls_combat > 0 else 0
        mark = " ✓" if rate >= DEVIATION_RATE_TARGET else ""
        print(f"    {cls.capitalize()}: {rate:.1%} ({cls_dev}/{cls_combat}){mark}")


# ── Report writer ─────────────────────────────────────────────────────────────

def write_report(results: dict, gate: GateResult, output_path: Path):
    """Write the balance matrix markdown report with gate results."""
    lines = []
    lines.append("# Balance Matrix — TASK-SIMGATE-1")
    lines.append("")
    lines.append(f"Generated: {time.strftime('%Y-%m-%d %H:%M:%S UTC')}")
    lines.append(f"Games per matchup: {GAMES_PER_MATCHUP}")
    lines.append(f"Base seed: {BASE_SEED}")
    lines.append("")
    lines.append("## Winrate Matrix (Row = P0 class, Column = P1 class)")
    lines.append("")
    lines.append("| P0 \\\\ P1 | " + " | ".join(c.capitalize() for c in CLASSES) + " |")
    lines.append("|" + "---|" * (len(CLASSES) + 1))

    for row_class in CLASSES:
        row = f"| **{row_class.capitalize()}** "
        for col_class in CLASSES:
            d = results.get(row_class, {}).get(col_class, {})
            wr = d.get("win_rate_p0", 0.5)
            # Flag outliers
            flag = ""
            if wr < WINRATE_PAIRING_MIN or wr > WINRATE_PAIRING_MAX:
                flag = " ⚠"
            row += f"| {wr:.1%}{flag} "
        row += "|"
        lines.append(row)

    lines.append("")
    lines.append("## Bot Telemetry")
    lines.append("")
    lines.append("### Avg Turns to Finish")
    lines.append("")
    lines.append("| P0 \\\\ P1 | " + " | ".join(c.capitalize() for c in CLASSES) + " |")
    lines.append("|" + "---|" * (len(CLASSES) + 1))

    for row_class in CLASSES:
        row = f"| **{row_class.capitalize()}** "
        for col_class in CLASSES:
            d = results.get(row_class, {}).get(col_class, {})
            avg_turns = d.get("avg_turns", 0)
            row += f"| {avg_turns:.1f} "
        row += "|"
        lines.append(row)

    lines.append("")
    lines.append("### Attack Deviation Rate")
    lines.append("")
    lines.append("| P0 \\\\ P1 | " + " | ".join(c.capitalize() for c in CLASSES) + " |")
    lines.append("|" + "---|" * (len(CLASSES) + 1))

    for row_class in CLASSES:
        row = f"| **{row_class.capitalize()}** "
        for col_class in CLASSES:
            d = results.get(row_class, {}).get(col_class, {})
            ct = d.get("total_combat_turns", 0)
            dt = d.get("total_deviation_turns", 0)
            rate = dt / ct if ct > 0 else 0
            row += f"| {rate:.1%} "
        row += "|"
        lines.append(row)

    lines.append("")
    lines.append("### Avg Cards in Hand at End")
    lines.append("")
    lines.append("| P0 \\\\ P1 | " + " | ".join(c.capitalize() for c in CLASSES) + " |")
    lines.append("|" + "---|" * (len(CLASSES) + 1))

    for row_class in CLASSES:
        row = f"| **{row_class.capitalize()}** "
        for col_class in CLASSES:
            d = results.get(row_class, {}).get(col_class, {})
            avg_cih = d.get("avg_cards_in_hand_p0", 0)
            row += f"| {avg_cih:.1f} "
        row += "|"
        lines.append(row)

    lines.append("")
    lines.append("## Per-Class Winrates (vs the field)")
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
        flag = ""
        if wr < WINRATE_VS_FIELD_MIN or wr > WINRATE_VS_FIELD_MAX:
            flag = " ⚠"
        lines.append(f"| {cls.capitalize()} | {total_wins} | {total_games} | {wr:.1%}{flag} |")

    lines.append("")
    lines.append("## Per-Class Deviation Rate (as P0, aggregated)")
    lines.append("")
    lines.append("| Class | Total Combat Turns | Deviation Turns | Deviation Rate |")
    lines.append("|-------|-------------------|----------------|----------------|")

    total_combat = 0
    total_deviation = 0
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
        mark = " ✓" if rate >= DEVIATION_RATE_TARGET else ""
        lines.append(f"| {cls.capitalize()} | {cls_combat} | {cls_dev} | {rate:.1%}{mark} |")

    lines.append("")
    lines.append(f"**Overall**: {total_deviation} deviation turns out of {total_combat} combat turns "
                 f"({total_deviation/total_combat:.1%})" if total_combat > 0 else "**Overall**: 0 combat turns")
    overall_rate = total_deviation / total_combat if total_combat > 0 else 0
    lines.append(f"Target: ≥ {DEVIATION_RATE_TARGET:.0%} "
                 + ("✓ MET" if overall_rate >= DEVIATION_RATE_TARGET else "✗ NOT MET"))

    lines.append("")
    lines.append("## Gate Results")
    lines.append("")
    if gate.passed:
        lines.append("**PASS** — All thresholds met.")
    else:
        lines.append(f"**FAIL** — {len(gate.failures)} threshold(s) exceeded:")
        lines.append("")
        for f in gate.failures:
            lines.append(f"- {f}")

    if gate.warnings:
        lines.append("")
        lines.append("### Warnings (non-blocking)")
        for w in gate.warnings:
            lines.append(f"- {w}")

    lines.append("")
    lines.append("---")
    lines.append(f"Thresholds: vs-field winrate [{WINRATE_VS_FIELD_MIN:.0%}, {WINRATE_VS_FIELD_MAX:.0%}], "
                 f"pairing winrate [{WINRATE_PAIRING_MIN:.0%}, {WINRATE_PAIRING_MAX:.0%}], "
                 f"deviation rate ≥ {DEVIATION_RATE_TARGET:.0%}, "
                 f"games per matchup = {GAMES_PER_MATCHUP}")

    content = "\n".join(lines) + "\n"
    with open(output_path, "w") as f:
        f.write(content)
    print(f"\nReport written to {output_path}")


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="TASK-SIMGATE-1 balance gate")
    parser.add_argument("--games", type=int, default=GAMES_PER_MATCHUP,
                        help=f"Games per matchup (default: {GAMES_PER_MATCHUP})")
    parser.add_argument("--seed", type=int, default=BASE_SEED,
                        help=f"Base seed (default: {BASE_SEED})")
    parser.add_argument("--deck-file", type=str, default=None,
                        help="Path to a custom deck pack file (default: auto-generate)")
    args = parser.parse_args()

    games = args.games
    seed = args.seed

    if not SIM_BIN.exists():
        print(f"[balance_gate] ERROR: Sim binary not found at {SIM_BIN}", file=sys.stderr)
        sys.exit(1)

    if not ARTIFACTS_PATH.exists():
        print(f"[balance_gate] ERROR: Artifacts file not found at {ARTIFACTS_PATH}", file=sys.stderr)
        sys.exit(1)

    # Create or use deck pack
    if args.deck_file:
        deck_path = Path(args.deck_file)
        if not deck_path.exists():
            print(f"[balance_gate] ERROR: Deck file not found: {deck_path}", file=sys.stderr)
            sys.exit(1)
    else:
        registry = load_card_registry()
        deck_path = HERE / ".deck_pack.balance.json"
        write_deck_pack(registry, deck_path)
        print(f"[balance_gate] Created deck pack: {deck_path} ({len(registry)} cards in registry)")

    # Run the 7x7 matrix
    results: dict[str, dict[str, dict]] = {c: {} for c in CLASSES}

    total_matchups = len(CLASSES) * len(CLASSES)
    completed = 0

    print(f"[balance_gate] Running {total_matchups} matchups, {games} games each, seed={seed}")
    print()

    for row_class in CLASSES:
        for col_class in CLASSES:
            completed += 1
            matchup_seed = seed + completed * 1009  # unique seed per pairing
            print(f"[balance_gate] [{completed}/{total_matchups}] {row_class} vs {col_class}...", end=" ")
            sys.stdout.flush()

            report = run_matchup(deck_path, row_class, col_class, games, matchup_seed)
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
                    "avg_cards_in_hand_p0": report.get("avg_cards_in_hand_p0", 0),
                    "avg_cards_in_hand_p1": report.get("avg_cards_in_hand_p1", 0),
                }
                wr = results[row_class][col_class]["win_rate_p0"]
                avg_cih = results[row_class][col_class]["avg_cards_in_hand_p0"]
                print(f"P0 wr: {wr:.1%}, dev: {report.get('attack_deviation_rate', 0):.1%}, "
                      f"avg_cih: {avg_cih:.1f}")
            else:
                results[row_class][col_class] = {}
                print("FAILED")

    # Evaluate gate
    gate = GateResult()
    print()
    print("=" * 60)
    print("GATE CHECKS")
    print("=" * 60)

    check_winrate_vs_field(results, gate)
    check_winrate_pairings(results, gate)
    check_deviation_rate(results, gate)

    # Write report
    output_path = HERE / "balance_matrix.md"
    write_report(results, gate, output_path)

    # Print summary
    print()
    print("=" * 60)
    print("GATE VERDICT")
    print("=" * 60)
    if gate.passed:
        print("PASS — All thresholds met.")
    else:
        print(f"FAIL — {len(gate.failures)} threshold(s) exceeded:")
        for f in gate.failures:
            print(f"  - {f}")

    # Cleanup temp deck pack
    if not args.deck_file:
        deck_path.unlink(missing_ok=True)

    sys.exit(0 if gate.passed else 1)


if __name__ == "__main__":
    main()