# WORLD_TERRAIN.md — the terrain rules (FABLE-105)

Trikzos, 2026-10-25: *"We need the coding of our game for our infinite expanding world to include rules about
the terrain that each map prompt shall end up generating (this should also get built into our LoRA model) —
one of the rules being: we should use the terrain generated in the art in part for naming the locations …
these could be Mountain Pass, or the Mountain Stronghold. The maps should have more dots, thoroughly but
accurately using the terrain."*

This is permanent code, not a one-off. One plan, three readers, one vocabulary.

## The rule in one sentence

**Every map is planned before it is painted; the painting, the places on it, and the names of those places
all come from that plan, in the same words.**

## The vocabulary — `content/world/terrain.json`

* **Features** are the things a painting shows: `mountain_range`, `forest`, `ancient_grove`, `river`, `lake`,
  `marsh`, `coast`, `ruins`, `stronghold`, `plain`, `canyon`, `volcano`, `mine`, `glacier`, `dunes`,
  `observatory`. Each has a `paint` phrase (what the painter is told), a width in grid cells, and **spots**.
* **Spots** are where a place can stand on a feature and what it is called there: a mountain range has a
  *pass* (Pass, Gap, Saddle, Notch, Gate), a *peak* (Peak, Spire, Crag, Horn), a *foothill*, a *cave*, a *hold*
  (Stronghold, Hold, Keep, Eyrie, Watch). Each spot lists which **kinds of place fit** it: a Warden or a boss
  holds a pass, a hold, a gate, a bridge, a headland; a Dig goes in a cave, a barrow, a ruin, a wreck; a
  Merchant waits at a ford, a landing, a crossroads, a market; an Elite takes the peak or the heart of the
  wood; Lore sits at standing stones, springs and ruins.
* **Biomes** say which features a page of theirs may show (`menu`), which it always shows (`always`), and the
  **keep** — the feature the page's guardian stands in, always in the east where the road ends.
* The **grid** is 3 x 3: north-west … south-east. Every sentence to the painter and every box in the plan uses
  these nine words, so "in the north-west, a jagged mountain range" means the same thing to the painter and
  to the code that puts the pass there.

Add a feature by adding it here. Nothing else needs to change for it to be painted, settled on and named.

## The plan — `engine/World/Terrain.cs`

`TerrainPlanner.Plan(rng, lexicon, biome…)` is a pure function of the page's seed:

1. pick 5–7 features from the biome's menu (never three of a kind), plus the keep;
2. give each a cell (wide features take a free neighbour to the east or south); the keep takes the east cell;
3. scatter spots through each feature's box — every spot kind at least once, then more in proportion to the
   feature's share of the canvas, about 1.7 spots per place the page will have, so **every place finds terrain**;
4. write the painter's prompt (`TerrainPrompt.Build`): trigger word, house style, the biome, then the features
   west to east, each "in the <cell>, <paint phrase>", the keep marked "the road's end", the road sentence.

`WorldGenerator.Generate` then **settles** the page's places on the plan (`SettleOnTerrain`): the choosy kinds
first (Warden/boss, Elite, Lore, events, then fights), each taking the nearest free spot that fits within
reach (x counted 1.5x so a column stays a column); it moves onto the spot and is **named for it** — a biome
adjective plus the spot's noun: *Thornlit Pass*, *Rootdeep Barrow*, *Gull Haven*. A place with no spot in
reach stays where it was and borrows the nearest feature's noun. The guardian keeps its own name and its post.
Every `Blip` now carries `Terrain` and `Spot`; every `WorldPage` carries its `TerrainPlan`.

The terrain uses its **own rng streams** (`"terrain"`, `"terrain-names"`), so nothing that discoveries hang
off changed: ids, kinds, roads and guardians are exactly what they were under generator version 1. Only x/y
and names moved — onto the terrain. (Test: `Page_TerrainChangesNothingThatDiscoveriesHangOff`.)

## More dots

