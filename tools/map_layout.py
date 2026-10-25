#!/usr/bin/env python3
"""FABLE-105: lay a campaign map out on its painting BY THE TERRAIN RULES (docs/WORLD_TERRAIN.md).

Trikzos: "you did what we did, but then didn't use the entire rest of the map ... use critical thinking with
where the dots should go." The rules this script enforces, in order:

  1. THE MAIN PATH CROSSES THE WHOLE LAND. It follows the plate's ROAD (content/map/terrain/<region>.json) from
     one end of the island to the other - the start on one shore, the boss at the far end - with the upper lane
     of the graph riding the high ground on one side of the road and the lower lane the low ground on the other.
  2. EVERY PLACE STANDS ON A TERRAIN SPOT THAT FITS IT. A Warden holds a pass or a hold, a Dig a cave or a wreck,
     a Merchant a ford or a cove, an Elite a peak or the heart of a wood (fits come from content/world/terrain.json).
  3. NO TWO PLACES CROWD. Places keep MIN_GAP apart so their labels never collide; nothing stands under the title
     banner, the left button column or the node panel (KEEP_OUT).
  4. EVERY PART OF THE LAND IS VISITED. Side places (graph nodes marked "zone": "side") go to the areas the main
     path left empty - the biggest first, nearest their parent - so the islands, the spit and the peninsula carry
     a dot too.
  5. EVERY PLACE IS NAMED FOR ITS GROUND: a stratum adjective and the spot's noun ("Grey Saddle", "Ash Wreck").

    python3 tools/map_layout.py                 # lay out every class zone, print the report, write a contact sheet
    python3 tools/map_layout.py --sheet out.jpg

Imported by tools/gen_start_zones.py, which writes the result into content/start_zones.json.
"""
import argparse
import json
import math
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MIN_GAP = 118.0          # plate px between any two places (labels are ~200 px wide but sit under the icon)
MIN_GAP_RELAXED = 92.0
REACH = 175.0            # how far a place may step from its ideal point onto a spot
REACH_RELAXED = 260.0
ACROSS = 110.0           # how far the two lanes sit off the road, either side (the optimiser tries a range around it)
OUTLYING = {"south_isle", "north_strip", "north_islet"}   # reached by sea: kept for side places, never on the main path
TRIES = 240              # layouts tried per zone; the one that breaks the fewest rules wins
FIT_OF = {"WardenBoss": "AreaBoss"}
CHOOSY = {"WardenBoss": 0, "Warden": 1, "Elite": 2, "Dig": 3, "Merchant": 4}
PLACE_ADJ = {   # enough per stratum that a map of 15 places never says the same word twice
    "TIDE": ["Grey", "Salt", "Tidewashed", "Brine", "Mist", "Gull", "Pale", "Drift", "Kelp", "Foam", "Wrack", "Sounding", "Low", "Cold"],
    "EMBER": ["Ash", "Cinder", "Scorched", "Brass", "Red", "Kiln", "Smoulder", "Char", "Forge", "Slag", "Flint", "Burnt", "Iron", "Copper"],
    "VERDANT": ["Moss", "Thorn", "Green", "Root", "Fern", "Bramble", "Elder", "Dew", "Briar", "Oakshade", "Hazel", "Willow", "Wild", "Deep"],
    "HOLLOW": ["Bone", "Hollow", "Grave", "Dusk", "Wither", "Shade", "Crow", "Rust", "Barrow", "Ghost", "Pale", "Knell", "Sorrow", "Night"],
    "DAWN": ["Gold", "Sun", "Bright", "Amber", "Morning", "High", "White", "Fair", "Gilt", "Lark", "Halo", "Glory", "Clear", "Noon"],
}
# where each main node sits in the graph's shape: (along the road 0..1, across: -1 upper lane .. +1 lower lane)
SHAPE = {
    "r1_n01": (0.00, 0.0),
    "r1_n02": (0.17, -0.9), "r1_n05": (0.24, 0.85),
    "r1_n04": (0.36, -1.0), "r1_n07": (0.47, 0.05), "r1_n08": (0.42, 1.0),
    "r1_n06": (0.58, -0.95), "r1_n09": (0.72, -0.75), "r1_n10": (0.69, 0.8),
    "r1_n11": (0.86, 0.1), "r1_n12": (1.00, 0.0),
}


