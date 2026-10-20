# Runewake — The World (foundation, v1)

*A base to build on. Everything here is deliberately shallow at the edges so it can grow. Trikzos owns this
document; anything may be renamed or overruled. When a name changes here, `tools/gen_start_zones.py` and
`content/classes.json` are where it must change in the game.*

---

## 1. The land nobody has mapped

The world is called **Ulmaren** by the few who still use a name for it. Most people name only the place they
stand on. There are no complete maps. The sea has no far shore anyone has returned from, the sky has a
second, darker moon that is not always there, and the ground itself is older than the gods who claim it.

Ulmaren has had at least two ages:

- **The Old Age.** The age of the Runes — the first language, in which things were *named into being*. The
  seven Wardens, the great Seals, the Tower, and the World Tree are all its leavings. The Old Age ended in a
  cataclysm nobody fully remembers: *mountains knelt, seas traded places with the sky*, and the Old Age's
  wonders were buried on purpose, by people who thought they were saving the world from them.
- **The Buried Age** — now. A long, quiet after. Small peoples in small places, living on top of a world they
  do not understand. Every so often a seal cracks, a rune wakes, and the buried world remembers it is awake.

That is the game's title: **Runewake** — the Runes are waking. Each class is one of the small peoples of
the Buried Age, standing closest to one of the Old Age's leavings, and shaped by it.

### The Runes and the Strata
Magic in Ulmaren is not one thing. It is five **Strata** — five layers of the Old Age's language still
running under the ground, like seams in rock:

| Stratum | What it is | Who leans on it |
|---|---|---|
| **EMBER** | fire, forging, the will to strike | Warrior |
| **TIDE** | water, memory, the sky-sea, the currents between worlds | Battlemage, Astrologist |
| **DAWN** | light, oaths, the kept vigil | Paladin |
| **VERDANT** | growth, root, the Source | Druid |
| **HOLLOW** | the dead, the dark, what is taken | Necromancer, Rogue |

Nobody chose their Stratum. People simply grew up on top of one.

### Things everyone has heard of (and nobody has seen)
- **The Seals** — Old Age locks on something beneath the world. Each starting region ends at one.
- **The Tower** — a spire with no door that the Astrologists say can be seen from the Moon.
- **The Crossroads** — where the seven homelands' roads are said to meet. Finding it is how the open
  world begins.
- **The Void** — a tear at the western edge of everything, where the Old Age's cataclysm never finished.
- **The Source** — the spring the World Tree drinks from. Druids say it is the Old Age's language still
  being spoken, very quietly.
- **The Gods of Old** — the Old Age's makers, or its victims, or both. Offerings still reach them (see
  *Purchase Drachmae*).

---

## 2. The seven homelands

### Warrior — Harrowfield · region "The Long Furrow" (EMBER)
A farming village so far from anything that the roads to it have names but no destinations. There is
nothing in Harrowfield but fields, weather, and the long evenings — so the people fill them with
training. They train the way other villages gossip: every yard has a sparring post, every harvest ends
in a tourney, and every child learns to swing a scythe and a sword in the same week. The result is a
village of farmers who happen to be the strongest fighters in the known world, and who mostly do not
know it. Under the fields runs the **Ember** stratum; the old forges in the Furrow still burn with no fuel.
- **Place names:** Harrowfield (the village), the Long Furrow (the fields and the road out), the Threshing
  Ring (the tourney ground), Old Smoke (the fuel-less forge), Cairn Hill (where the first farmers are buried).
- **People:** stubborn, plain-spoken, trust work over talk. Fight with farm iron turned to war iron.
- **Threats:** raiders who think a farm village is easy; the forge waking things in the hills; the
  village's own champions, who fight each other for want of anyone else.
- **The Seal:** under Cairn Hill. The first farmers were put there to sit on it.

