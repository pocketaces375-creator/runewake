# 02 — Card Database (complete, generated from repo content JSONs)

> Source of truth: `content/cards/*.json` and `content/artifacts/launch_artifacts.json` in the runewake repo,
> exported verbatim at commit ed6a40a (2026-08-31). If this file and the repo disagree, the repo wins.
> Cards carry no prose rules text — rules live in the ability DSL (docs/02_CARD_DSL.md defines ops). Each card below shows its full DSL verbatim plus flavor text.

## verdant.json (13 cards)

| Name | id | Type | Rarity | Cost | Atk | Vigor | Keywords | Set |
|---|---|---|---|---|---|---|---|---|
| Root Warden | `vrd_c_root_warden` | CREATURE | COMMON | 3 | 2 | 4 | GUARD | buried_age |
| Verdant Sproutling | `vrd_c_verdant_sproutling` | CREATURE | COMMON | 1 | 1 | 2 | — | buried_age |
| Thornbark Defender | `vrd_c_thornbark_defender` | CREATURE | COMMON | 4 | 2 | 6 | GUARD, FRAGILE | buried_age |
| Wildwood Stalker | `vrd_c_wildwood_stalker` | CREATURE | COMMON | 2 | 3 | 2 | — | buried_age |
| Grove Healer | `vrd_u_grove_healer` | CREATURE | UNCOMMON | 3 | 1 | 3 | — | buried_age |
| Canopy Archer | `vrd_u_canopy_archer` | CREATURE | UNCOMMON | 4 | 3 | 3 | REACH | buried_age |
| Elder Treant | `vrd_u_elder_treant` | CREATURE | UNCOMMON | 6 | 5 | 7 | GUARD, ROOTED | buried_age |
| Saphoof Charger | `vrd_u_saphoof_charger` | CREATURE | UNCOMMON | 5 | 5 | 4 | PIERCE | buried_age |
| Bloomweaver | `vrd_r_bloomweaver` | CREATURE | RARE | 4 | 1 | 4 | — | buried_age |
| Undergrowth Eruption | `vrd_r_undergrowth_eruption` | RITUAL | RARE | 3 | — | — | — | buried_age |
| Nature's Renewal | `vrd_r_natures_renewal` | RITUAL | RARE | 2 | — | — | — | buried_age |
| Heartwood Relic | `vrd_x_heartwood_relic` | RELIC | RELIC | 4 | — | — | SEALED | buried_age |
| Verdant Bud | `vrd_t_verdant_bud` | TOKEN | COMMON | 0 | 1 | 1 | SWIFT | buried_age |

### Root Warden  (`vrd_c_root_warden`)
- VERDANT CREATURE · rarity COMMON · cost 3 · attack 2 · vigor 4 · keywords: GUARD · power_score 7.1
- ABILITY `ON_SUMMON`:
  - `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL"}, "vigor": 1}`
- *"The grove keeps its own ledgers, and it does not forgive debts."*

### Verdant Sproutling  (`vrd_c_verdant_sproutling`)
- VERDANT CREATURE · rarity COMMON · cost 1 · attack 1 · vigor 2 · power_score 2.0
- *"Life finds a way through every crack."*

### Thornbark Defender  (`vrd_c_thornbark_defender`)
- VERDANT CREATURE · rarity COMMON · cost 4 · attack 2 · vigor 6 · keywords: GUARD, FRAGILE · power_score 6.8
- *"A wall of living thorns that withers when the battle ends."*

### Wildwood Stalker  (`vrd_c_wildwood_stalker`)
- VERDANT CREATURE · rarity COMMON · cost 2 · attack 3 · vigor 2 · power_score 3.5
- *"It hunts between the roots, patient and swift."*

### Grove Healer  (`vrd_u_grove_healer`)
- VERDANT CREATURE · rarity UNCOMMON · cost 3 · attack 1 · vigor 3 · power_score 5.2
- ABILITY `ON_SUMMON`:
  - `{"op": "HEAL", "target": {"scope": "ALLY_CREATURE", "filter": "ANY", "count": 1}, "amount": 3}`
- *"The touch of living bark closes wounds in moments."*

### Canopy Archer  (`vrd_u_canopy_archer`)
- VERDANT CREATURE · rarity UNCOMMON · cost 4 · attack 3 · vigor 3 · keywords: REACH · power_score 5.5
- *"She waits in the high branches, arrow nocked."*

### Elder Treant  (`vrd_u_elder_treant`)
- VERDANT CREATURE · rarity UNCOMMON · cost 6 · attack 5 · vigor 7 · keywords: GUARD, ROOTED · power_score 9.5
- *"Older than the barrow, rooted before the first stone was laid."*

### Saphoof Charger  (`vrd_u_saphoof_charger`)
- VERDANT CREATURE · rarity UNCOMMON · cost 5 · attack 5 · vigor 4 · keywords: PIERCE · power_score 7.8
- *"When the great beast runs, the ground remembers."*

### Bloomweaver  (`vrd_r_bloomweaver`)
- VERDANT CREATURE · rarity RARE · cost 4 · attack 1 · vigor 4 · power_score 8.5
- ABILITY `ON_TURN_START`:
  - `{"op": "SUMMON", "target": {"scope": "PLAYER_SELF"}, "token_id": "vrd_t_verdant_bud"}`
- *"Each morning, a new bloom unfolds in her footsteps."*

