#!/usr/bin/env python3
"""FABLE-020 — authoring script for Tower floor 1, "The Rootgate".

The Tower is AUTHORED, not generated: every place, name, line of text and
rule below is written by hand. This script only does the tedious part — it
picks each fight's 30-card deck from the card pool by the theme and strength
written here — and freezes the result into content/tower/floor_001.json.
Edit this file (not the JSON) and re-run it:

    python3 tools/tower/author_floor_001.py

Then validate and sim it:
    dotnet run --project sim -- tower-sim --floor 1
"""
import json, glob, os, hashlib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
cards = []
for f in sorted(glob.glob(os.path.join(ROOT, "content", "cards", "*.json"))):
    d = json.load(open(f))
    cards += d["cards"] if isinstance(d, dict) else d
POOL = [c for c in cards if c["type"] not in ("TOKEN", "ARTIFACT") and not c["id"].startswith("tut_")]
RANK = {"COMMON": 0, "UNCOMMON": 1, "RARE": 2, "RELIC": 3}

def value(c):
    if c["type"] == "CREATURE":
        v = (c.get("attack") or 0) + (c.get("vigor") or 0)
        v += 1.5 * len([k for k in c.get("keywords", []) if k in ("GUARD", "VENOM", "PIERCE", "SWIFT", "WARD", "REACH")])
        return v + 0.8 * RANK[c["rarity"]] - 0.35 * c["cost"]
    return 2.0 * c["cost"] + 1.5 * RANK[c["rarity"]]

def stable(s):
    return int(hashlib.sha1(s.encode()).hexdigest()[:8], 16)

def deck(place_id, strata, tier):
    """30 unique cards. tier: 0 duel, 1 elite, 2 keeper, 3 boss. Higher tier = stronger slice of the pool."""
    primary = [c for c in POOL if c["strata"] == strata[0]]
    rest = [c for c in POOL if c["strata"] in strata[1:]]
    other = [c for c in POOL if c["strata"] not in strata]
    keep = {0: 0.70, 1: 0.55, 2: 0.40, 3: 0.30}[tier]          # fraction of each group's strongest cards eligible
    out = []
    for group, n in ((primary, 18), (rest, 9), (other, 30)):
        ranked = sorted(group, key=lambda c: (-value(c), c["id"]))
        top = ranked[: max(n, int(len(ranked) * keep) + 1)]
        top.sort(key=lambda c: (stable(place_id + c["id"]), c["id"]))   # authored variety per place, stable forever
        for c in top:
            if len(out) >= 30: break
            if c["id"] in out: continue
            if group is not other and len([x for x in out if x in [g["id"] for g in group]]) >= n: break
            out.append(c["id"])
    return out[:30]

V, H, T, D, E = "VERDANT", "HOLLOW", "TIDE", "DAWN", "EMBER"

import os as _os, json as _json
# FABLE-021 tuning knobs (tower-sim decides these — see the comment on the boss below).
KEEPER_VIGOR = int(_os.environ.get("KEEPER_VIGOR", "40"))
KEEPER_ATTUNE = int(_os.environ.get("KEEPER_ATTUNE", "1"))
BOSS_ATTUNE = int(_os.environ.get("BOSS_ATTUNE", "1"))
RAID_POOLS = _json.loads(_os.environ.get("RAID_POOLS", "[62, 76, 84, 94, 105]"))
RAID_ROUNDS = int(_os.environ.get("RAID_ROUNDS", "22"))
BOSS_RULES = _json.loads(_os.environ.get("BOSS_RULES", '["extra_draw", "crumble:14:2"]'))


