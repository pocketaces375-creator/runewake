#!/usr/bin/env python3
"""
TASK-TUNE-5050-1: Balance all 7 classes to a 45-55% win rate against each other.

Loads each class's starter deck from client/content/decks/starter_decks.json,
runs the full 7×7 class matchup matrix (49 pairings, 200 games each),
reports per-class winrate with best/worst matchup.

If any class is outside 45-55%: adjust numbers from TASK-CLASS-IDENTITY-1A/1B/1C
bounds and the six existing items' numbers by at most +/-1 per number per iteration.
Rush classes (Rogue, Warrior) must not drop below 45%.
Up to 3 iterations. ADOPT if in band, report-only if not.
"""

import argparse
import json
import shutil
import subprocess
import sys
import time
from copy import deepcopy
from pathlib import Path

# ── Paths ──
HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
SIM_BIN = ROOT / "sim" / "bin" / "Debug" / "net8.0" / "Runewake.Sim"
ARTIFACTS_PATH = ROOT / "content" / "artifacts" / "launch_artifacts.json"
STARTER_DECKS_PATH = ROOT / "client" / "content" / "decks" / "starter_decks.json"

GAMES_PER_MATCHUP = 200
BASE_SEED = 42

CLASSES = [
    "battlemage",
    "necromancer",
    "paladin",
    "druid",
    "rogue",
    "astrologist",
    "warrior",
]

WINRATE_MIN = 0.45
WINRATE_MAX = 0.55

DECK_PACKS_DIR = HERE / ".deck_packs"
WORKING_ARTIFACT_PATH = DECK_PACKS_DIR / "working_launch_artifacts.json"


def load_card_registry() -> dict[str, dict]:
    registry: dict[str, dict] = {}
    for strata in ["verdant", "ember", "tide", "hollow", "dawn"]:
        path = ROOT / "content" / "cards" / f"{strata}.json"
        if path.exists():
            with open(path) as f:
                for card in json.load(f):
                    registry[card["id"]] = card
    # Neutral cards
    for path in [ROOT / "content" / "cards" / "neutral.json"]:
        if path.exists():
            with open(path) as f:
                for card in json.load(f):
                    registry[card["id"]] = card
    return registry


def load_starter_decks() -> dict[str, dict]:
    with open(STARTER_DECKS_PATH) as f:
        data = json.load(f)
    return {entry["class_id"]: entry for entry in data["starters"]}


def build_class_deck_pack(registry: dict, class_id: str, starter: dict) -> list[dict]:
    pack = []
    for cid in starter["cards"]:
        card = registry.get(cid)
        if card is not None:
            pack.append(card)
        else:
            print(f"[tune_5050] WARNING: card '{cid}' not found in registry", file=sys.stderr)
    return pack


def write_deck_pack(cards: list[dict], path: Path):
    with open(path, "w") as f:
        json.dump(cards, f)


