# ARTIFACT_NAME_ALIASES.md — Old → New Display Name Mapping

This file maps **old multi-word display names** to **new one-word display names** for Artifacts in `launch_artifacts.json`. Rulings, design docs, and historical references that use the old names remain traceable through this index.

## Mapping

| Old Name | New Name | Artifact ID | Class | Slot Pool |
|---|---|---|---|---|
| Ancestral Blade | Sword | `artf_warrior_sword` | warrior | sword |
| Bulwark of the Line | Shield | `artf_warrior_shield` | warrior | shield |
| Warden's Focus | Wand | `artf_mage_wand` | mage | wand |
| Mantle of the Living Rune | Aura | `artf_mage_aura` | mage | aura |
| Whisperfang | *(unchanged)* | `artf_thief_dagger_whisper` | thief | dagger |
| Duskfang | *(unchanged)* | `artf_thief_dagger_dusk` | thief | dagger |
| Dawnlit Censer | Censer | `artf_cleric_censer` | cleric | censer |
| Icon of the Unbroken | Icon | `artf_cleric_icon` | cleric | icon |
| Heartwood Bow | Bow | `artf_ranger_bow` | ranger | bow |
| Quiver of Whispers | Quiver | `artf_ranger_quiver` | ranger | quiver |
| Grimoire of the Hollow Court | Skull | `artf_necromancer_grimoire` | necromancer | grimoire |
| Phylactery of the Pale King | Shard | `artf_necromancer_phylactery` | necromancer | phylactery |
| Forgehammer of the Deep Halls | Hammer | `artf_runesmith_hammer` | runesmith | hammer |
| Runic Anvil | Anvil | `artf_runesmith_anvil` | runesmith | anvil |

## Notes

- **Whisperfang** and **Duskfang** were already single-word names and are unchanged. They must remain distinguishable as the Thief class uses two Dagger-slot Artifacts.
- Artifact **IDs** (`artf_*`) and **slot pools** are unchanged — this is a display-name-only change.
- Design docs (`FIELD_EFFECT_SPEC.md`, `ARTIFACT_RULINGS.md`, `ARTIFACT_CLASSES.md`) and rulings (`R1`–`R26`) reference old names. Use this table for cross-referencing.