World pages already carry 45–60 places. The terrain plan scales its spots with that count, so a denser page
simply gets more spots per feature. For authored campaign regions, `tools/region_gen.py`'s `encounter_slots`
is the dial; the rule for new regions is **14 or more** places including side places, each standing on an annotated feature (below). region_01 has 15: 11 on the path and 4 side fights.

## Painting a page — `tools/world_map_paint.py`

    python3 tools/world_map_paint.py --biome elvenwood --instance 0 --page 1

It asks the engine for the page (`Runewake.Sim world-page …`), sends the plan's prompt (+ negative prompt)
to the image pipeline with the Runewake map LoRA, and files the painting as
`client/content/art/world/<biome>_<instance>_<page>.webp` — the path `WorldMapScene` loads when it exists.
The page's places already stand where the prompt put the features, so the dots land on the terrain.
The first pages of every biome (instance 0) are the ones to paint and ship; deeper pages can be painted later
or served from the cloud without any code change. Nothing is installed until Trikzos picks the painting.

## Authored maps — the campaign plates (`tools/map_layout.py`)

Trikzos, on the first pass: *"you did what we did, but then didn't use the entire rest of the map."* Snapping an
old path onto the terrain is not enough; the layout itself has to come from the land. A hand-painted plate
(region_01's map, `client/content/art/map/map_plate.png`) gets its terrain **read off the painting once** into
`content/map/terrain/<region>.json`:

* **areas** — the parts of the land a player would name (the west shore, the spine, the south isle …), each with
  a size; every area of size 2+ must carry a place;
* **features** with **spots** in plate pixels, each feature in an area;
* the **road** — the way across the whole island, one shore to the other;
* **keep_out** — the title banner, the left button column and the node panel, where nothing may stand.

`tools/map_layout.py` lays every zone out by five rules, scoring a few hundred candidate layouts and keeping the
one that breaks the fewest:

1. **The main path crosses the whole land.** The start stands on one shore, the boss at the far end; the graph's
   upper lane rides one side of the road and the lower lane the other. (Zones that start in the east run it
   backwards; mirrored zones swap the lanes.) The main path stays on the mainland.
2. **Every place stands on a spot that fits it** (Warden → pass/hold/gate, Dig → cave/wreck, Merchant → cove/ford …).
3. **No crowding**: 118 px between places (92 at worst), nothing in the keep-out boxes.
4. **Every part of the land is visited**: the graph's **side places** (nodes marked `"zone": "side"`, optional
   fights that unlock from a main node) go to the areas the path left empty — the islands, the spit, the
   peninsula — biggest first, nearest their parent.
5. **Every place is named for its ground**: a stratum adjective (each used once per map) and the spot's noun.

Roads crossing, land left empty, crowding and over-long roads are the score; `python3 tools/map_layout.py`
prints each zone's report and writes a contact sheet. `tools/gen_start_zones.py` writes the result into
`content/start_zones.json` (positions + `places`) and the base region. The map labels every place by its ground
(*Wrack Pass*, *Sounding Haven*); tapping it names who waits there (*DUEL · WRACK PASS — Captain Orran*).
`StartZoneTests.Every_zone_map_uses_the_whole_island` holds the line. When a plate is repainted, redo its
terrain file from the new painting; the layouts follow.

## The LoRA

The model must learn that these words mean these shapes in these places. So **every training map is captioned
in this vocabulary**: `tools/style_lora.py caption` tells the captioner to name each terrain feature it sees
with the lexicon's feature words and give its 3x3 cell, in the same "in the north-west, a jagged mountain
range" form the prompts use. Training captions and generation prompts share one grammar; that is what makes
"in the east, a dark fortress" land in the east.

## What not to do

* Do not place a node by eye on a painting and name it after a character. The ground names the place; the foe
  is the foe.
* Do not add a terrain word anywhere but `terrain.json`. Prompt, placement and naming read the same file.
* Do not bump the atlas `version` for terrain changes — they do not move ids. Bump it only for a new world.