### Undergrowth Eruption  (`vrd_r_undergrowth_eruption`)
- VERDANT RITUAL · rarity RARE · cost 3 · power_score 6.5
- ABILITY `RESOLVE`:
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": "ALL"}, "amount": 2}`
- *"The soil remembers the blood it has drunk."*

### Nature's Renewal  (`vrd_r_natures_renewal`)
- VERDANT RITUAL · rarity RARE · cost 2 · power_score 5.0
- ABILITY `RESOLVE`:
  - `{"op": "HEAL", "target": {"scope": "ALLY_CREATURE", "filter": "ANY", "count": "ALL"}, "amount": 2}`
- *"Spring comes even to the deepest shadows."*

### Heartwood Relic  (`vrd_x_heartwood_relic`)
- VERDANT RELIC · rarity RELIC · cost 4 · keywords: SEALED · power_score 11.0
- ABILITY `PASSIVE`:
  - `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ANY", "count": "ALL"}, "vigor": 1}`
- *"A splinter of the First Tree, still warm with life."*

### Verdant Bud  (`vrd_t_verdant_bud`)
- VERDANT TOKEN · rarity COMMON · cost 0 · attack 1 · vigor 1 · keywords: SWIFT

## ember.json (12 cards)

| Name | id | Type | Rarity | Cost | Atk | Vigor | Keywords | Set |
|---|---|---|---|---|---|---|---|---|
| Cinder Runner | `emb_c_cinder_runner` | CREATURE | COMMON | 2 | 3 | 1 | SWIFT | buried_age |
| Ember Hound | `emb_c_ember_hound` | CREATURE | COMMON | 1 | 2 | 1 | SWIFT | buried_age |
| Flame Javelin | `emb_c_flame_javelin` | RITUAL | COMMON | 1 | — | — | — | buried_age |
| Forgeguard Berserker | `emb_c_forgeguard_berserker` | CREATURE | COMMON | 3 | 4 | 3 | — | buried_age |
| Wildfire Adept | `emb_u_wildfire_adept` | CREATURE | UNCOMMON | 2 | 2 | 2 | — | buried_age |
| Lava Serpent | `emb_u_lava_serpent` | CREATURE | UNCOMMON | 5 | 5 | 3 | PIERCE, FRAGILE | buried_age |
| Searing Blast | `emb_u_searing_blast` | RITUAL | UNCOMMON | 3 | — | — | — | buried_age |
| Cinderstorm Elemental | `emb_u_cinderstorm_elemental` | CREATURE | UNCOMMON | 4 | 4 | 4 | — | buried_age |
| Magma Forger | `emb_r_magma_forger` | CREATURE | RARE | 3 | 2 | 3 | — | buried_age |
| Inferno Burst | `emb_r_inferno_burst` | RITUAL | RARE | 5 | — | — | — | buried_age |
| Phoenix Ash | `emb_r_phoenix_ash` | CREATURE | RARE | 6 | 4 | 4 | UNEARTH, ECHO | buried_age |
| The Last Ember | `emb_x_the_last_ember` | RELIC | RELIC | 3 | — | — | SEALED | buried_age |

### Cinder Runner  (`emb_c_cinder_runner`)
- EMBER CREATURE · rarity COMMON · cost 2 · attack 3 · vigor 1 · keywords: SWIFT · power_score 4.85
- *"Forge-children learned to run before they learned to breathe."*

### Ember Hound  (`emb_c_ember_hound`)
- EMBER CREATURE · rarity COMMON · cost 1 · attack 2 · vigor 1 · keywords: SWIFT · power_score 3.2
- *"Pack-forged and flame-tempered."*

### Flame Javelin  (`emb_c_flame_javelin`)
- EMBER RITUAL · rarity COMMON · cost 1 · power_score 3.0
- ABILITY `RESOLVE`:
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": 1}, "amount": 2}`
- *"A spear of fire, thrown from the heart of the forge."*

### Forgeguard Berserker  (`emb_c_forgeguard_berserker`)
- EMBER CREATURE · rarity COMMON · cost 3 · attack 4 · vigor 3 · power_score 5.5
- *"He fights like the fire — without mercy, without memory."*

### Wildfire Adept  (`emb_u_wildfire_adept`)
- EMBER CREATURE · rarity UNCOMMON · cost 2 · attack 2 · vigor 2 · power_score 5.0
- ABILITY `ON_CAST_RITUAL`:
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": 1}, "amount": 1}`
- *"Every spell she casts fans the flames higher."*

### Lava Serpent  (`emb_u_lava_serpent`)
- EMBER CREATURE · rarity UNCOMMON · cost 5 · attack 5 · vigor 3 · keywords: PIERCE, FRAGILE · power_score 7.2
- *"Molten blood and a temper to match."*

### Searing Blast  (`emb_u_searing_blast`)
- EMBER RITUAL · rarity UNCOMMON · cost 3 · power_score 5.5
- ABILITY `RESOLVE`:
  - `{"op": "DAMAGE", "target": {"scope": "PLAYER_ENEMY"}, "amount": 4}`
- *"The heat of a dying star, focused to a point."*

### Cinderstorm Elemental  (`emb_u_cinderstorm_elemental`)
- EMBER CREATURE · rarity UNCOMMON · cost 4 · attack 4 · vigor 4 · power_score 7.8
- ABILITY `ON_DEATH`:
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": "ALL"}, "amount": 2}`
- *"Even in dying, it burns."*