def fight(pid, name, kind, wing, x, y, nxt, strata, tier, vigor, attune, cls, intro, outro=None, requires=None, entry=False, opening=None, reward=None, shards=60, rules=None):
    enc = {
        "id": f"tower:1:{pid}", "name": name, "deck": deck(pid, strata, tier),
        "enemy_vigor": vigor, "enemy_attunement": attune, "class": cls,
        "dialogue_intro": intro, "shard_reward": shards,
        "dig_charge_reward": 1 if kind in ("Keeper", "Boss") else 0,
        "drops": [],
    }
    if outro: enc["dialogue_outro"] = outro
    if opening: enc["opening_rule"] = opening
    if rules: enc["boss_rules"] = rules
    if reward: enc["card_reward"] = reward
    p = {"id": pid, "name": name, "kind": kind, "wing": wing, "x": x, "y": y, "next": nxt, "encounter": enc}
    if requires: p["requires"] = requires
    if entry: p["entry"] = True
    return p

def event(pid, name, wing, x, y, nxt, kind, text, rewards, entry=False):
    p = {"id": pid, "name": name, "kind": "Event", "wing": wing, "x": x, "y": y, "next": nxt,
         "event": {"kind": kind, "text": text, "rewards": rewards}}
    if entry: p["entry"] = True
    return p

def lore(pid, name, wing, x, y, nxt, text):
    return {"id": pid, "name": name, "kind": "Lore", "wing": wing, "x": x, "y": y, "next": nxt, "lore": text}