### Necromancer — the Greyvault Undercroft · region "The Greyvault Undercroft" (HOLLOW)
**Greyvault** is one hill with two faces. On its crown stands a cathedral of grey stone that has never
once stopped ringing its bells; under it lie the catacombs — level under level of the Old Age's dead,
and the Buried Age's dead stacked on top. The Necromancers are the people of the *under*: bone-keepers,
grave-wardens, the ones who learned that the dead down here are not finished. They are not evil and they
are not loved. They keep the dead in order, and the dead keep them company. They have never met the
people upstairs face to face; there is a sealed door between the Undercroft and the nave, and both sides
keep it sealed.
- **Place names:** Greyvault (the hill), the Undercroft (the catacombs), the Bone Stair, the Ossuary
  Court, the Sealed Door, the Quiet Choir (a chamber where the dead hum).
- **People:** patient, dry, literal. Speak to the dead as colleagues.
- **Threats:** things that climb up from the lowest level; the upstairs church's purgers when they come
  down; rival keepers who want the dead to *serve* instead of rest.
- **The Seal:** the lowest level of all. The dead were stacked on it on purpose.

### Paladin — the Greyvault Ward · region "The Greyvault Ward" (DAWN)
The *other* face of the same hill: the cathedral, its cloisters, its lantern-lit graveyard where the
Buried Age's dead are laid in the open air. The Paladins are the Order of the Vigil — the ones who keep
the bells ringing and the lanterns lit, because the Old Age taught that the dark only takes what is left
unwatched. They know there is something under the hill. They do not go down. They stand on top of it,
and that is the whole of their faith: **stand where you are put, and do not move.**
- **Place names:** the Ward (the cathedral precinct), the Lantern Yard (the open graveyard), the Bell
  Tower, the Oath-Stone, the Sealed Door (the same door, from above).
- **People:** formal, kind, immovable. Oaths are literal; a broken one is a wound.
- **Threats:** oath-breakers; raiders after the reliquaries; what comes *up* through the Sealed Door; the
  Order's own inquisitors, who test every new knight.
- **The Seal:** the one under the hill. The Paladins guard it from above without knowing its name.

*Design note: the two Greyvault regions share a map plate but never a node. Later, when the open world
exists, the Sealed Door is a natural place for the two classes to finally meet.*

### Battlemage — Sealwick · region "The Unmade Coast" (TIDE)
At the western edge of Ulmaren the ground stops making sense. That is **the Void**: not emptiness but a
wound in space where the Old Age's cataclysm never finished happening — stairs that climb into last week,
rain that falls upward, a river that is also a wall. The shore along its edge is **the Unmade Coast** — the
place where the world stops being finished — and on it sits **Sealwick**: a quiet, orderly, almost sleepy village of people whose whole purpose is to
keep the Void from spreading. They are mage-raiders: they go *in*, in bands, fight whatever the Void has
made, plant a seal, and come home to dinner. Nobody in Sealwick talks about it much. It is the job.
- **Place names:** Sealwick (the village), the Unmade Coast (the shore along the Void), the Stitchline (the chain of seals),
  the Upstairs River, the Breach (where the Void last pushed through), the Quiet Gate.
- **People:** calm, methodical, dryly funny; magic as a trade, not a mystery. Blade in one hand, spell in
  the other.
- **Threats:** Void-made things (geometry with teeth); unsealed breaches; raiders who come back changed;
  the Coast's own warden, who decides who is ready to go in.
- **The Seal:** the first seal on the Stitchline, the one every other seal is anchored to.

### Druid — the Boughs of Yrmund · region "The Rootwake" (VERDANT)
**Yrmund** is a tree the size of a mountain, and it walks. Slowly — a league a season — but it has
never stopped, and the trench of torn ground and new forest it leaves behind is called the **Rootwake**.
The Druids live in its boughs and go where it goes. They stay close because the tree drinks from the
**Source**, and near the Source the Old Age's language can almost be heard; a Druid's life is spent
learning to hear it. They are the only people in Ulmaren who do not have a *place* — they have a
direction.
- **Place names:** Yrmund (the tree), the Boughs (the settlement in its crown), the Rootwake (its trail),
  the Heartwood Stair, the Source-Well, the Fallen Bough (a limb Yrmund shed, now a village of its own).
- **People:** unhurried, listening, strange to outsiders. Measure time in the tree's steps.
- **Threats:** blight that follows in the Rootwake; poachers and timber-lords who want the tree stopped;
  the things that lived in the ground Yrmund tears open.