### Magma Forger  (`emb_r_magma_forger`)
- EMBER CREATURE · rarity RARE · cost 3 · attack 2 · vigor 3 · power_score 7.5
- ABILITY `ON_SUMMON`:
  - `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ANY", "count": "ALL"}, "attack": 1}`
- *"He hammers strength into every ally's blade."*

### Inferno Burst  (`emb_r_inferno_burst`)
- EMBER RITUAL · rarity RARE · cost 5 · power_score 8.5
- ABILITY `RESOLVE`:
  - `{"op": "DAMAGE", "target": {"scope": "PLAYER_ENEMY"}, "amount": 5}`
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": "ALL"}, "amount": 1}`
- *"The forge-gods demand sacrifice."*

### Phoenix Ash  (`emb_r_phoenix_ash`)
- EMBER CREATURE · rarity RARE · cost 6 · attack 4 · vigor 4 · keywords: UNEARTH, ECHO · power_score 9.5
- *"From ash, she rises. From ash, she burns again."*

### The Last Ember  (`emb_x_the_last_ember`)
- EMBER RELIC · rarity RELIC · cost 3 · keywords: SEALED · power_score 9.0
- ABILITY `ON_TURN_START`:
  - `{"op": "DAMAGE", "target": {"scope": "PLAYER_ENEMY"}, "amount": 1}`
- *"The last spark of a world that burned too brightly."*

## tide.json (12 cards)

| Name | id | Type | Rarity | Cost | Atk | Vigor | Keywords | Set |
|---|---|---|---|---|---|---|---|---|
| Silt Reader | `tid_c_silt_reader` | CREATURE | UNCOMMON | 4 | 2 | 5 | — | buried_age |
| Tidal Scholar | `tid_c_tidal_scholar` | CREATURE | COMMON | 2 | 1 | 3 | — | buried_age |
| Deep One | `tid_c_deep_one` | CREATURE | COMMON | 3 | 3 | 3 | — | buried_age |
| Abyssal Gaze | `tid_c_abyssal_gaze` | RITUAL | COMMON | 1 | — | — | — | buried_age |
| Brine Witch | `tid_u_brine_witch` | CREATURE | UNCOMMON | 4 | 3 | 3 | — | buried_age |
| Coral Guardian | `tid_u_coral_guardian` | CREATURE | UNCOMMON | 5 | 3 | 6 | GUARD | buried_age |
| Memory Tides | `tid_u_memory_tides` | RITUAL | UNCOMMON | 2 | — | — | — | buried_age |
| Whirlpool Elemental | `tid_c_whirlpool_elemental` | CREATURE | COMMON | 3 | 2 | 4 | — | buried_age |
| Hydrokinetic Adept | `tid_r_hydrokinetic_adept` | CREATURE | RARE | 3 | 2 | 3 | — | buried_age |
| Flood of Secrets | `tid_r_flood_of_secrets` | RITUAL | RARE | 4 | — | — | — | buried_age |
| Sunken Leviathan | `tid_r_sunken_leviathan` | CREATURE | RARE | 7 | 7 | 7 | — | buried_age |
| Tidal Seal | `tid_x_tidal_seal` | RELIC | RELIC | 5 | — | — | SEALED | buried_age |

### Silt Reader  (`tid_c_silt_reader`)
- TIDE CREATURE · rarity UNCOMMON · cost 4 · attack 2 · vigor 5 · power_score 10.4
- ABILITY `ON_SUMMON`:
  - `{"op": "EXCAVATE", "target": {"scope": "PLAYER_SELF"}, "amount": 3}`
