#!/usr/bin/env python3
"""
campaign_loop_sim.py — walk the Continue loop over the real region graph.

FABLE-012 makes Continue roll a victory straight into the next challenge, over
and over. That loop reads a region's node graph and its unlock rules, so it can
dead-end in ways no C# unit test will notice: a node whose encounter id does not
exist, an unlock that can never be satisfied, a chain that stops one fight in.

This is the same walk CampaignRun.FindNextDuelNode performs, run over every
region file, so a broken chain shows up here instead of on a phone.

    python3 tools/campaign_loop_sim.py          # all regions
    python3 tools/campaign_loop_sim.py region_01
"""
import glob
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DUEL_TYPES = {"duel", "elite", "warden", "warden_boss", "wardenboss"}


def load_encounters():
    enc = {}
    for f in sorted(glob.glob(os.path.join(ROOT, "content", "encounters", "*.json"))):
        try:
            d = json.load(open(f, encoding="utf-8"))
        except Exception as e:
            print(f"  !! unreadable {os.path.basename(f)}: {e}")
            continue
        for e in d.get("encounters", []):
            enc[e["id"]] = e
    return enc


def unlocked(node, cleared):
    u = node.get("unlock")
    if not u:
        return True
    if u.get("op") != "NODES_CLEARED":
        return False
    return all(x in cleared for x in u.get("value", []))


def is_duel(node):
    return str(node.get("type", "")).lower().replace("_", "") in {
        t.replace("_", "") for t in DUEL_TYPES}


def next_duel(nodes, cleared, enc):
    for n in nodes:
        if n["id"] in cleared:
            continue
        if not unlocked(n, cleared):
            continue
        if not is_duel(n):
            continue
        e = n.get("encounter")
        if not e or e not in enc:
            continue
        return n
    return None


def walk(region_path, enc, problems):
    region = json.load(open(region_path, encoding="utf-8"))
    nodes = region.get("nodes", [])
    name = os.path.basename(region_path).replace(".json", "")
    duels = [n for n in nodes if is_duel(n)]
    print(f"\n{name}: {len(nodes)} nodes, {len(duels)} duel-type")

    # Every duel node must point at an encounter that exists, or the loop will
    # silently skip it forever and the player can never finish the region.
    for n in duels:
        e = n.get("encounter")
        if not e:
            problems.append(f"{name}/{n['id']}: duel node with no encounter")
        elif e not in enc:
            problems.append(f"{name}/{n['id']}: encounter '{e}' does not exist")

    cleared, chain = set(), []
    while True:
        n = next_duel(nodes, cleared, enc)
        if n is None:
            break
        chain.append((n["id"], enc[n["encounter"]]["name"]))
        cleared.add(n["id"])
        if len(chain) > 200:
            problems.append(f"{name}: loop did not terminate")
            break

    for i, (nid, ename) in enumerate(chain, 1):
        print(f"  {i:2}. {nid:<16} {ename}")
    if not chain:
        problems.append(f"{name}: the loop cannot start — no reachable first duel")
        print("  (nothing reachable)")
        return

    blocked = [n["id"] for n in duels if n["id"] not in cleared]
    gates = sorted({v for n in nodes if n["id"] in blocked
                    for v in (n.get("unlock") or {}).get("value", [])
                    if v not in cleared})
    if blocked:
        print(f"  -> hands off to the map after {len(chain)}; still gated: "
              f"{', '.join(blocked)}")
        print(f"     waiting on non-duel nodes: {', '.join(gates) if gates else '(none)'}")
        # Gated behind a non-duel node (shrine, dig, merchant) is correct — the
        # loop should not auto-play those. Gated behind NOTHING reachable is a bug.
        for b in blocked:
            node = next(n for n in nodes if n["id"] == b)
            need = (node.get("unlock") or {}).get("value", [])
            if need and all(x in cleared for x in need):
                problems.append(f"{name}/{b}: unlocked but the loop never offered it")
    else:
        print(f"  -> every duel in the region reachable in one unbroken run of {len(chain)}")


def main():
    enc = load_encounters()
    print(f"{len(enc)} encounters indexed")
    wanted = sys.argv[1] if len(sys.argv) > 1 else None
    files = sorted(glob.glob(os.path.join(ROOT, "content", "map", "region_*.json")))
    if wanted:
        files = [f for f in files if wanted in os.path.basename(f)]
    if not files:
        print("no region files found", file=sys.stderr)
        return 1

    problems = []
    for f in files:
        walk(f, enc, problems)

    print()
    if problems:
        print(f"CAMPAIGN LOOP FAILED ({len(problems)} problem(s)):")
        for p in problems:
            print(f"  - {p}")
        return 1
    print("CAMPAIGN LOOP OK — every region starts, chains, and ends somewhere real")
    return 0


if __name__ == "__main__":
    sys.exit(main())