def load(region="region_01"):
    lex = json.load(open(ROOT / "content" / "world" / "terrain.json"))["features"]
    plate = json.load(open(ROOT / "content" / "map" / "terrain" / f"{region}.json"))
    graph = json.load(open(ROOT / "content" / "map" / f"{region}.json"))
    spots = []
    for f in plate["features"]:
        kinds = {s["kind"]: s for s in lex[f["kind"]]["spots"]}
        for sp in f["spots"]:
            d = kinds[sp["kind"]]
            spots.append(dict(feature=f["id"], terrain=f["kind"], area=f["area"], kind=sp["kind"], x=sp["x"], y=sp["y"],
                              nouns=d["nouns"], fits=d["fits"]))
    return plate, graph, spots


def _in(box, x, y, pad=0):
    return box[0] - pad <= x <= box[2] + pad and box[1] - pad <= y <= box[3] + pad


def road_at(road, t):
    """Point and unit normal at fraction t (0..1) of the road's length."""
    seg = [math.dist(road[i], road[i + 1]) for i in range(len(road) - 1)]
    total, want = sum(seg), max(0.0, min(1.0, t)) * sum(seg)
    for i, L in enumerate(seg):
        if want <= L or i == len(seg) - 1:
            u = want / L if L else 0
            (x0, y0), (x1, y1) = road[i], road[i + 1]
            dx, dy = (x1 - x0) / L, (y1 - y0) / L
            return (x0 + (x1 - x0) * u, y0 + (y1 - y0) * u), (-dy, dx)
        want -= L


def _segments_cross(a, b, c, d):
    def o(p, q, r):
        return (q[0] - p[0]) * (r[1] - p[1]) - (q[1] - p[1]) * (r[0] - p[0])
    if a in (c, d) or b in (c, d):
        return False
    return (o(a, b, c) * o(a, b, d) < 0) and (o(c, d, a) * o(c, d, b) < 0)


def layout(start_east, mirror, strata, seed, region="region_01", tries=TRIES):
    """Try many layouts (lane width, small slides along the road) and keep the one that breaks the fewest rules:
    roads crossing, land left without a dot, places crowding, roads too long to read as one step."""
    best, best_cost = None, None
    rnd = random.Random(seed * 7919 + 1)
    for k in range(tries):
        across = ACROSS if k == 0 else rnd.uniform(78, 150)
        jitter = {} if k == 0 else {n: (rnd.uniform(-0.04, 0.04), rnd.uniform(-0.25, 0.25)) for n in SHAPE}
        pos, places, areas = _layout_once(start_east, mirror, strata, seed, region, across, jitter)
        r = report(pos, areas, region)
        cost = r["crossings"] * 1000 + len(r["missing"]) * 700 + max(0, 112 - r["min_gap"]) * 40 + r["long_edges"] * 6
        if best_cost is None or cost < best_cost:
            best, best_cost = (pos, places, areas), cost
    return best


