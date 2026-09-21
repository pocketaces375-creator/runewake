# POCKETED_CLASSES.md — Future Release Content

The following classes and artifacts have been moved to `content/pocketed/` and `client/content/art/pocketed/` as part of TASK-ROSTER-LOCK-1. They are preserved for a future release — their data and art have not been deleted.

## Pocketed Classes

| Class | Former Stratum | Former Town |
|-------|---------------|-------------|
| Mage | Tide | Saltmere |
| Cleric | Dawn | Sunspire |
| Runesmith | Ember | Emberhold |
| Tidecaller | Tide | Saltmere |
| Dawnward | Dawn | Sunspire |
| Occultist | Hollow | Duskchapel |
| Thief (→ now Rogue) | Hollow | Duskchapel |
| Ranger (→ now Astrologist) | Verdant | Greyhollow |

## Pocketed Artifacts

| Artifact | Former Class |
|----------|-------------|
| artf_mage_wand | Mage |
| artf_mage_aura | Mage |
| artf_cleric_censer | Cleric |
| artf_cleric_icon | Cleric |
| artf_ranger_bow | Ranger |
| artf_ranger_quiver | Ranger |
| artf_necromancer_grimoire | Necromancer |
| artf_necromancer_phylactery | Necromancer |
| artf_necromancer_scythe | Necromancer |
| artf_runesmith_hammer | Runesmith |
| artf_runesmith_anvil | Runesmith |
| artf_druid_staff | Druid |
| artf_druid_totem | Druid |
| artf_paladin_anvil | Paladin |
| artf_thief_dagger_whisper | Thief |
| artf_thief_dagger_dusk | Thief |

## Status

All pocketed data is preserved in `content/pocketed/` and `client/content/art/pocketed/` with `"pocketed": true, "future_release": true` flags. These classes and artifacts are NOT loaded by the current game build. They will be re-integrated in a future release cycle.

## Proposed — not yet built (long-term)

Recorded 2026-09-21 from Trikzos, for classes to be built "a long time from now".
Unlike everything above, these have **no data and no art** — nothing exists in
`content/` or `client/content/art/` for them. This is the design note, verbatim,
plus the two things a future builder needs to know before starting.

| Class | Artifact 1 | Artifact 2 |
|-------|-----------|-----------|
| Blacksmith | Hammer | Forge |
| Alchemist | Vial strap | Seal |
| Traveling Merchant | Bag of magical wares | Price gouging |
| Chef | Chef knife | **— not yet named —** |

Every current class carries exactly two artifacts (one per slot pool —
`ArtifactRegistry.OpponentLoadout` and the tutorial's `{artifact_1}` /
`{artifact_2}` tokens both assume it), so **Chef needs a second one named
before it can be built.** Ask Trikzos; do not invent it.

**Overlaps to settle before building — flagged, not decided:**

- **Blacksmith / Hammer + Forge would be the third smithing kit.** The live
  Paladin already carries `artf_paladin_hammer`, `artf_paladin_wardens_hammer`
  and `artf_paladin_anvil`, and the pocketed Runesmith above is Hammer + Anvil.
  Three hammer classes will read as one. Decide how Blacksmith differs — or
  whether it absorbs Runesmith — before any art is commissioned.
- **Alchemist / Seal** clashes by name, not by id, with the mythic rune
  "Seal of Order" and the cards "Tidal Seal", "Sealing Light" and
  "Aelin's Seal". No code conflict, but a player reading "Seal" on the board
  will not know which one is meant. A more specific name is worth considering.

**Art note:** *Price gouging* is not an object, and neither is Battlemage's
*Aura*. `docs/ART_VARIETY_SYSTEM.md` renders every artifact tile as "the object
alone, empty background falling to black" — a non-physical artifact needs a depiction chosen for it
(a symbol, a scene, an effect) rather than going through the object prompt
as-is. Same goes for *Forge* at artifact-plate scale.
