# Deck Forge artifacts (FABLE-054)

Every deck carries **two artifacts**, picked in the Deck Forge from its class's pool of four. They sit in the framed slots beside the deck count. A new deck starts with the two marked ★, and a deck with no legal pick uses those two. Artifacts aren't cards, so they never count toward the 30.

- **Content:** `content/artifacts/forge_artifacts.json`, with a copy in `client/content/artifacts/`. Each artifact has `"forge": true`, `"default"`, a printed `"text"` and an `"art_id"`. The art reuses existing artifact paintings, so no image credits were spent.
- **Engine:**
  - `ArtifactRegistry.ForgePool`, `ForgeDefaults`, `IsValidLoadout`, `PlayerLoadout` and `OpponentForgeLoadout`.
  - The old launch and variant artifacts still exist for tutorials and bosses. They never mix with the Forge pool.
- **Rules test for every artifact:** `tests/Engine/ForgeArtifactTests.cs`. Its text is checked against what the engine does.
- **Online:** a lobby seat posts its two picks after its deck as `artf_` entries. `SeatConfig.FromPosted` splits them out on both phones and on the PC bot. **Everyone must update**: an older build would read the picks as cards.

## Engine pieces this needed

- **An artifact's Charges are its own.** `SELF_ARTIFACT` add/reset touches only that artifact's slot. Before, it emptied or filled the partner artifact too.
- **`extra_triggers`** lets one artifact listen for more than one event.
- **New target filters:**
  - `EVENT`: the creature just summoned.
  - `EVENT_ADJACENT`: the lanes beside it.
  - `EARLIEST_SUMMONED`: the first creature summoned this turn.
  - `HAS_ATTACKED`
  - `ATTACK_ABOVE_VIGOR`
- **First creature attacked is marked before its listeners run.** It used to be set after, so "the first creature attacked each enemy turn" (the old Shield, now Unbroken Bulwark) found no one on the first blow.

## The pool

### Warrior: the one big swing

- **Warbrand** ★: Your first attacker each turn gets +3 Attack this turn.
- **Bloodied Standard** ★: At the end of your turn, each of your damaged creatures gets +1/+1 for good.
- **Champion's Oath**: Start of your turn: if you control exactly one creature, it gets +1/+1 for good.
- **Twinblade**: Gain a Charge whenever one of your creatures attacks. At 2: your strongest creature that already attacked may attack again, with +2 Attack this turn.

### Battlemage: control the tempo, punish the plan

- **Spellwake Focus** ★: Whenever you cast a Ritual, deal 2 damage to the enemy creature with the highest Attack.
- **Tidecaller's Vigil** ★: The first time you cast a Ritual each turn, draw a card and your creatures get +1 Attack this turn.
- **Null Sigil**: Gain a Charge at the end of each of your turns. At 2: the enemy's artifacts are Suppressed for their next turn, and you set a Counter Sigil.
- **Undertow Ring**: Gain a Charge whenever you cast a Ritual. At 2: return the enemy's highest-cost creature to its owner's hand.

### Necromancer: death is a resource

- **Bone Reliquary** ★: Gain a Charge whenever one of your creatures dies. At 4: your strongest dead creature costing 3 or less rises again.
- **Gravecaller's Skull** ★: At the end of your turn, if one of your creatures died this turn, raise a 1/1 Risen Bones.
- **Blighthorn Skull**: Start of your turn: give the enemy creature with the most Vigor Burn 2.
- **Soul Jar**: Whenever one of your creatures dies, deal 1 damage to the enemy.

### Paladin: nothing gets through

- **Aegis of Sunspire** ★: The first creature you summon each turn gets +0/+2 for good.
- **Oathkeeper's Banner** ★: At the end of your turn, heal each of your creatures 1. Guard creatures heal 2.
- **Unbroken Bulwark**: The first time each enemy turn one of your creatures is attacked, prevent 2 of the damage it would take.
- **Knight-Commander's Hammer**: Start of your turn: if you control a Knight, summon a 1/2 Squire with Guard.

### Druid: the garden grows

- **Heartroot Totem** ★: Start of your turn: your most wounded creature gets +1/+1 for good and heals 2.
- **Grove Seedbook** ★: Gain a Charge at the start of each of your turns. At 3: +1 Attunement for the rest of the duel.
- **Grovewing Moth**: Whenever you summon a creature, the creatures in the lanes beside it get +1/+1 for good.
- **Wildheart Grimoire**: Gain a Charge at the start of each of your turns. At 2: if you control a Beast, a 2/1 Wolf Cub joins it.