def _layout_once(start_east, mirror, strata, seed, region, across, jitter):
    plate, graph, spots = load(region)
    road = plate["road"][::-1] if start_east else plate["road"]
    keep_out = [k["box"] for k in plate.get("keep_out", [])]
    nodes = {n["id"]: n for n in graph["nodes"]}
    side = [n["id"] for n in graph["nodes"] if n.get("zone") == "side"]
    main = [i for i in nodes if i not in side]
    sign = -1.0 if mirror else 1.0
    if start_east:
        sign = -sign        # the normal turns with the road; keep "upper lane" meaning the same side of the island per mirror flag
    rnd = random.Random(seed)
    usable = [s for s in spots if not any(_in(b, s["x"], s["y"], 10) for b in keep_out)]
    mainland = [s for s in usable if s["area"] not in OUTLYING]
    placed, spot_of = {}, {}

    def free(s, gap):
        return not s.get("taken") and all(math.dist((s["x"], s["y"]), p) >= gap for p in placed.values())

    def pick(nid, target, fit, pool=None, gaps=(MIN_GAP, MIN_GAP_RELAXED), reaches=(REACH, REACH_RELAXED)):
        pool = pool or usable
        for gap in gaps:
            for reach in reaches:
                cands = [s for s in pool if fit in s["fits"] and free(s, gap) and math.dist((s["x"], s["y"]), target) <= reach]
                if cands:
                    return min(cands, key=lambda s: math.dist((s["x"], s["y"]), target))
        cands = [s for s in pool if free(s, MIN_GAP_RELAXED)]            # last resort: any free ground nearby
        return min(cands, key=lambda s: math.dist((s["x"], s["y"]), target)) if cands else None

    # 1-3: the main path, choosy places first
    for nid in sorted(main, key=lambda i: (CHOOSY.get(nodes[i]["type"], 5), SHAPE.get(i, (0.5, 0))[0])):
        t, u = SHAPE.get(nid, (0.5, 0.0))
        dt, du = jitter.get(nid, (0.0, 0.0))
        if nid not in ("r1_n01", "r1_n12"):   # the start and the boss stay at the two ends of the land
            t, u = t + dt, u + du
        (px, py), (nx, ny) = road_at(road, t)
        target = (px + nx * u * across * sign, py + ny * u * across * sign)
        fit = FIT_OF.get(nodes[nid]["type"], nodes[nid]["type"])
        s = pick(nid, target, fit, pool=mainland)
        s["taken"] = True
        placed[nid], spot_of[nid] = (s["x"], s["y"]), s

    # 4: side places go to the land the path left empty - the biggest areas first, nearest their parent
    areas = {a["id"]: a for a in plate["areas"]}
    def covered():
        return {spot_of[n]["area"] for n in spot_of}
    for nid in side:
        parent = (nodes[nid].get("unlock") or {}).get("value", [None])[0]
        pp = placed.get(parent, (768, 352))
        empty = [a for a in areas.values() if a["id"] not in covered()]
        def score(a):
            cx, cy = (a["box"][0] + a["box"][2]) / 2, (a["box"][1] + a["box"][3]) / 2
            return a.get("size", 1) * 160 - math.dist((cx, cy), pp)
        fit = FIT_OF.get(nodes[nid]["type"], nodes[nid]["type"])
        s = None
        for a in sorted(empty, key=score, reverse=True):
            cands = [sp for sp in usable if sp["area"] == a["id"] and fit in sp["fits"] and free(sp, MIN_GAP)]
            if cands:   # the spot in that area nearest the parent, so the side road is short and reads as a branch
                s = min(cands, key=lambda sp: math.dist((sp["x"], sp["y"]), pp))
                break
        if s is None:
            s = pick(nid, pp, fit)
        s["taken"] = True
        placed[nid], spot_of[nid] = (s["x"], s["y"]), s

    # 5: names from the ground
    adjs = list(PLACE_ADJ.get(strata, PLACE_ADJ["VERDANT"]))
    rnd.shuffle(adjs)
    used, places, used_nouns = set(), {}, set()
    for i, nid in enumerate(sorted(placed)):
        nouns = spot_of[nid]["nouns"]
        fresh = [n for n in nouns if n not in used_nouns] or nouns     # vary the nouns too: not three "Peak"s
        noun = rnd.choice(fresh)
        name = f"{adjs[i % len(adjs)]} {noun}"                        # every adjective once before any repeats
        if name in used:
            name = f"{adjs[(i + 5) % len(adjs)]} {noun}"
        used.add(name); used_nouns.add(noun)
        places[nid] = name
    return ({n: [int(p[0]), int(p[1])] for n, p in placed.items()}, places, {n: spot_of[n]["area"] for n in placed})


