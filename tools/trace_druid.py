#!/usr/bin/env python3
"""Run a Druid mirror duel with full debug trace of first 3 turns."""
import subprocess, json, sys

def run_sim(deck_a, deck_b, klass_a, klass_b, seed=42):
    """Run a single game via the sim CLI with trace output."""
    cmd = [
        "dotnet", "run", "--project", "sim", "--no-build", "--",
        "run",
        "--deck-a", deck_a,
        "--deck-b", deck_b,
        "--games", "1",
        "--seed", str(seed),
        "--class-a", klass_a,
        "--class-b", klass_b,
        "--artifacts-path", "content/artifacts/launch_artifacts.json",
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=120, cwd="/home/fictive/runewake")
    # Parse the JSON result from stdout
    try:
        data = json.loads(result.stdout)
        print(f"P0 wins: {data['p0_wins']}/{data['total_games']}")
        print(f"P1 wins: {data['p1_wins']}/{data['total_games']}")
        print(f"Avg turns: {data['avg_turns']}")
        return data
    except json.JSONDecodeError:
        print("STDERR:", result.stderr)
        print("STDOUT (last 3000):", result.stdout[-3000:])
        return None

def run_batch(deck_a, deck_b, klass_a, klass_b, seed=42, games=200, label=""):
    """Run a batch simulation."""
    cmd = [
        "dotnet", "run", "--project", "sim", "--no-build", "--",
        "run",
        "--deck-a", deck_a,
        "--deck-b", deck_b,
        "--games", str(games),
        "--seed", str(seed),
        "--class-a", klass_a,
        "--class-b", klass_b,
        "--artifacts-path", "content/artifacts/launch_artifacts.json",
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=300, cwd="/home/fictive/runewake")
    try:
        data = json.loads(result.stdout)
        p0_rate = data['win_rate_p0'] * 100
        print(f"{label} ({games}g seed {seed}): P0={data['p0_wins']}/{data['total_games']} ({p0_rate:.1f}%), avg {data['avg_turns']:.1f}t, first death {data['avg_turns_first_creature_death']:.1f}t")
        return data
    except json. DecodeError:
        print(f"PARSE ERROR for {label}:")
        print(result.stderr[-2000:])
        print(result.stdout[-2000:])
        return None

# Build first
subprocess.run(["dotnet", "build", "sim/", "-q"], cwd="/home/fictive/runewake", check=True)

if len(sys.argv) > 1 and sys.argv[1] == "--batch":
    # Run batch outputs
    run_batch("tmp/starter_druid.json", "tmp/starter_druid.json", "druid", "druid", games=200, label="Druid mirror")
    run_batch("tmp/starter_warrior.json", "tmp/starter_druid.json", "warrior", "druid", games=200, label="Warrior(P0) vs Druid(P1)")
else:
    # Single game trace
    result = run_sim("tmp/starter_druid.json", "tmp/starter_druid.json", "druid", "druid", seed=42)