### Rogue: steal, slip, strike

- **Venomfang** ★: Your first attacker each turn gets +1 Attack and Venom this turn.
- **Cutpurse Dagger** ★: Gain a Charge the first time one of your creatures attacks each turn. At 2: the enemy discards a random card and you draw 1.
- **Gloom Hook**: The first creature you summon each turn gains Dodge 30%.
- **Saboteur's Spike**: Gain a Charge at the start of each of your turns. At 3: the enemy's artifacts are Suppressed for a turn, and you take control of their cheapest creature this turn.

### Astrologist: the stars decide where you stand

- **Stillwater Orb** ★: Gain a Charge at the start of each of your turns. At 2: Stun the three enemy creatures with the highest Attack.
- **Lockstar Astrolabe** ★: Start of your turn: Lock one empty enemy lane, and the enemy creature with the highest Attack gets -2 Attack until your next turn.
- **Gravity Sphere**: At the end of each of your turns: Drain 1 (the enemy has 1 less Attunement next turn).
- **Inverted Sky**: Start of your turn: every enemy creature with more Attack than Vigor has them swapped, then their creature with the highest Attack gets -1 Attack for good.
## Balance (sims, 2026-10-06)

**How the sims ran:**

- Each class played a 30-card singleton deck from its stratum: best class-synergy cards first, then by power.
- GreedyBot played both sides, with seats alternated.
- The harness was kept out of the repo, the same way as the drop sims.

**Overall win rate per class:**

| Setup | Warrior | Battlemage | Necromancer | Paladin | Druid | Rogue | Astrologist |
|---|---|---|---|---|---|---|---|
| No artifacts at all (60 games per pairing) | 55% | **28%** | 46% | 62% | 53% | **65%** | 41% |
| First draft of the artifacts | 44% | 29% | **79%** | **75%** | 44% | 38% | 41% |
| **As shipped** (starter pairs, 80 games per pairing) | **48%** | **52%** | **54%** | **52%** | **48%** | **49%** | **47%** |

**Within each class**, every pair of the four was played against every other class (20–30 games per opponent).

- After tuning, each class's six pairs sit within about 15 points of each other.
- No artifact is a must-pick or a dead pick.
- The weakest pairs are Tidecaller's Vigil + Null Sigil (Battlemage) and Stillwater Orb + Inverted Sky (Astrologist), both at about 30–45%.

**What tuning changed from the first draft:**

- **Necromancer (79% → 54%):**
  - Bone Reliquary needs 4 deaths, not 3, and only raises creatures costing 3 or less.
  - Gravecaller's Skull only counts your own creatures' deaths.
- **Paladin (75% → 52%):** Aegis gives +0/+2 instead of Ward.
- **Battlemage (29% → 52%):**
  - Spellwake Focus hits for 2.
  - Tidecaller's Vigil now draws a card and gives +1 Attack on your first Ritual each turn.
  - Null Sigil and Undertow Ring fill at 2 Charges.
- **Astrologist (41% → 47%):**
  - Stillwater Orb fills at 2 and Stuns three creatures.
  - Lockstar gives −2 Attack.
  - Gravity Sphere Drains every turn.
  - Inverted Sky flips every glass cannon each turn, then gives their biggest hitter −1 Attack.
- **Warrior:**
  - Warbrand gives +3.
  - Bloodied Standard gives +1/+1.
  - Twinblade fills at 2 and grants +2.
  - Champion's Oath lost Pierce.
- **Rogue:**
  - Venomfang also gives +1 Attack.
  - Cutpurse Dagger fills at 2.
- **Druid:**
  - Heartroot gives +1/+1.
  - Wildheart needs only one Beast.

### Flagged for Trikzos: matchups too lopsided for artifacts to fix

These come from the decks themselves:

| Matchup | Win rate | Why |
|---|---|---|
| Battlemage vs Paladin | 5% | Battlemage's deck is mostly Rituals and Sigils, with few bodies, so it can't get through a wall of Guards. |
| Paladin vs Rogue | 18% | Rogue's discard and Venom take Paladin's walls apart. |
| Rogue vs Battlemage | 14% | Counter Sigils and bounce beat Rogue's tempo. |
| Necromancer vs Battlemage | 16% | Same reason as Rogue. |

Fixing these means changing cards. Options:

- Give Battlemage two or three sturdier spell-fed creatures.
- Give Paladin one answer to Venom.

The bot also undervalues control decks. Battlemage was 28% even before artifacts, so real players may see a gentler spread than these numbers.