places = [
    # ── The Courtyard: the way in ───────────────────────────────────────────
    fight("c1", "The Rootgate Threshold", "Duel", "courtyard", 70, 300, ["c3"], (V, H), 0, 30, 0, "druid",
          ["Roots as thick as a man's waist have split the Tower's first door.", "Something wearing bark for skin steps out of the gap."], entry=True),
    event("c2", "The Fallen Portcullis", "courtyard", 90, 470, ["c3"], "Shrine",
          "Under the fallen gate, a dry alcove. Rest here; nothing in the Tower will follow you in.", ["shard:40"], entry=True),
    fight("c3", "Hall of Broken Oaths", "Elite", "courtyard", 210, 330, ["t1", "o1", "f1"], (D, V), 1, 30, 0, "paladin",
          ["Every Delver who came before swore an oath at this door.", "Their oaths are still here. They have hardened into a knight."]),

    # ── The Thorn Galleries (upper wing) ────────────────────────────────────
    fight("t1", "Gallery of Hanging Seeds", "Duel", "thorn", 330, 150, ["t2", "t3"], (V, D), 0, 32, 0, "druid",
          ["Seed-pods the size of lanterns sway overhead. One of them is breathing."]),
    fight("t2", "The Briar Loom", "Duel", "thorn", 440, 80, ["t4"], (V, E), 0, 32, 0, "druid",
          ["A loom weaves thorns into cloth. The weaver has not stopped in four hundred years."]),
    lore("t3", "A Gardener's Last Page", "thorn", 440, 200, ["t4"],
         "“We planted the Rootgate to hold the seal shut, and it held. We did not ask what the roots would drink once the seal stopped feeding them. Now they drink us.” — torn page, pinned to the wall with a thorn"),
    fight("t4", "The Pruning Hall", "Elite", "thorn", 560, 130, ["t5", "t6"], (V, D), 1, 36, 1, "paladin",
          ["Blades hang from the ceiling on vines, swinging slowly.", "The Pruner wants to know which parts of you are dead wood."]),
    event("t5", "Dewglass Spring", "thorn", 660, 60, ["t7"], "Merchant",
          "A root-merchant sells what the Tower gives it: shards, and seeds that remember being cards.", ["shard:30"]),
    fight("t6", "Canopy of Knives", "Duel", "thorn", 670, 190, ["t7"], (V, T), 0, 34, 1, "druid",
          ["Leaves fall here like thrown blades."]),
    fight("t7", "Thornmother Vessa", "Keeper", "thorn", 790, 130, ["boss"], (V, D), 2, KEEPER_VIGOR, KEEPER_ATTUNE, "druid",
          ["The Galleries' keeper sits on a throne grown out of the Tower wall.", "“You are the first thing in a century I have not wanted to eat. Do not change my mind.”"],
          ["Vessa's throne withers. A key of green bark falls from her hand.", "“Take it. The Colossus is waiting for three of those.”"], shards=160),

    # ── The Ossuary Stair (middle wing) ─────────────────────────────────────
    fight("o1", "The First Landing", "Duel", "ossuary", 330, 320, ["o2"], (H, E), 0, 32, 0, "necromancer",
          ["The stair is built of bones laid like bricks. They shift as you climb."]),
    fight("o2", "Chapel of Counted Teeth", "Duel", "ossuary", 440, 300, ["o3", "o4"], (H, E), 0, 32, 0, "necromancer",
          ["A monk counts teeth into a bowl. He has reached a very large number.", "He would like yours."]),
    event("o3", "The Reliquary Niche", "ossuary", 540, 250, ["o5"], "Cache",
          "A sealed niche in the stair. Inside, wrapped in grave-cloth: something someone meant to come back for.", ["dig_charge:1", "shard:60"]),
    fight("o4", "The Drummers' Gallery", "Elite", "ossuary", 550, 370, ["o5", "o6"], (H, D), 1, 36, 1, "necromancer",
          ["Skeletal drummers beat a rhythm that makes your heart try to match it."]),
    lore("o5", "Stair-Warden's Ledger", "ossuary", 660, 300, ["o7"],
         "“Day 1: counted 11,204 steps. Day 40: counted 11,205. Day 90: stopped counting. The stair grows by one each time someone falls from it. I have not fallen. I have not fallen. I have not fallen.”"),
    fight("o6", "The Weeping Ossuary", "Duel", "ossuary", 660, 400, ["o7"], (H, T), 0, 34, 0, "necromancer",
          ["The bones here are wet. They have been crying for a long time."]),
    fight("o7", "The Bone-Abbot", "Keeper", "ossuary", 790, 330, ["boss"], (H, E), 2, KEEPER_VIGOR, KEEPER_ATTUNE, "necromancer",
          ["At the top of the stair, a figure in a mitre of vertebrae raises a crook.", "“Every step you took was once a pilgrim. Kneel, and become a step.”"],
          ["The Abbot collapses into a heap that is somehow still praying.", "A key of white bone rolls to your feet."], shards=160),

    # ── The Flooded Vestibule (lower wing) ──────────────────────────────────
    fight("f1", "The Drowned Antechamber", "Duel", "flooded", 330, 470, ["f2", "f3"], (T, H), 0, 32, 0, "battlemage",
          ["Black water, knee-deep and cold. Something swims past your ankles."]),
    event("f2", "The Lamplighter's Skiff", "flooded", 440, 420, ["f4"], "Shrine",
          "An old skiff with one lamp still burning. Sit in it a while. The water will not rise while the lamp is lit.", ["shard:40"]),
    fight("f3", "Gallery of Sunken Mirrors", "Duel", "flooded", 440, 540, ["f4"], (T, D), 0, 32, 0, "astrologist",
          ["Every mirror shows the room as it was before the flood. One shows you, drowned."]),
    fight("f4", "The Tide-Clock", "Elite", "flooded", 560, 480, ["f5", "f6"], (T, H), 1, 36, 0, "battlemage",
          ["A great clock ticks under the water. When it strikes, the tide in the room turns."]),
    lore("f5", "Letters in a Bottle", "flooded", 660, 430, ["f7"],
         "“My love — the Castellan has ordered the lower halls flooded so the roots cannot reach the seal from below. I am in the lower halls. Do not wait for me at the Rootgate. Wait for me at the top.”"),
    fight("f6", "The Kelp Cathedral", "Duel", "flooded", 670, 540, ["f7"], (T, V), 0, 34, 1, "astrologist",
          ["Kelp has grown into pillars and arches. The congregation is still here."]),
    fight("f7", "Drowned Castellan Irem", "Keeper", "flooded", 790, 500, ["boss"], (T, H), int(_os.environ.get("IREM_TIER", "1")), KEEPER_VIGOR, KEEPER_ATTUNE, "battlemage",
          ["The Castellan who flooded these halls stands in the water up to his chest.", "“I drowned my own people to keep the seal. What will YOU drown?”"],
          ["Irem sinks, finally, without a sound.", "A key of green-black coral floats up where he stood."], shards=160),

    # ── The Colossus (raid) ─────────────────────────────────────────────────
    # Raid tuning (FABLE-021, tower-sim --raids 80, TOP decks, GreedyBot): the pool scales with the
    # party — 62 alone … 105 for five — plus extra_draw and the floor crumbling from turn 14.
    # Result: 1 raider ~8%, 2 ~10%, 3–4 ~10–15%, 5 ~20%. Solo is possible, just harder.
    # Keepers: Vigor 40, +1 Attunement, one bent rule each → top decks ~15–20%, starters ~0–7%.
    # Retune in Supabase (tower_seed.sql), not the app.
    fight("boss", "The Rootbound Colossus", "Boss", "crown", 940, 310, [], (V, H, T), 3, RAID_POOLS[-1], BOSS_ATTUNE, "druid",
          ["The three keys turn together. The floor of the Tower's heart splits open.",
           "Something that was built to hold the seal shut stands up out of the roots — and it is very, very angry that you came.",
           "RAID: every Delver fights their own Colossus. Every blow lands on the same one.",
           "At the twenty-second round the Colossus brings the floor down. Finish it before then."],
          ["The Colossus kneels. The roots let go of the stair.", "Somewhere above, a door that has not opened in centuries groans."],
          requires=["t7", "o7", "f7"], opening="root_choked", shards=600),
]