- **The Seal:** Yrmund is walking *toward* it. Nobody knows why.

### Astrologist — Penumbra Camp · region "The Farside" (TIDE)
There is a camp on the dark side of the Moon. The Astrologists do not find this remarkable. They were
put there in the Old Age to watch — the far side of the Moon faces *out*, at the other planets and the
deep sky — and they never left. They travel by riding the **solar currents**: tides of light between the
worlds, which a trained Astrologist can read and sail the way a sailor reads wind. From Penumbra they can
reach the other planets, and sometimes do. From Ulmaren they can reach the Moon. Their magic is the
TIDE stratum turned upward — the sky as a sea.
- **Place names:** Penumbra Camp (the settlement), the Farside (the Moon's dark hemisphere), the Lens
  (the great observatory), the Current-Stair (where sailings begin), the Eclipse Field, the Unlit Crater.
- **People:** precise, patient, a little cold; speak in charts and omens. Nothing surprises them, which
  surprises everyone else.
- **Threats:** what falls from the deep sky; cultists who want the Lens turned the wrong way; currents
  that go somewhere they shouldn't; the camp's own Warden, who decides who may sail.
- **The Seal:** in the Unlit Crater. The Astrologists were posted on the Moon to *watch it*.

### Rogue — Tamunet · region "The Hungry Sands" (HOLLOW)
Far to the south the land is a desert of red stone and white dunes, and in it stands **Tamunet**, a city
of tomb-temples older than any kingdom, with a living town crammed into its gaps. Tamunet starves. It has
always starved: the river that fed it moved in the Old Age and never came back. So its people learned
to take. A child of Tamunet can lift a purse before they can read, and the clever ones live long enough
to become very clever indeed. They are not proud of it and they are not ashamed; it is what the city
is. Under the sand runs the **Hollow** stratum — the dead of the tomb-temples are the city's oldest
neighbours, and the Rogues have robbed them for a thousand years.
- **Place names:** Tamunet (the city), the Hungry Sands (the desert), the Tomb Row, the Dry River, the
  Salt Market, the Needle (the tallest tomb-temple), the Gullet (the thieves' quarter).
- **People:** quick, funny, loyal to their own crew and no one else. Think three moves ahead because
  the hungry do.
- **Threats:** tomb-guardians that wake when robbed; rival crews; the tax-men of whatever kingdom
  currently claims the desert; the Guild's own master, who tests every rat before they run.
- **The Seal:** at the bottom of the Needle. The Rogues have been digging toward it for generations
  without knowing what it is.

---

## 3. How the homelands connect (for later)
- All seven roads lead, eventually, to **the Crossroads**. Nobody in any homeland has been there.
- Greyvault's two faces (Paladin / Necromancer) share a hill and a Sealed Door.
- The Unmade Coast (Battlemage) and the Farside (Astrologist) share the TIDE stratum: the Void and the deep sky may
  be the same wound seen from two sides.
- Yrmund (Druid) walks; one day it could arrive at *any* of the others.
- Harrowfield (Warrior) is "the middle of nowhere" — later, nowhere turns out to be the middle of
  everything: the Crossroads may be nearest to it.
- Tamunet (Rogue) sits on the Dry River; where the river went is a story.

## 4. The rite before battle (FABLE-075)
Before every fight the **Gods of Old** answer whoever walks the Buried Age's roads: one blessing, taken at the
start of the battle — **Endurance** (more Vigor), **Insight** (more cards) or **Attunement** (a deeper well).
Nobody knows which god it is. Everyone has a theory.

## 5. Naming conventions
- **Places** are plain Buried-Age words with one old root: Harrowfield, Sealwick, Greyvault, Tamunet,
  Penumbra. No apostrophes, no invented syllable soup.
- **People** get a trade or a title before the name: Reeve, Keeper, Lantern-Sister, Sealwright,
  Current-Reader, Cutpurse. Warden is the last keeper of a region; the boss is the Warden revealed.
- **Old Age** things get grander names (Yrmund, the Stitchline, the Lens) — they were named in the Runes.