def report(positions, areas_of, region="region_01"):
    """The rules, checked: coverage of the land, spacing, keep-out, and how many roads cross."""
    plate, graph, _ = load(region)
    all_areas = [a["id"] for a in plate["areas"] if a.get("size", 1) >= 2]
    covered = set(areas_of.values())
    gaps = [math.dist(positions[a], positions[b]) for a in positions for b in positions if a < b]
    edges = [(n["id"], c) for n in graph["nodes"] for c in n.get("connects", [])]
    crossings = sum(1 for i, (a, b) in enumerate(edges) for (c, d) in edges[i + 1:]
                    if _segments_cross(tuple(positions[a]), tuple(positions[b]), tuple(positions[c]), tuple(positions[d])))
    xs = [p[0] for p in positions.values()]
    keep = sum(1 for p in positions.values() for k in plate.get("keep_out", []) if _in(k["box"], p[0], p[1]))
    long_edges = sum(max(0.0, math.dist(positions[a], positions[b]) - 300) for a, b in edges) / 10
    return dict(missing=[a for a in all_areas if a not in covered], min_gap=round(min(gaps)), crossings=crossings,
                span=(min(xs), max(xs)), keep_out_hits=keep, long_edges=round(long_edges))


def sheet(zones, out):
    from PIL import Image, ImageDraw
    _, graph, _ = load()
    conn = {n["id"]: n.get("connects", []) for n in graph["nodes"]}
    side = {n["id"] for n in graph["nodes"] if n.get("zone") == "side"}
    base = Image.open(ROOT / "client" / "content" / "art" / "map" / "map_plate.png").convert("RGB")
    tiles = []
    for cls, (pos, places) in zones.items():
        im = base.copy(); d = ImageDraw.Draw(im)
        for a, bs in conn.items():
            for b in bs:
                if a in pos and b in pos:
                    d.line([tuple(pos[a]), tuple(pos[b])], fill=(150, 210, 255) if b in side else (255, 230, 120), width=4)
        for n, (x, y) in pos.items():
            d.ellipse([x - 15, y - 15, x + 15, y + 15], fill=(80, 160, 255) if n in side else (220, 40, 40), outline=(255, 255, 255), width=3)
            d.text((x - 40, y + 18), places[n], fill=(255, 255, 255))
        d.text((20, 20), cls, fill=(255, 255, 0))
        tiles.append(im.resize((768, 352)))
    cols = 2
    rows = (len(tiles) + 1) // cols
    sh = Image.new("RGB", (768 * cols, 352 * rows))
    for i, t in enumerate(tiles):
        sh.paste(t, ((i % cols) * 768, (i // cols) * 352))
    sh.save(out, quality=85)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sheet", default="/tmp/map_layout_sheet.jpg")
    a = ap.parse_args()
    import importlib.util
    spec = importlib.util.spec_from_file_location("gsz", ROOT / "tools" / "gen_start_zones.py")
    g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
    zones = {}
    for ci, (cls, z) in enumerate(g.ZONES.items()):
        x0, mirror = z["layout"][0], z["layout"][5]
        pos, places, areas = layout(x0 > 768, mirror, z["strata"], seed=(ci + 1) * 77)
        r = report(pos, areas)
        print(f"{cls:12} span {r['span']}  min gap {r['min_gap']}  crossings {r['crossings']}  long {r['long_edges']}  keep-out {r['keep_out_hits']}  empty areas {r['missing'] or 'none'}")
        zones[cls] = (pos, places)
    sheet(zones, a.sheet)
    print("sheet:", a.sheet)


if __name__ == "__main__":
    main()
