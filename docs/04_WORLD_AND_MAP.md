# 04 — World, Map, and the Archaeology Layer v0.1

---

## 1. Premise

Something ended the world once already. The survivors did not write it down — they **buried** it, deliberately, in runestone caches sealed under five geological strata. Centuries later the seals are failing and the buried things are waking as creatures again. Delvers dig them up, bind them to rune-cards, and duel the regional Wardens who were placed to keep the caches shut.

The central mystery, revealed across regions: the Wardens are not villains. They are the descendants of the people who buried it. And the deeper you dig, the more the Codex suggests the burial was the correct decision.

This gives the archaeology undertone real teeth — the player's progression *is* the transgression. Every relic engraved with their name is also evidence.

## 2. Map structure

A **node graph**, not an open world. Final Fantasy Tactics / Mario World topology: nodes connected by edges, some gated, some optional, some hidden.

```
Region -> Zones -> Nodes
```

**Node types:**

| Type | Content |
|---|---|
| `DUEL` | Standard deck-wielder fight. Themed to the zone. |
| `ELITE` | Harder wielder with a modifier (e.g. "starts with a Guard token"). |
| `WARDEN` | Zone boss. First clear drops a Sigil rune. |
| `WARDEN_BOSS` | Region boss. First clear mints a **Lost Relic** card (see §4). |
| `DIG` | Excavation site. No duel — a short dig interaction. |
| `SHRINE` | Rest / deck edit / lore entry. |
| `CACHE` | Hidden node. Revealed only by a Codex clue, not by adjacency. |
| `MERCHANT` | Trade shards and duplicate cards. |

**Data shape:** the whole map is JSON (`content/map/region_01.json`) — nodes, edges, unlock conditions, encounter refs. The client renders from data. New regions ship as content packs with zero client code changes. This is what makes "expand the map for years" cheap.

```json
{
  "id": "r1_n07",
  "type": "ELITE",
  "position": [340, 512],
  "connects": ["r1_n06", "r1_n08", "r1_n12"],
  "unlock": { "op": "NODES_CLEARED", "value": ["r1_n06"] },
  "encounter": "r1_elite_ashkeeper",
  "rewards": ["shard:120", "fragment:ember:2", "dig_charge:1"]
}
```

## 3. Regions (v1)

| # | Region | Strata | Theme | Warden Boss |
|---|---|---|---|---|
| 1 | **The Fallow Reach** | VERDANT / DAWN | Farmland swallowed by a forest that grew in one night. Plow-lines still visible from the ridge. | **Warden Aelin, the Last Steward** |
| 2 | **Cinderhold** | EMBER / HOLLOW | A dwarf-scaled forge city that mined *downward into a seal*. | **Warden Bruk, Who Struck First** |
| 3 | **The Drowned Archive** | TIDE / HOLLOW | A library-city flooded on purpose, by its own librarians. | *(Region 3 boss — Season 1 finale, TBD)* |

Future regions (design space, not built): **The Glass Waste** (a battlefield fused to glass), **The Hanging Tombs**, **Sundermoor**, **The Ninth Seal**.

Each region carries **6–10 deck-wielders**, each with a legible archetype the player learns to counter with rune pages: the Root-Binder plays wide adjacency Verdant, the Ashkeeper plays Ember burn, the Silt-Reader plays Tide Excavate control, etc. Legibility matters more than variety here — the player should be able to say "this guy goes wide, I'll bring the sweeper page."

## 4. Excavation and the Lost Relic

**Dig Charges** are earned from duels (1 per node first-clear, 1 per 3 repeat wins). Spend one at a `DIG` node.

**The dig interaction** — deliberately short, 20–40 seconds, no minigame skill ceiling:
1. A grid of soil tiles over a hidden object silhouette.
2. You get N strikes (N set by the site, modified by tools you've found).
3. Each strike clears a tile and may reveal: rune fragments, shards, a Codex page, an **Unidentified Relic**, or nothing.
4. If you uncover enough of the silhouette before strikes run out, you get the site's headline find.

Tools (Brush, Iron Spade, Loadstone Rod, Seer's Lens) are permanent unlocks that change strike count, reveal radius, or highlight one true tile. They come from Elite nodes. This is a compact progression system that doesn't need much art.

**The Lost Relic — the retention hook**

When a player defeats a `WARDEN_BOSS` or a rare challenge encounter for the first time, the game **mints** a card:

```json
{
  "relic_instance_id": "uuid",
  "card_id": "relic_aelins_seal",
  "acquirer_name": "Trikzos",
  "acquired_at": "2026-08-03",
  "site": "The Fallow Reach — Steward's Barrow",
  "discovery_index": 41827,
  "engraving_style": "verdant_gold"
}
```

The card art frame renders a line across the bottom: *"Unearthed by Trikzos — Steward's Barrow, 3rd of Eighthmonth."* The `discovery_index` is the global count of players who have found it, so early finders carry a low number permanently. That number costs us nothing and is worth an enormous amount to a collector.

Rules:
- The engraving is **cosmetic and permanent**. It never affects card power. (Important — otherwise it becomes a fairness problem in PvP.)
- The ledger lives in Supabase (`relic_instances`), keyed to account, so it survives reinstalls and is the anchor for cross-device sync.
- Relics are also the natural place for later social features: a Museum screen, sharing a relic card image, "you were the 41,827th to find this."

## 5. The Codex (lore and mystery)

Every zone has a **Codex** of 8–14 entries: fragments of journals, inventory manifests, warding instructions, a child's drawing. They arrive from digs, first-clears, and hidden `CACHE` nodes.

Three functions, all load-bearing:
1. **Reward variety** — something to find that isn't a card.
2. **Puzzle gating** — some Codex entries contain a clue that reveals a hidden `CACHE` node ("count the pillars from the eastern arch"). The player solves it by *reading*, and taps the correct spot on the map. Very cheap to build, disproportionately memorable.
3. **The slow reveal** — the burial-was-correct twist lands only for players who read.

Codex entries are pure content JSON with an optional `reveals_node` field. The AI pipeline can draft them within a strict style guide, but Codex text is the one area worth heavy human editing — it's the voice of the game.

## 6. Trinkets

Small non-card collectibles from digs: a bent coin, a child's tooth-charm, a broken lens. Each gives a tiny passive on the *map* layer (+1 shard per duel, +1 strike at digs) and a paragraph of description. Cheap to generate, strong for completionists, and no balance risk because they never enter a duel.