# FABLE-021: the rules each Keeper and the Colossus bend (engine/Engine/BossRules.cs).
KEEPER_RULES = _json.loads(_os.environ.get("KEEPER_RULES", '{"t7": ["regen:2"], "o7": ["extra_draw"], "f7": ["crumble:14:1"]}'))
KEEPER_VIGOR_BY_ID = _json.loads(_os.environ.get("KEEPER_VIGOR_BY_ID", '{}'))
for _p in places:
    if _p["id"] in KEEPER_VIGOR_BY_ID:
        _p["encounter"]["enemy_vigor"] = KEEPER_VIGOR_BY_ID[_p["id"]]
    if _p["id"] in KEEPER_RULES:
        _p["encounter"]["boss_rules"] = KEEPER_RULES[_p["id"]]
    if _p["id"] == "boss" and BOSS_RULES:
        _p["encounter"]["boss_rules"] = BOSS_RULES

floor = {
    "floor": 1,
    "version": 2,
    "title": "The Rootgate",
    "theme": "The Tower's first floor: a gatehouse the seal's roots grew through. Three wings — thorn, bone and flood — each held by a Keeper. The Keepers' three keys wake the Colossus.",
    "strata": V, "strata2": H, "board_skin": "default",
    "intro": [
        "The Tower has one hundred floors. No one living has seen the second.",
        "This is the Rootgate. The roots that grew through it were planted to hold the seal shut. They held. Then they got hungry.",
        "Three Keepers guard three keys. The keys wake the Colossus. The Colossus has never fallen.",
    ],
    "unlock_threshold": 100,
    "wings": [
        {"id": "courtyard", "name": "The Courtyard", "blurb": "The way in."},
        {"id": "thorn", "name": "The Thorn Galleries", "blurb": "Verdant. Guardians that do not die easily."},
        {"id": "ossuary", "name": "The Ossuary Stair", "blurb": "Hollow. Venom, and the dead that come back."},
        {"id": "flooded", "name": "The Flooded Vestibule", "blurb": "Tide. Wards, echoes, and the long game."},
        {"id": "crown", "name": "The Heart of the Rootgate", "blurb": "The Colossus. A raid for up to five."},
    ],
    "places": places,
    "raid": {"max_players": 5, "shared_pool": RAID_POOLS[-1], "shared_pool_by_players": RAID_POOLS, "max_rounds": RAID_ROUNDS, "class": "druid"},
}
out = os.path.join(ROOT, "content", "tower", "floor_001.json")
with open(out, "w") as f:
    json.dump(floor, f, indent=2, ensure_ascii=False)
    f.write("\n")
print(f"wrote {out}: {len(places)} places, decks: " + ", ".join(f"{p['id']}={len(p['encounter']['deck'])}" for p in places if 'encounter' in p))