- ABILITY `ON_TURN_START` IF {"op": "BARROW_COUNT_GTE", "value": 4}:
  - `{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- *"She read the riverbed the way her mother read faces."*

### Tidal Scholar  (`tid_c_tidal_scholar`)
- TIDE CREATURE · rarity COMMON · cost 2 · attack 1 · vigor 3 · power_score 4.5
- ABILITY `ON_SUMMON`:
  - `{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- *"Knowledge flows like water — endlessly, unstoppably."*

### Deep One  (`tid_c_deep_one`)
- TIDE CREATURE · rarity COMMON · cost 3 · attack 3 · vigor 3 · power_score 4.5
- *"From the abyss it rises, silent and patient."*

### Abyssal Gaze  (`tid_c_abyssal_gaze`)
- TIDE RITUAL · rarity COMMON · cost 1 · power_score 2.0
- ABILITY `RESOLVE`:
  - `{"op": "EXCAVATE", "target": {"scope": "PLAYER_SELF"}, "amount": 2}`
- *"The depths see you as clearly as you see them."*

### Brine Witch  (`tid_u_brine_witch`)
- TIDE CREATURE · rarity UNCOMMON · cost 4 · attack 3 · vigor 3 · power_score 5.5
- ABILITY `ON_SUMMON`:
  - `{"op": "BURY", "target": {"scope": "PLAYER_SELF"}, "amount": 2}`
- *"Salt and spell, wrought together."*

### Coral Guardian  (`tid_u_coral_guardian`)
- TIDE CREATURE · rarity UNCOMMON · cost 5 · attack 3 · vigor 6 · keywords: GUARD · power_score 7.5
- *"A living reef that remembers every ship that passed."*

### Memory Tides  (`tid_u_memory_tides`)
- TIDE RITUAL · rarity UNCOMMON · cost 2 · power_score 4.0
- ABILITY `RESOLVE`:
  - `{"op": "EXCAVATE", "target": {"scope": "PLAYER_SELF"}, "amount": 2}`
  - `{"op": "DISCARD", "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- *"The tide brings, and the tide takes away."*

### Whirlpool Elemental  (`tid_c_whirlpool_elemental`)
- TIDE CREATURE · rarity COMMON · cost 3 · attack 2 · vigor 4 · power_score 5.0
- ABILITY `ON_DEATH`:
  - `{"op": "BOUNCE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": 1}, "amount": 1}`
- *"It unravels into foam, dragging you down with it."*

### Hydrokinetic Adept  (`tid_r_hydrokinetic_adept`)
- TIDE CREATURE · rarity RARE · cost 3 · attack 2 · vigor 3 · power_score 7.0
- ABILITY `ON_ALLY_DEATH`:
  - `{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- *"Every drop that falls tells her a story."*

### Flood of Secrets  (`tid_r_flood_of_secrets`)
- TIDE RITUAL · rarity RARE · cost 4 · power_score 6.5
- ABILITY `RESOLVE`:
  - `{"op": "DISCARD", "target": {"scope": "PLAYER_ENEMY"}, "amount": 2}`
- *"The tide washes away all hidden things."*

### Sunken Leviathan  (`tid_r_sunken_leviathan`)
- TIDE CREATURE · rarity RARE · cost 7 · attack 7 · vigor 7 · power_score 10.5
- *"It sleeps in the deep, dreaming of cities it swallowed."*

### Tidal Seal  (`tid_x_tidal_seal`)
- TIDE RELIC · rarity RELIC · cost 5 · keywords: SEALED · power_score 12.5
- ABILITY `PASSIVE`:
  - `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ANY", "count": "ALL"}, "vigor": 2}`
- *"Bound with the seal of the deep, holding back the flood."*

## hollow.json (12 cards)

| Name | id | Type | Rarity | Cost | Atk | Vigor | Keywords | Set |
|---|---|---|---|---|---|---|---|---|
| Gravewrit Thrall | `hol_c_gravewrit_thrall` | CREATURE | UNCOMMON | 3 | 4 | 2 | UNEARTH | buried_age |
| Skeletal Reaver | `hol_c_skeletal_reaver` | CREATURE | COMMON | 1 | 2 | 1 | — | buried_age |
| Deathspeaker | `hol_c_deathspeaker` | CREATURE | COMMON | 3 | 2 | 3 | — | buried_age |
| Bone Shard Volley | `hol_c_bone_shard_volley` | RITUAL | COMMON | 2 | — | — | — | buried_age |
| Crypt Crawler | `hol_u_crypt_crawler` | CREATURE | UNCOMMON | 4 | 4 | 3 | — | buried_age |
| Soul Harvest | `hol_u_soul_harvest` | RITUAL | UNCOMMON | 1 | — | — | — | buried_age |
| Barrow Revenant | `hol_u_barrow_revenant` | CREATURE | UNCOMMON | 5 | 5 | 5 | UNEARTH | buried_age |
| Ossuary Guard | `hol_c_ossuary_guard` | CREATURE | COMMON | 2 | 1 | 4 | GUARD | buried_age |
| Wraith Stalker | `hol_r_wraith_stalker` | CREATURE | RARE | 4 | 3 | 2 | VENOM, UNEARTH | buried_age |
| Curse of Binding | `hol_r_curse_of_binding` | RITUAL | RARE | 3 | — | — | — | buried_age |
| Hollow Herald | `hol_r_hollow_herald` | CREATURE | RARE | 6 | 5 | 6 | — | buried_age |
| The Black Barrow | `hol_x_the_black_barrow` | RELIC | RELIC | 4 | — | — | SEALED | buried_age |

### Gravewrit Thrall  (`hol_c_gravewrit_thrall`)
- HOLLOW CREATURE · rarity UNCOMMON · cost 3 · attack 4 · vigor 2 · keywords: UNEARTH · power_score 8.2
- ABILITY `ON_DEATH`:
  - `{"op": "DAMAGE", "target": {"scope": "PLAYER_ENEMY"}, "amount": 1}`
  - `{"op": "BURY", "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- *"Its name was scraped off the stone. It came anyway."*

### Skeletal Reaver  (`hol_c_skeletal_reaver`)
- HOLLOW CREATURE · rarity COMMON · cost 1 · attack 2 · vigor 1 · power_score 2.2
- *"Bones that remember the sword."*

### Deathspeaker  (`hol_c_deathspeaker`)
- HOLLOW CREATURE · rarity COMMON · cost 3 · attack 2 · vigor 3 · power_score 5.0
- ABILITY `ON_TURN_END`:
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "DAMAGED", "count": "ALL"}, "amount": 1}`
- *"He whispers to the wounded, promising the quiet."*

### Bone Shard Volley  (`hol_c_bone_shard_volley`)
- HOLLOW RITUAL · rarity COMMON · cost 2 · power_score 3.5
- ABILITY `RESOLVE`:
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": 1}, "amount": 3}`
- *"The dead do not miss."*

### Crypt Crawler  (`hol_u_crypt_crawler`)
- HOLLOW CREATURE · rarity UNCOMMON · cost 4 · attack 4 · vigor 3 · power_score 6.5
- ABILITY `ON_DEATH`:
  - `{"op": "EXCAVATE", "target": {"scope": "PLAYER_SELF"}, "amount": 2}`
- *"It drags itself from the dark, clutching forgotten things."*

### Soul Harvest  (`hol_u_soul_harvest`)
- HOLLOW RITUAL · rarity UNCOMMON · cost 1 · power_score 4.5
- ABILITY `RESOLVE`:
  - `{"op": "DESTROY", "target": {"scope": "ALLY_CREATURE", "filter": "EXHAUSTED", "count": 1}}`
  - `{"op": "ATTUNE", "target": {"scope": "PLAYER_SELF"}, "amount": 3}`
- *"The barrow gives. And takes."*

### Barrow Revenant  (`hol_u_barrow_revenant`)
- HOLLOW CREATURE · rarity UNCOMMON · cost 5 · attack 5 · vigor 5 · keywords: UNEARTH · power_score 8.5
- *"It rose when the barrow was unsealed. It will not go back."*

### Ossuary Guard  (`hol_c_ossuary_guard`)
- HOLLOW CREATURE · rarity COMMON · cost 2 · attack 1 · vigor 4 · keywords: GUARD · power_score 4.0
- *"Bone-walls remember every siege."*

### Wraith Stalker  (`hol_r_wraith_stalker`)
- HOLLOW CREATURE · rarity RARE · cost 4 · attack 3 · vigor 2 · keywords: VENOM, UNEARTH · power_score 7.5
- *"One touch is all it needs."*

### Curse of Binding  (`hol_r_curse_of_binding`)
- HOLLOW RITUAL · rarity RARE · cost 3 · power_score 6.5
- ABILITY `RESOLVE`:
  - `{"op": "SILENCE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": 1}}`
  - `{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": 1}, "amount": 2}`
- *"Words that bind the soul and break the will."*

### Hollow Herald  (`hol_r_hollow_herald`)
- HOLLOW CREATURE · rarity RARE · cost 6 · attack 5 · vigor 6 · power_score 10.0
- ABILITY `ON_SUMMON`:
  - `{"op": "UNBURY", "target": {"scope": "PLAYER_SELF"}, "amount": 2}`
- *"Her voice echoes from the barrow, calling the buried home."*

### The Black Barrow  (`hol_x_the_black_barrow`)
- HOLLOW RELIC · rarity RELIC · cost 4 · keywords: SEALED · power_score 10.0
- ABILITY `PASSIVE`:
  - `{"op": "DEBUFF", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": "ALL"}, "attack": -1}`
- *"The barrow's hunger reaches beyond the grave."*

## dawn.json (12 cards)

| Name | id | Type | Rarity | Cost | Atk | Vigor | Keywords | Set |
|---|---|---|---|---|---|---|---|---|
| Sealing Light | `dwn_r_sealing_light` | RITUAL | COMMON | 2 | — | — | — | buried_age |
| Dawn Warder | `dwn_c_dawn_warder` | CREATURE | COMMON | 2 | 1 | 3 | GUARD | buried_age |
| Sunblade Recruit | `dwn_c_sunblade_recruit` | CREATURE | COMMON | 3 | 3 | 3 | — | buried_age |
| Golden Retainer | `dwn_c_golden_retainer` | CREATURE | COMMON | 4 | 3 | 4 | — | buried_age |
| Purifying Light | `dwn_u_purifying_light` | RITUAL | UNCOMMON | 1 | — | — | — | buried_age |
| Morning Herald | `dwn_u_morning_herald` | CREATURE | UNCOMMON | 3 | 2 | 4 | — | buried_age |
| Steadfast Bulwark | `dwn_u_steadfast_bulwark` | CREATURE | UNCOMMON | 5 | 3 | 8 | GUARD | buried_age |
| Dawnbreaker Charger | `dwn_c_dawnbreaker_charger` | CREATURE | COMMON | 4 | 4 | 3 | SWIFT | buried_age |
| Radiant Prophet | `dwn_r_radiant_prophet` | CREATURE | RARE | 4 | 3 | 3 | — | buried_age |
| Holy Edict | `dwn_r_holy_edict` | RITUAL | RARE | 4 | — | — | — | buried_age |
| Archangel of Order | `dwn_r_archangel_of_order` | CREATURE | RARE | 7 | 6 | 6 | WARD, GUARD | buried_age |
| Dawn Relic | `dwn_x_dawn_relic` | RELIC | RELIC | 6 | — | — | SEALED | buried_age |

### Sealing Light  (`dwn_r_sealing_light`)
- DAWN RITUAL · rarity COMMON · cost 2 · power_score 5.6
- ABILITY `RESOLVE`:
  - `{"op": "GRANT_KEY", "target": {"scope": "ALLY_CREATURE", "filter": "CHOSEN", "count": 1}, "keyword": "WARD"}`
  - `{"op": "HEAL", "target": {"scope": "ALLY_CREATURE", "filter": "CHOSEN", "count": 1}, "amount": 2}`
- *"The wardens did not build doors. They built reasons not to open them."*

### Dawn Warder  (`dwn_c_dawn_warder`)
- DAWN CREATURE · rarity COMMON · cost 2 · attack 1 · vigor 3 · keywords: GUARD · power_score 3.5
- *"The first light holds the line."*

### Sunblade Recruit  (`dwn_c_sunblade_recruit`)
- DAWN CREATURE · rarity COMMON · cost 3 · attack 3 · vigor 3 · power_score 4.5
- *"Steel and sunlight, sworn to the covenant."*

### Golden Retainer  (`dwn_c_golden_retainer`)
- DAWN CREATURE · rarity COMMON · cost 4 · attack 3 · vigor 4 · power_score 7.0
- ABILITY `ON_SUMMON`:
  - `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL"}, "attack": 1, "vigor": 1}`
- *"Gold and duty, inseparable in service."*

### Purifying Light  (`dwn_u_purifying_light`)
- DAWN RITUAL · rarity UNCOMMON · cost 1 · power_score 3.0
- ABILITY `RESOLVE`:
  - `{"op": "SILENCE", "target": {"scope": "ENEMY_CREATURE", "filter": "ANY", "count": 1}}`
- *"Light purges corruption."*

### Morning Herald  (`dwn_u_morning_herald`)
- DAWN CREATURE · rarity UNCOMMON · cost 3 · attack 2 · vigor 4 · power_score 5.5
- ABILITY `ON_TURN_START`:
  - `{"op": "HEAL", "target": {"scope": "ALLY_CREATURE", "filter": "DAMAGED", "count": 1}, "amount": 2}`
- *"Each dawn brings the promise of renewal."*

### Steadfast Bulwark  (`dwn_u_steadfast_bulwark`)
- DAWN CREATURE · rarity UNCOMMON · cost 5 · attack 3 · vigor 8 · keywords: GUARD · power_score 8.5
- *"It stands. It always stands."*

### Dawnbreaker Charger  (`dwn_c_dawnbreaker_charger`)
- DAWN CREATURE · rarity COMMON · cost 4 · attack 4 · vigor 3 · keywords: SWIFT · power_score 6.0
- *"Light strikes first."*

### Radiant Prophet  (`dwn_r_radiant_prophet`)
- DAWN CREATURE · rarity RARE · cost 4 · attack 3 · vigor 3 · power_score 8.5
- ABILITY `ON_SUMMON`:
  - `{"op": "EXCAVATE", "target": {"scope": "PLAYER_SELF"}, "amount": 2}`
  - `{"op": "GAIN_VIGOR", "target": {"scope": "PLAYER_SELF"}, "amount": 2}`
- *"She sees what lies buried and strengthens those who seek it."*

### Holy Edict  (`dwn_r_holy_edict`)
- DAWN RITUAL · rarity RARE · cost 4 · power_score 6.0
- ABILITY `RESOLVE`:
  - `{"op": "DESTROY", "target": {"scope": "ENEMY_CREATURE", "filter": "DAMAGED", "count": 1}}`
- *"Judgment is passed. Sentence is executed."*

### Archangel of Order  (`dwn_r_archangel_of_order`)
- DAWN CREATURE · rarity RARE · cost 7 · attack 6 · vigor 6 · keywords: WARD, GUARD · power_score 11.5
- *"She does not fight — she enforces."*

### Dawn Relic  (`dwn_x_dawn_relic`)
- DAWN RELIC · rarity RELIC · cost 6 · keywords: SEALED · power_score 14.0
- ABILITY `PASSIVE`:
  - `{"op": "GRANT_KEY", "target": {"scope": "ALLY_CREATURE", "filter": "ANY", "count": "ALL"}, "keyword": "WARD"}`
- *"A fragment of the First Dawn, imbued with unbreakable light."*

## tutorial_pack.json (4 cards)

| Name | id | Type | Rarity | Cost | Atk | Vigor | Keywords | Set |
|---|---|---|---|---|---|---|---|---|
| Student of Embers | `tut_c_student_of_embers` | CREATURE | COMMON | 1 | 2 | 1 | — | buried_age |
| Verdant Initiate | `tut_c_verdant_initiate` | CREATURE | COMMON | 2 | 2 | 3 | — | buried_age |
| Iron Apprentice | `tut_c_iron_apprentice` | CREATURE | COMMON | 3 | 3 | 3 | — | buried_age |
| Thorn Sprout | `tut_opponent_token` | CREATURE | COMMON | 1 | 1 | 1 | — | buried_age |

### Student of Embers  (`tut_c_student_of_embers`)
- VERDANT CREATURE · rarity COMMON · cost 1 · attack 2 · vigor 1
- *"A simple creature of fire."*

### Verdant Initiate  (`tut_c_verdant_initiate`)
- VERDANT CREATURE · rarity COMMON · cost 2 · attack 2 · vigor 3
- *"A sturdy forest follower."*

### Iron Apprentice  (`tut_c_iron_apprentice`)
- DAWN CREATURE · rarity COMMON · cost 3 · attack 3 · vigor 3
- *"A slow but steady fighter."*

### Thorn Sprout  (`tut_opponent_token`)
- VERDANT CREATURE · rarity COMMON · cost 1 · attack 1 · vigor 1
- *"A knot of briars that moves when the wind passes through it."*

## Artifacts — launch_artifacts.json (14 artifacts, 7 classes x 2)

Display names were shortened to one word by TASK-NAMES (aliases in docs/ARTIFACT_NAME_ALIASES.md). Full design text: FIELD_EFFECT_SPEC.md §5 + ARTIFACT_CLASSES.md §§4–7; timing rulings R1–R26 in ARTIFACT_RULINGS.md.

### Sword  (`artf_warrior_sword`) — class warrior, slot pool sword
- PASSIVE: `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ATTACKING", "count": "ALL"}, "attack": 1, "vigor": 0, "duration": "WHILE_ATTACKING"}`
- TRIGGER: `{"trigger": "ON_CREATURE_ATTACKS", "condition": {"op": "ATTACKERS_THIS_TURN_GTE", "value": 3}, "effects": [{"op": "GRANT_KEY", "target": {"scope": "PLAYER_SELF"}, "keyword": "ANCESTRAL_SHIELD", "duration": "UNTIL_YOUR_NEXT_TURN", "uses": 1}]}`
- other fields: `{"flavor": "Tempered in the fires of a thousand charges."}`

### Shield  (`artf_warrior_shield`) — class warrior, slot pool shield
- PASSIVE: `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "HAS_NOT_ATTACKED", "count": "ALL"}, "attack": 0, "vigor": 1, "applied_at": "ON_TURN_END", "duration": "UNTIL_YOUR_NEXT_TURN"}`
- TRIGGER: `{"trigger": "ON_ALLY_ATTACKED", "condition": {"op": "NO_ATTACKERS_LAST_TURN"}, "effects": [{"op": "PREVENT_DAMAGE", "target": {"scope": "ALLY_CREATURE", "filter": "FIRST_ATTACKED", "count": 1}, "amount": 2, "frequency": "ONCE_PER_ENEMY_TURN"}]}`
- other fields: `{"flavor": "The line holds. The line always holds."}`

### Wand  (`artf_mage_wand`) — class mage, slot pool wand
- PASSIVE: `{"op": "COST_MOD", "applies_to": "SPELL", "filter": "FIRST_SPELL_EACH_TURN", "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- TRIGGER: `{"trigger": "ON_SPELL_CAST", "condition": {"op": "SPELLS_CAST_THIS_TURN_EQ", "value": 2}, "effects": [{"op": "ADD_CHARGE", "target": {"scope": "PLAYER_SELF"}, "amount": 1}]}`
- CHARGES: `{"max": 3, "gain_on": "on_spell_second", "spend_on": "on_spell_targets_creature", "spend_note": "auto-spend ALL on next spell with >=1 creature target; +1 damage OR +1 healing per charge, FIRST creature target only; buff spells do not spend (R4)"}`
- other fields: `{"flavor": "The warden's eye never blinks."}`

### Aura  (`artf_mage_aura`) — class mage, slot pool aura
- PASSIVE: `{"op": "PREVENT_DAMAGE", "target": {"scope": "PLAYER_SELF"}, "amount": 1, "filter": "FIRST_ATTACK_EACH_TURN", "source": "ATTACK"}`
- TRIGGER: `{"trigger": "ON_CREATURE_ATTACKS", "condition": {"op": "ENEMY_ATTACKS_YOUR_CHARACTER"}, "effects": [{"op": "COST_MOD", "applies_to": "SPELL", "target": {"scope": "PLAYER_SELF"}, "amount": 1, "duration": "THIS_TURN", "stacks": true}]}`
- other fields: `{"flavor": "The rune breathes. So does the mage."}`

### Whisperfang  (`artf_rogue_dagger_whisper`) — class rogue, slot pool dagger
- PASSIVE: `{"op": "GRANT_KEY", "target": {"scope": "ALLY_CREATURE", "filter": "FIRST_ATTACKER", "count": 1}, "keyword": "STEALTH_STRIKE"}`
- TRIGGER: `{"trigger": "ON_TURN_END", "condition": {"op": "ATTACKERS_THIS_TURN_EQ", "value": 1}, "effects": [{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}]}`
- other fields: `{"flavor": "Strike once. Vanish. Collect."}`

### Duskfang  (`artf_rogue_dagger_dusk`) — class rogue, slot pool dagger
- PASSIVE: `{"op": "COST_MOD", "applies_to": "CREATURE", "filter": "ATTACK_LTE", "value": 2, "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- TRIGGER: `{"trigger": "ON_CHARGE_FULL", "condition": null, "effects": [{"op": "SUPPRESS", "target": {"scope": "PLAYER_ENEMY", "slots": "ALL"}, "turns": 1}, {"op": "RESET_CHARGES", "target": {"scope": "SELF_ARTIFACT"}}]}`
- CHARGES: `{"max": 3, "gain_on": "on_creature_deals_damage_to_character", "max_per_creature_per_turn": 1, "spend_on": "on_charge_full"}`
- other fields: `{"flavor": "The night always collects."}`

### Censer  (`artf_cleric_censer`) — class cleric, slot pool censer
- PASSIVE: `{"op": "HEAL", "cadence": "ON_TURN_START", "target": {"scope": "ALLY_CREATURE", "filter": "MOST_WOUNDED", "count": 1, "tiebreak": "OWNER_CHOOSES"}, "amount": 1}`
- TRIGGER: `{"trigger": "ON_CHARGE_FULL", "timing": "END_OF_TURN", "condition": null, "effects": [{"op": "HEAL_FULL", "target": {"scope": "ALLY_CREATURE", "count": "ALL"}}, {"op": "RESET_CHARGES", "target": {"scope": "SELF_ARTIFACT"}}]}`
- CHARGES: `{"max": 3, "gain_on": "on_creature_survived_combat_damage", "max_per_turn": 1, "spend_on": "on_charge_full"}`
- other fields: `{"flavor": "The light remembers every wound."}`

### Icon  (`artf_cleric_icon`) — class cleric, slot pool icon
- PASSIVE: `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "HEALED_THIS_TURN", "count": "ALL"}, "attack": 1, "vigor": 0, "duration": "THIS_TURN", "note": "only actual heals >=1 restored health count; overheal excluded (R13)"}`
- TRIGGER: `{"trigger": "ON_ALLY_DEATH", "condition": null, "effects": [{"op": "HEAL", "target": {"scope": "PLAYER_SELF"}, "amount": 2}]}`
- other fields: `{"flavor": "Faith is not fragile. It is forged."}`

### Bow  (`artf_astrologist_orb`) — class astrologist, slot pool bow
- PASSIVE: `{"op": "SET_PREY", "cadence": "ON_TURN_START", "order": "BEFORE_ALL_OTHER_TURN_START_EFFECTS", "target": {"scope": "ENEMY_CREATURE", "filter": "HIGHEST_ATTACK", "count": 1, "tiebreak": "OLDEST_IN_PLAY"}}`
- TRIGGER: `{"trigger": "ON_PREY_DESTROYED", "condition": {"op": "DURING_YOUR_TURN"}, "effects": [{"op": "DRAW", "target": {"scope": "PLAYER_SELF"}, "amount": 1}], "frequency": "ONCE_PER_TURN"}`
- other fields: `{"flavor": "The forest marks its own."}`

### Quiver  (`artf_astrologist_constellation_starlight`) — class astrologist, slot pool quiver
- PASSIVE: `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ATTACKS_PREY", "count": "ALL"}, "attack": 1, "vigor": 0, "duration": "WHILE_ATTACKING"}`
- TRIGGER: `{"trigger": "ON_CREATURE_ATTACKS", "condition": {"op": "NTH_ATTACKER_ON_PREY_THIS_TURN", "value": 2}, "effects": [{"op": "DAMAGE", "target": {"scope": "PLAYER_ENEMY"}, "amount": 1}], "frequency": "ONCE_PER_TURN"}`
- other fields: `{"flavor": "The whispers guide every arrow."}`

### Skull  (`artf_necromancer_skull`) — class necromancer, slot pool grimoire
- PASSIVE: `{"op": "COST_MOD", "applies_to": "CREATURE", "condition": {"op": "CREATURE_DIED_THIS_TURN", "side": "ANY"}, "target": {"scope": "PLAYER_SELF"}, "amount": 1}`
- TRIGGER: `{"trigger": "ON_CHARGE_FULL", "timing": "END_OF_TURN", "condition": null, "effects": [{"op": "REVIVE_TOKEN", "target": {"scope": "PLAYER_SELF"}, "token": "artf_revenant_token", "stats": {"attack": 3, "vigor": 3}, "on_board_full": "FIZZLE"}, {"op": "RESET_CHARGES", "target": {"scope": "SELF_ARTIFACT"}}]}`
- CHARGES: `{"max": 3, "gain_on": "on_ally_dies", "spend_on": "on_charge_full"}`
- other fields: `{"flavor": "Death is a door. The Court holds the key."}`

### Shard  (`artf_necromancer_ritual_piece`) — class necromancer, slot pool phylactery
- PASSIVE: `{"op": "PREVENT_DAMAGE", "target": {"scope": "PLAYER_SELF"}, "amount": 1, "source": "ATTACK", "condition": {"op": "FEWER_ALLY_CREATURES_THAN_ENEMY"}, "note": "evaluated at damage-application time (R21)"}`
- TRIGGER: `{"trigger": "ON_CREATURE_DIES", "condition": {"op": "ENEMY"}, "effects": [{"op": "HEAL", "target": {"scope": "PLAYER_SELF"}, "amount": 1}]}`
- other fields: `{"flavor": "Even death pays its dues."}`

### Hammer  (`artf_runesmith_hammer`) — class runesmith, slot pool hammer
- PASSIVE: `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "FIRST_SUMMONED_THIS_TURN", "count": 1}, "attack": 0, "vigor": 1, "duration": "PERMANENT", "note": "no cost threshold; tokens count (R23)"}`
- TRIGGER: `{"trigger": "ON_SUMMON", "condition": {"op": "FRIENDLY_ON_YOUR_TURN"}, "effects": [{"op": "ADD_CHARGE", "target": {"scope": "PLAYER_SELF"}, "amount": 1}]}`
- CHARGES: `{"max": 3, "gain_on": "on_ally_summoned", "spend_on": "partner_anvil"}`
- other fields: `{"flavor": "Strike deep. The mountain remembers."}`

### Anvil  (`artf_paladin_banner`) — class runesmith, slot pool anvil
- PASSIVE: `{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "HAS_PERMANENT_BUFF", "count": "ALL"}, "attack": 1, "vigor": 0, "duration": "WHILE_PRESENT"}`
- TRIGGER: `{"trigger": "ON_TURN_END_NO_ATTACK", "condition": {"op": "AND", "all": [{"op": "ALLY_CREATURE_EXISTS"}, {"op": "PARTNER_CHARGES_GTE", "value": 1}]}, "effects": [{"op": "FORGE", "spend_from": "PARTNER_SLOT", "spend": "ALL", "target": {"scope": "ALLY_CREATURE", "filter": "HIGHEST_COST", "count": 1, "tiebreak": "OLDEST_IN_PLAY"}, "per_charge": {"attack": 1, "vigor": 1}, "duration": "PERMANENT"}]}`
- other fields: `{"flavor": "What is not broken can be reforged."}`

**Totals: 65 deck/tutorial cards + 14 artifacts. Launch target: 333 deck cards + 42 artifacts (3 variants per slot), i.e. ~296 still to produce via the pipeline once the sim gate is trusted.**