def run_matchup(deck_a_path: Path, deck_b_path: Path, class_a: str, class_b: str,
                games: int, seed: int) -> dict | None:
    try:
        result = subprocess.run(
            [str(SIM_BIN), "run",
             "--deck-a", str(deck_a_path),
             "--deck-b", str(deck_b_path),
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
        print(f"[tune_5050] ERROR: CLI not found at {SIM_BIN}", file=sys.stderr)
        return None
    except subprocess.TimeoutExpired:
        print(f"[tune_5050] ERROR: CLI timed out", file=sys.stderr)
        return None

    if result.returncode != 0:
        print(f"[tune_5050] ERROR: CLI returned {result.returncode}", file=sys.stderr)
        print(f"  stderr: {result.stderr[:500]}", file=sys.stderr)
        return None

    for line in result.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            return json.loads(line)
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        print(f"[tune_5050] ERROR: could not parse CLI output", file=sys.stderr)
        return None


def run_full_matrix(deck_packs: dict[str, Path], games: int, seed: int,
                    iteration: int = 0) -> dict[str, dict[str, dict]]:
    results: dict[str, dict[str, dict]] = {c: {} for c in CLASSES}
    total_matchups = len(CLASSES) * len(CLASSES)
    completed = 0

    print(f"\n[tune_5050] Iteration {iteration}: {total_matchups} matchups, {games} games each, seed={seed}")

    for row_class in CLASSES:
        for col_class in CLASSES:
            completed += 1
            matchup_seed = seed + completed * 1009
            print(f"  [{completed}/{total_matchups}] {row_class} (P0) vs {col_class} (P1)...", end=" ")
            sys.stdout.flush()

            report = run_matchup(
                deck_packs[row_class], deck_packs[col_class],
                row_class, col_class, games, matchup_seed
            )
            if report is not None:
                p0_wins = report.get("p0_wins", 0)
                p1_wins = report.get("p1_wins", 0)
                total = report.get("total_games", 0)
                wr_p0 = p0_wins / total if total > 0 else 0.5
                results[row_class][col_class] = {
                    "p0_wins": p0_wins,
                    "p1_wins": p1_wins,
                    "total_games": total,
                    "win_rate_p0": wr_p0,
                }
                print(f"P0 wr: {wr_p0:.1%} ({p0_wins}/{total})")
            else:
                results[row_class][col_class] = {}
                print("FAILED")

    return results


def compute_class_stats(results: dict) -> dict[str, dict]:
    stats = {}
    for cls in CLASSES:
        total_wins = 0
        total_games = 0
        pairings = {}
        for opp in CLASSES:
            if cls == opp:
                continue
            d = results.get(cls, {}).get(opp, {})
            p0_wins = d.get("p0_wins", 0)
            total_this = d.get("total_games", 0)
            d_rev = results.get(opp, {}).get(cls, {})
            rev_p0_wins = d_rev.get("p0_wins", 0)
            rev_total = d_rev.get("total_games", 0)
            cls_wins = p0_wins + (rev_total - rev_p0_wins)
            cls_games = total_this + rev_total
            total_wins += cls_wins
            total_games += cls_games
            wr_combined = cls_wins / cls_games if cls_games > 0 else 0.5
            pairings[opp] = {"wins": cls_wins, "games": cls_games, "winrate": wr_combined}

        winrate = total_wins / total_games if total_games > 0 else 0.5
        best = max(pairings.items(), key=lambda x: x[1]["winrate"])
        worst = min(pairings.items(), key=lambda x: x[1]["winrate"])

        stats[cls] = {
            "winrate_vs_field": winrate,
            "wins": total_wins,
            "games": total_games,
            "best_matchup": (best[0], best[1]["winrate"]),
            "worst_matchup": (worst[0], worst[1]["winrate"]),
            "pairings": pairings,
        }
    return stats


def check_band(stats: dict) -> list[str]:
    violations = []
    for cls in CLASSES:
        wr = stats[cls]["winrate_vs_field"]
        if cls in ("rogue", "warrior") and wr < WINRATE_MIN:
            violations.append(f"{cls}: {wr:.1%} (RUSH below {WINRATE_MIN:.0%}% minimum)")
        elif wr < WINRATE_MIN or wr > WINRATE_MAX:
            violations.append(f"{cls}: {wr:.1%} outside [{WINRATE_MIN:.0%}, {WINRATE_MAX:.0%}]")
    return violations


def format_winrate_matrix(results: dict) -> str:
    lines = []
    header = f"{'P0\\P1':>12}"
    for c in CLASSES:
        header += f"  {c.capitalize():>6}"
    lines.append(header)
    for row_class in CLASSES:
        row = f"{row_class.capitalize():>12}"
        for col_class in CLASSES:
            d = results.get(row_class, {}).get(col_class, {})
            wr = d.get("win_rate_p0", 0.5)
            flag = "!" if wr < 0.25 or wr > 0.75 else ""
            row += f"  {wr:.1%}{flag}"
        lines.append(row)
    return "\n".join(lines)


def write_report(results: dict, stats: dict, violations: list[str],
                 output_path: Path, iteration: int, adopted: bool = False,
                 adjustments: list[str] | None = None):
    lines = []
    lines.append("# TASK-TUNE-5050-1 — Class Balance Matrix")
    lines.append("")
    lines.append(f"Generated: {time.strftime('%Y-%m-%d %H:%M:%S UTC')}")
    lines.append(f"Iteration: {iteration}")
    lines.append(f"Games per matchup: {GAMES_PER_MATCHUP}")
    lines.append(f"Base seed: {BASE_SEED}")
    lines.append(f"Target band: [{WINRATE_MIN:.0%}, {WINRATE_MAX:.0%}]")
    status = "ADOPTED" if adopted else "REPORT ONLY" if iteration >= 3 else "IN PROGRESS"
    lines.append(f"Status: {status}")
    lines.append(f"Violations: {len(violations)} {'— all in band' if not violations else ''}")
    lines.append("")

    if adjustments:
        lines.append("### Adjustments applied")
        for a in adjustments:
            lines.append(f"- {a}")
        lines.append("")

    lines.append("## Winrate Matrix (P0 vs P1)")
    lines.append("")
    lines.append("```")
    lines.append(format_winrate_matrix(results))
    lines.append("```")
    lines.append("")

    lines.append("## Per-Class Winrates (mirrors excluded)")
    lines.append("")
    lines.append("| Class | Winrate | Wins | Games | Best Matchup | Worst Matchup |")
    lines.append("|-------|---------|------|-------|--------------|---------------|")
    for cls in CLASSES:
        s = stats[cls]
        wr = s["winrate_vs_field"]
        flag = ""
        if cls in ("rogue", "warrior") and wr < WINRATE_MIN:
            flag = " ⚠ RUSH"
        elif wr < WINRATE_MIN or wr > WINRATE_MAX:
            flag = " ⚠"
        best = s["best_matchup"]
        worst = s["worst_matchup"]
        lines.append(f"| {cls.capitalize()} | {wr:.1%}{flag} | {s['wins']} | {s['games']} | "
                      f"{best[0].capitalize()} ({best[1]:.1%}) | {worst[0].capitalize()} ({worst[1]:.1%}) |")
    lines.append("")

    lines.append("## Mirror Match P0 Winrates")
    for cls in CLASSES:
        d = results.get(cls, {}).get(cls, {})
        wr = d.get("win_rate_p0", 0.5)
        lines.append(f"- {cls.capitalize()}: P0 {wr:.1%}")
    lines.append("")

    if violations:
        lines.append("## Violations")
        for v in violations:
            lines.append(f"- {v}")
        lines.append("")
        if iteration >= 3:
            lines.append("### Two Worst Outliers (for Fable)")
            sorted_v = sorted(
                [(c, stats[c]["winrate_vs_field"]) for c in CLASSES],
                key=lambda x: abs(x[1] - 0.5), reverse=True
            )
            for cls, wr in sorted_v[:2]:
                lines.append(f"- {cls.capitalize()}: {wr:.1%} (delta {abs(wr - 0.5):.1%})")
    else:
        lines.append("**All classes in band.**")
        if adopted:
            lines.append("**Numbers adopted and shipped.**")
    lines.append("")
    lines.append(f"AI: GreedyBot (TASK-AI-TACTICIAN-1 pending)")
    lines.append(f"Thresholds: per-class [{WINRATE_MIN:.0%}, {WINRATE_MAX:.0%}], "
                 f"{GAMES_PER_MATCHUP} games/pairing")

    content = "\n".join(lines) + "\n"
    with open(output_path, "w") as f:
        f.write(content)
    print(f"\n[tune_5050] Report written to {output_path}")


def adjust_numbers_on_artifacts(artifacts: list[dict], class_stats: dict[str, dict],
                                adjustments_log: list[str]) -> list[dict]:
    """Adjust artifact numbers within TASK-CLASS-IDENTITY-1A/1B/1C bounds."""
    arts = deepcopy(artifacts)

    def find_aid(aid: str) -> dict | None:
        for a in arts:
            if a["id"] == aid:
                return a
        return None

    for cls in CLASSES:
        wr = class_stats[cls]["winrate_vs_field"]
        if WINRATE_MIN <= wr <= WINRATE_MAX:
            continue
        over = wr > WINRATE_MAX
        delta = -1 if over else +1

        if cls == "astrologist":
            orb = find_aid("artf_astrologist_orb")
            starlight = find_aid("artf_astrologist_constellation_starlight")
            if orb and "trigger" in orb and "effects" in orb["trigger"]:
                for eff in orb["trigger"]["effects"]:
                    if eff.get("op") == "DRAW" and 1 <= eff.get("amount", 2) + delta <= 2:
                        old = eff["amount"]
                        eff["amount"] += delta
                        adjustments_log.append(f"Astrologist Orb draw: {old} → {eff['amount']} (bound 1-2)")
            if starlight and "trigger" in starlight and "effects" in starlight["trigger"]:
                for eff in starlight["trigger"]["effects"]:
                    if eff.get("op") == "DAMAGE" and 3 <= eff.get("amount", 4) + delta <= 6:
                        old = eff["amount"]
                        eff["amount"] += delta
                        adjustments_log.append(f"Constellation Starlight damage: {old} → {eff['amount']} (bound 3-6)")

        elif cls == "paladin":
            banner = find_aid("artf_paladin_banner")
            if banner and "full_charge" in banner:
                for eff in banner["full_charge"]:
                    if eff.get("op") == "HEAL" and isinstance(eff.get("amount"), int):
                        if 1 <= eff["amount"] + delta <= 3:
                            old = eff["amount"]
                            eff["amount"] += delta
                            adjustments_log.append(f"Paladin Banner heal: {old} → {eff['amount']} (bound 1-3)")

        elif cls == "druid":
            book = find_aid("artf_druid_book_of_familiar")
            if book and "trigger" in book and "effects" in book["trigger"]:
                for eff in book["trigger"]["effects"]:
                    if eff.get("op") == "SUMMON":
                        if isinstance(eff.get("attack"), int) and 1 <= eff["attack"] + delta <= 2:
                            old = eff["attack"]
                            eff["attack"] += delta
                            adjustments_log.append(f"Druid Book Familiar attack: {old} → {eff['attack']} (bound 1-2)")
                        if isinstance(eff.get("vigor"), int) and 1 <= eff["vigor"] + delta <= 2:
                            old = eff["vigor"]
                            eff["vigor"] += delta
                            adjustments_log.append(f"Druid Book Familiar vigor: {old} → {eff['vigor']} (bound 1-2)")

        elif cls == "rogue":
            whisper = find_aid("artf_rogue_dagger_whisper")
            if over and whisper:
                if "full_charge" in whisper:
                    for eff in whisper["full_charge"]:
                        if eff.get("op") == "DAMAGE" and isinstance(eff.get("amount"), int):
                            if 2 <= eff["amount"] + delta <= 4:
                                old = eff["amount"]
                                eff["amount"] += delta
                                adjustments_log.append(f"Rogue Whisper face damage: {old} → {eff['amount']} (bound 2-4)")

        elif cls == "warrior":
            # RUSH — if too strong, adjust the six items. War is rush so can't go below 45%.
            if over:
                pass  # handle in six-items step below

    # Step 2: Adjust six existing items' numbers
    six_ids = [
        "artf_warrior_sword", "artf_warrior_shield",
        "artf_battlemage_wand", "artf_battlemage_aura",
        "artf_necromancer_skull", "artf_paladin_hammer",
    ]

    for cls in CLASSES:
        wr = class_stats[cls]["winrate_vs_field"]
        if WINRATE_MIN <= wr <= WINRATE_MAX:
            continue
        delta = -1 if wr > WINRATE_MAX else +1

        class_items = [a for a in arts if a["class"] == cls and a["id"] in six_ids]
        for item in class_items:
            if "passive" in item and item["passive"].get("op") == "BUFF":
                atk = item["passive"].get("attack", 0)
                vig = item["passive"].get("vigor", 0)
                if isinstance(atk, int) and isinstance(vig, int):
                    if atk + delta >= 0 and atk + delta <= 5 and atk > 0:
                        old = atk
                        item["passive"]["attack"] = atk + delta
                        adjustments_log.append(f"{item['id'].replace('artf_', '')}: passive atk {old} → {atk + delta}")
                    if vig + delta >= 0 and vig + delta <= 5 and vig > 0:
                        old = vig
                        item["passive"]["vigor"] = vig + delta
                        adjustments_log.append(f"{item['id'].replace('artf_', '')}: passive vig {old} → {vig + delta}")

            # Skull's COST_MOD
            if item["id"] == "artf_necromancer_skull":
                if "passive" in item and item["passive"].get("op") == "COST_MOD":
                    if isinstance(item["passive"].get("amount"), int):
                        if 0 <= item["passive"]["amount"] + delta <= 3:
                            old = item["passive"]["amount"]
                            item["passive"]["amount"] += delta
                            adjustments_log.append(f"necromancer_skull: COST_MOD {old} → {item['passive']['amount']}")
                if "trigger" in item and "effects" in item["trigger"]:
                    for eff in item["trigger"]["effects"]:
                        if eff.get("op") == "REVIVE_TOKEN":
                            if isinstance(eff.get("attack"), int) and 1 <= eff["attack"] + delta <= 5:
                                old = eff["attack"]
                                eff["attack"] += delta
                                adjustments_log.append(f"necromancer_skull: revive atk {old} → {eff['attack']}")
                            if isinstance(eff.get("vigor"), int) and 1 <= eff["vigor"] + delta <= 5:
                                old = eff["vigor"]
                                eff["vigor"] += delta
                                adjustments_log.append(f"necromancer_skull: revive vig {old} → {eff['vigor']}")

    return arts


def save_artifacts(artifacts: list[dict], path: Path):
    with open(path, "w") as f:
        json.dump(artifacts, f, indent=2)


def main():
    parser = argparse.ArgumentParser(description="TASK-TUNE-5050-1")
    parser.add_argument("--games", type=int, default=GAMES_PER_MATCHUP)
    parser.add_argument("--seed", type=int, default=BASE_SEED)
    parser.add_argument("--max-iterations", type=int, default=3)
    parser.add_argument("--adopt", action="store_true",
                        help="Write adjusted numbers to launch_artifacts.json")
    args = parser.parse_args()

    games = args.games
    seed = args.seed
    max_iter = args.max_iterations

    if not SIM_BIN.exists():
        print(f"[tune_5050] ERROR: Sim binary not found at {SIM_BIN}", file=sys.stderr)
        sys.exit(1)
    if not ARTIFACTS_PATH.exists():
        print(f"[tune_5050] ERROR: Artifacts file not found at {ARTIFACTS_PATH}", file=sys.stderr)
        sys.exit(1)

    # Load cards and starter decks
    registry = load_card_registry()
    print(f"[tune_5050] Loaded {len(registry)} cards in registry")
    starters = load_starter_decks()
    print(f"[tune_5050] Loaded {len(starters)} starter decks")
    for cls in CLASSES:
        if cls not in starters:
            print(f"[tune_5050] ERROR: No starter deck for '{cls}'", file=sys.stderr)
            sys.exit(1)

    # Build per-class deck packs
    DECK_PACKS_DIR.mkdir(exist_ok=True)
    deck_pack_paths: dict[str, Path] = {}
    for cls in CLASSES:
        pack = build_class_deck_pack(registry, cls, starters[cls])
        path = DECK_PACKS_DIR / f"{cls}_starter.json"
        write_deck_pack(pack, path)
        deck_pack_paths[cls] = path
        print(f"[tune_5050] {cls}: {len(pack)} cards → {path}")

    # Load and prepare artifacts
    with open(ARTIFACTS_PATH) as f:
        original_artifacts = json.load(f)
    working_artifacts = deepcopy(original_artifacts)

    iteration = 0
    final_results = None
    final_stats = None
    final_violations = []
    adjustments_log: list[str] = []
    adopted = False

    while iteration < max_iter:
        iteration += 1
        print(f"\n{'='*70}")
        print(f"ITERATION {iteration}")
        print(f"{'='*70}")

        # Write working artifacts, then copy to ARTIFACTS_PATH
        save_artifacts(working_artifacts, WORKING_ARTIFACT_PATH)
        shutil.copy2(WORKING_ARTIFACT_PATH, ARTIFACTS_PATH)

        results = run_full_matrix(deck_pack_paths, games, seed, iteration)
        stats = compute_class_stats(results)
        violations = check_band(stats)

        final_results = results
        final_stats = stats
        final_violations = violations

        write_report(results, stats, violations,
                     HERE / f"balance_matrix_iter{iteration}.md",
                     iteration, adjustments=adjustments_log if adjustments_log else None)

        if not violations:
            print(f"\n{'='*70}")
            print(f"ALL CLASSES IN BAND after {iteration} iteration(s)!")
            print(f"{'='*70}")
            if args.adopt:
                adopted = True
            break

        if iteration >= max_iter:
            print(f"\n{'='*70}")
            print(f"MAX ITERATIONS ({max_iter}) — {len(violations)} class(es) still outside band")
            print(f"{'='*70}")
            break

        # Adjust numbers
        adjustments_log = []
        print(f"\nAdjusting numbers...")
        working_artifacts = adjust_numbers_on_artifacts(working_artifacts, stats, adjustments_log)
        if adjustments_log:
            for a in adjustments_log:
                print(f"  {a}")
        else:
            print("  No adjustments possible within bounds — stopping iteration")
            break

    # Write final report
    write_report(final_results, final_stats, final_violations,
                 HERE / "balance_matrix.md",
                 iteration, adopted=adopted,
                 adjustments=adjustments_log if adjustments_log else None)

    # Cleanup temp files
    if DECK_PACKS_DIR.exists():
        shutil.rmtree(DECK_PACKS_DIR)

    # Restore original artifacts if not adopting
    if not adopted and args.adopt:
        save_artifacts(original_artifacts, ARTIFACTS_PATH)
        print("[tune_5050] Artifacts restored to original (not adopted)")

    # Print summary
    print(f"\n{'='*70}")
    print("SUMMARY")
    print(f"{'='*70}")
    print(f"Iterations: {iteration}")
    print(f"Status: {'ADOPTED' if adopted else 'REPORT ONLY'}")
    if final_stats:
        for cls in CLASSES:
            s = final_stats[cls]
            wr = s["winrate_vs_field"]
            flag = " ⚠" if wr < WINRATE_MIN or wr > WINRATE_MAX else ""
            print(f"  {cls.capitalize():>12}: {wr:.1%}{flag}  "
                  f"best={s['best_matchup'][0]}:{s['best_matchup'][1]:.1%}, "
                  f"worst={s['worst_matchup'][0]}:{s['worst_matchup'][1]:.1%}")

    if adopted:
        print(f"\nNumbers ADOPTED and written to {ARTIFACTS_PATH}")
    elif not final_violations:
        print("\nIn band but --adopt not set")
    else:
        print(f"\nNot in band after {iteration} iteration(s)")
        sorted_v = sorted(
            [(c, final_stats[c]["winrate_vs_field"]) for c in CLASSES],
            key=lambda x: abs(x[1] - 0.5), reverse=True
        )
        print("Two worst outliers:")
        for cls, wr in sorted_v[:2]:
            print(f"  {cls.capitalize()}: {wr:.1%} (delta {abs(wr - 0.5):.1%})")


if __name__ == "__main__":
    main()