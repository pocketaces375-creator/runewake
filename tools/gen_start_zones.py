#!/usr/bin/env python3
"""FABLE-068: every class starts in its own land.

Writes the seven class starting zones (Trikzos: "Each class needs its own starting zone, thematically
correct, same with the enemies — reimagine each map to be correct to the class"):

  content/start_zones.json             which class gets which zone: its name, colours, map layout and
                                        the encounter on every node of the first region
  content/encounters/start_zones.json  the 63 themed encounters (9 per class), with a lesson card on
                                        each fight after the guided first duel

…and mirrors both into client/content/ (the copy the game actually loads).

The zones overlay region_01: same node ids and unlock graph, so saves, the tutorial, the campaign loop
and the region chain (r1_n12 → region_02) are untouched — only what stands on each node changes.
Decks are built here from the card pool (only cards with art), themed by stratum and card name, and
ramp gently: the second fight is all commons, rares only show up from the Warden on.

Run:  python3 tools/gen_start_zones.py      (deterministic; re-running gives identical files)
"""
import glob
import math
import json
import os
import random
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# ── the path shape, laid on a different stretch of the map plate (1536×704) per class ──────────────
SHAPE = [(0, 0), (-1, 1), (1, 1), (-1.25, 2), (1.25, 2), (-1.35, 3.05), (0, 2.75), (1.35, 3.05),
         (-0.7, 4), (0.75, 4), (0, 5), (0, 6)]
NODES = [f"r1_n{i:02d}" for i in range(1, 13)]
FIGHTS = ["r1_n01", "r1_n02", "r1_n04", "r1_n05", "r1_n06", "r1_n09", "r1_n10", "r1_n11", "r1_n12"]

# node: (tier for deck/drops, enemy Vigor, shard, dig charges, fragment (P=primary,S=secondary, amount), modifier)
NODE_RULES = {
    "r1_n01": (0, 18, 30, 0, None, None),
    "r1_n02": (1, 18, 40, 1, None, None),
    "r1_n04": (2, 20, 80, 1, ("P", 2), "ELITE"),
    "r1_n05": (2, 20, 50, 0, None, None),
    "r1_n06": (3, 20, 50, 0, None, None),
    "r1_n09": (4, 21, 100, 2, ("S", 2), "ELITE"),
    "r1_n10": (3, 21, 60, 0, None, None),
    "r1_n11": (4, 22, 150, 3, ("P", 3), "WARDEN"),
    "r1_n12": (5, 22, 300, 5, ("P", 5), None),
}

LESSONS = {
    "GUARD":   ("GUARD", "Attacks on an empty lane hit a GUARD creature first. Put one where you are open — and when the enemy has one, break it before you go for their Vigor."),
    "WARD":    ("WARD", "WARD blocks the next damage, once. Pop it with a small hit or a cheap spell before you send in something big."),
    "SWIFT":   ("SWIFT", "SWIFT creatures can attack the turn they arrive. Keep a blocker up — and use your own to finish a foe that thinks it is safe."),
    "PIERCE":  ("PIERCE", "PIERCE: damage past a killed blocker carries on to the player. Chump-blocking with a tiny creature will not fully save you."),
    "VENOM":   ("VENOM", "VENOM: anything it damages in combat is destroyed. Don't trade your best creature into it — block with something small, or kill it with a ritual."),
    "DODGE":   ("DODGE", "DODGE gives a chance to take no combat damage at all. Rituals are the surer answer."),
    "ARMOR":   ("ARMOR", "ARMOR takes that much less damage from every hit. Many small hits do little — hit it once, and hit it hard."),
    "EXALTED": ("EXALTED", "Their first attacker each turn gets +1/+1 for each Exalted creature they control. Kill the Exalted ones and the big swing shrinks."),
    "UNEARTH": ("UNEARTH", "Some creatures claw their way back out of the Barrow — that is UNEARTH. Killing one may not be the end of it."),
    "RITUAL":  ("RITUALS", "A card with no Strength and no Vigor is a Ritual: it resolves the moment it is played. This foe leans on them — spread your board so one spell can't undo it."),
    "HAND":    ("READ THE TABLE", "Along the top you can see how many cards your foe is holding. A full hand late in a fight usually means something big is coming — keep a blocker back."),
    "ELITE":   ("ELITE", "Elites start with more Vigor and pay out more: shards, a dig charge and stratum fragments. Build a board before you start trading."),
    "WARDEN":  ("WARDEN", "The keeper of this land. Wardens carry Relics too — each gathers charges and fires when full. Hold a card or Relic to read it."),
    "BOSS":    ("BOSS RULES", "Bosses bend the rules. Your leftmost lane starts buried and only opens when the boss's first creature dies — kill one early."),
}

# class: zone, strata, strata2, skin, plate layout (start x, y, across px, along px, direction), lessons for
#        n02 n05 n06 n09 n10, name regex for on-theme cards, and the nine foes:
#        node: (name, opponent class, intro lines, outro lines)
ZONES = {
 "battlemage": dict(
    zone="The Drowned Markets", strata="TIDE", strata2="EMBER", skin="start_tide",
    layout=(330, 440, -8, 152, 112, False), lessons=["GUARD", "WARD", "RITUAL", "PIERCE", "HAND"],
    theme=r"arcane|spell|glyph|mana|wave|tide|brine|riptide|null|echo|mirror|storm|arc|deep|coral|undertow|scholar|adept|current|flood",
    foes={
     "r1_n01": ("Dockside Duelist Wren", "battlemage",
                ["Saltmere's lower market floods twice a day. Between tides, the aspirants duel on the stalls.",
                 "A young duelist flicks seawater from her wand. \"First lesson's free. Try to keep up.\""],
                ["Wren laughs, soaked to the knee. \"Not bad. The market will want a look at you.\""]),
     "r1_n02": ("The Breakwater Golem", "battlemage",
                ["Something of barnacled stone wades out of the shallows, set to guard the toll-steps.",
                 "Its rune-eyes flare. Spell-wrought, it lets no one pass unpaid."],
                ["The golem slumps back into the surf, its runes guttering out. The toll-steps are yours."]),
     "r1_n04": ("Spell-Broker Maddoc", "rogue",
                ["In a stall stacked with stoppered bottles, a broker sells spells by the breath.",
                 "\"You've been casting magic you never paid for, friend. Let's settle the account.\""],
                ["Maddoc counts what is left of his stock and sighs. \"Call it even. Don't come back.\""]),
     "r1_n05": ("Brine-Hexer Ilsa", "battlemage",
                ["A woman in a coat of fishskin draws a ward of seawater around herself.",
                 "\"Strike all you like. The tide gives back what it takes.\""],
                ["Her ward bursts like a wave on rock. \"Well,\" Ilsa says, dripping. \"The tide was wrong.\""]),
     "r1_n06": ("Tidecaller Pell", "astrologist",
                ["On a drowned bell-tower, a tidecaller chants the sea higher with every verse.",
                 "\"Saltmere is mine at high water. And the water is always rising.\""],
                ["The chant breaks. The sea, sulking, slides back down the tower steps."]),
     "r1_n09": ("The Undertow Captain", "warrior",
                ["A drowned galleon lists in the harbour mouth. Her captain never left her.",
                 "\"Saltmere owes me a ship. You'll do as a down payment.\""],
                ["The captain sinks back into the dark water, saluting as he goes."]),
     "r1_n10": ("Archivist Corran", "battlemage",
                ["Under the market lies a sunken library, its shelves read only at low tide.",
                 "\"Every spell you cast, I have already catalogued. Show me something new.\""],
                ["Corran scribbles furiously. \"A new entry. How thrilling. How humiliating.\""]),
     "r1_n11": ("Warden Seraphine of the Tide-Gate", "battlemage",
                ["At the Tide-Gate, the whole sea is held back by one woman's will.",
                 "\"Aspirants come every season. Few pass. Show me you can hold a spell under pressure.\""],
                ["Seraphine steps back from the gate. \"Then come below. See what I have been holding back.\""]),
     "r1_n12": ("Seraphine, the Drowned Archmage", "battlemage",
                ["The gate opens. Seraphine stands in the eye of a standing wave.",
                 "\"Saltmere was a city once, not a market. I drowned it to seal what sleeps beneath — and I would do it again.\""],
                ["The wave collapses. Seraphine kneels in the shallows. \"Go, then. The deep road is open. Beyond it, the Cinderfall Steps.\""]),
    }),
 "necromancer": dict(
    zone="The Ossuary Veils", strata="HOLLOW", strata2="DAWN", skin="start_hollow",
    layout=(1220, 470, 188, 150, 112, False), lessons=["GUARD", "UNEARTH", "VENOM", "SWIFT", "HAND"],
    theme=r"bone|grave|barrow|lich|ossuary|rot|plague|raise|crypt|marrow|death|soul|ghoul|revenant|thrall|wraith|necro|horde|tithe|unearth|putrid",
    foes={
     "r1_n01": ("Gravedigger Hobb", "necromancer",
                ["Palewatch buries its dead in tiers — a city of bone stacked to the clouds.",
                 "An old gravedigger leans on his spade. \"Fresh blood for the Veils? Let's see if you can raise more than dust.\""],
                ["Hobb nods slowly. \"You'll do. The dead are watching you now.\""]),
     "r1_n02": ("The Ossuary Sentinel", "necromancer",
                ["A sentinel of stacked skulls blocks the stair to the lower crypts.",
                 "It was built to stand, not to think — and it stands very well."],
                ["The sentinel topples, skulls rolling down the stair like dice."]),
     "r1_n04": ("Purger Halvard", "paladin",
                ["A knight of the dawn church has come to cleanse the Veils with lantern and blade.",
                 "\"Every corpse you raise is a soul you steal. I will put them back to rest — and you with them.\""],
                ["Halvard's lantern goes out. \"The abbess will hear of this,\" he gasps, and flees up the stair."]),
     "r1_n05": ("The Twice-Buried", "necromancer",
                ["The ground heaves. Something that was buried has decided not to stay that way.",
                 "Bury it once and it climbs back. You will have to bury it again."],
                ["This time it stays down. Mostly."]),
     "r1_n06": ("Rotbloom Widow", "druid",
                ["In the grave-gardens, nightshade grows from the graves of poisoners.",
                 "The widow tending them smiles. \"Every bloom here killed someone. Care to smell one?\""],
                ["The widow wilts among her flowers. Even the nightshade turns away from you."]),
     "r1_n09": ("Lantern-Abbess Mirel", "paladin",
                ["The Purger's abbess comes herself, a procession of lanterns behind her.",
                 "\"Palewatch was a holy place before your kind came. It will be again.\""],
                ["The lanterns gutter one by one. Mirel lowers her staff. \"Not today, then. But the dawn always comes.\""]),
     "r1_n10": ("Bone-Scribe Ettrin", "necromancer",
                ["A rival necromancer records every death in Palewatch in a ledger bound in skin.",
                 "\"Your name is not in my book yet. Let's correct that.\""],
                ["Ettrin tears a page out of his ledger — the one with your name half-written."]),
     "r1_n11": ("Warden Corvane, Keeper of the Veils", "necromancer",
                ["At the top of the bone city, the Keeper decides who may command the dead.",
                 "\"Many can wake a corpse. Few can make it obey. Show me.\""],
                ["Corvane's thralls kneel — to you. \"Interesting,\" he says. \"Come down to the throne.\""]),
     "r1_n12": ("Corvane, the Pale Sovereign", "necromancer",
                ["The Keeper sheds his robe. Beneath is bone, and an old light burning in empty sockets.",
                 "\"I have ruled the Veils for three hundred years. I did not do it by losing.\""],
                ["The light in Corvane's eyes dims. \"The Veils are yours. Beyond them, the Cinderfall Steps — go and be feared there.\""]),
    }),
 "paladin": dict(
    zone="The Sunspire Marches", strata="DAWN", strata2="VERDANT", skin="start_dawn",
    layout=(1190, 300, 172, 150, 112, True), lessons=["GUARD", "WARD", "ARMOR", "SWIFT", "HAND"],
    theme=r"dawn|sun|light|oath|knight|squire|templar|vigil|aegis|shield|bastion|herald|lantern|radiant|holy|gleam|bulwark|paladin|guard|warden|bless",
    foes={
     "r1_n01": ("Squire-Captain Bram", "paladin",
                ["In the training yards of Sunspire, every oath begins with a bruise.",
                 "\"On guard, recruit. The light helps those who keep their shield up.\""],
                ["Bram lowers his practice blade. \"Good. Now go and swear it for real.\""]),
     "r1_n02": ("Shieldwall Veteran Hesk", "warrior",
                ["An old soldier holds the bridge out of Sunspire, as he has for twenty years.",
                 "\"Nobody crosses my bridge without showing me their shield-work.\""],
                ["Hesk steps aside, rubbing his shield arm. \"That'll do. Mind the marches.\""]),
     "r1_n04": ("Oathbreaker Sir Vael", "paladin",
                ["A knight in tarnished gold has turned his sword on the order that raised him.",
                 "\"Oaths are chains, child. I broke mine. Let me show you how.\""],
                ["Vael's blade cracks across the sigil of the Spire. He does not get up."]),
     "r1_n05": ("Sister Lumen of the Lantern", "paladin",
                ["A cleric of a rival order bars the pilgrim road, wrapped in wards of light.",
                 "\"Sunspire's light is not yours alone. Prove you deserve it.\""],
                ["Her wards fade like morning mist. \"Go in light, then,\" she says, and means it."]),
     "r1_n06": ("The Gilded Bulwark", "paladin",
                ["A war-construct of the old order, armoured in plates of beaten gold.",
                 "It was made to outlast sieges. It has outlasted its makers."],
                ["The Bulwark grinds to a halt, one golden fist still raised in salute."]),
     "r1_n09": ("Hollow Raider Kesh", "rogue",
                ["Raiders from the Hollows have struck the outer chapels and looted the reliquaries.",
                 "\"Gold and bones sell the same in the Hollows. Yours will too.\""],
                ["Kesh drops the stolen relics and runs. The chapel bells ring out behind you."]),
     "r1_n10": ("Inquisitor Aldric", "paladin",
                ["An inquisitor of the Spire questions every knight who seeks the high marches.",
                 "\"Faith untested is only habit. Let us test yours.\""],
                ["Aldric bows his head. \"Tested, and true. Go on.\""]),
     "r1_n11": ("Warden Dawnhild", "paladin",
                ["At the Sunspire's gate, the High Warden keeps the first flame.",
                 "\"Every knight of Sunspire stood where you stand. Not all of them walked away.\""],
                ["Dawnhild lowers her shield. \"Then face me at the flame itself.\""]),
     "r1_n12": ("Dawnhild, the Unbroken Wall", "paladin",
                ["The first flame rises around her like wings.",
                 "\"I have never yielded a step. Not to the dark, and not to you.\""],
                ["For the first time in her life, Dawnhild yields a step. \"The marches are yours, knight. The Cinderfall Steps wait beyond.\""]),
    }),
 "druid": dict(
    zone="The Mossgrave Wilds", strata="VERDANT", strata2="HOLLOW", skin="start_verdant",
    layout=(330, 330, 7, 152, 112, False), lessons=["GUARD", "SWIFT", "VENOM", "PIERCE", "HAND"],
    theme=r"root|moss|bark|grove|thorn|vine|fern|seed|bloom|wild|wood|leaf|thicket|wolf|stag|elk|hound|badger|treant|sprout|canopy|verdant|heart|pack|bramble|sylvan|forest",
    foes={
     "r1_n01": ("Grove-Keeper Tamsin", "druid",
                ["In the canopies of Mossgrave, new druids are tested by the forest itself.",
                 "\"The roots told me you were coming. Show me you can listen back.\""],
                ["Tamsin smiles. \"The forest likes you. Don't make it change its mind.\""]),
     "r1_n02": ("The Bark-Hide Bear", "druid",
                ["A bear with bark for a hide stands across the path, guarding its den.",
                 "It does not want to fight. It will, though."],
                ["The bear lumbers back into its den, grumbling. The path is clear."]),
     "r1_n04": ("Poacher-Lord Garran", "warrior",
                ["Snares and cages hang from every branch. Someone is stealing the forest's beasts.",
                 "\"Druids fetch a fine price too, you know.\""],
                ["You spring the cages. The beasts of Mossgrave scatter into the green, and Garran with them."]),
     "r1_n05": ("The Thicket Pack", "druid",
                ["Eyes in the underbrush. Then many eyes.",
                 "The pack moves before you can count them."],
                ["The pack melts back into the thicket. Somewhere, a wolf howls — almost respectfully."]),
     "r1_n06": ("Blightcaller Venn", "necromancer",
                ["Where he walks, the moss turns grey and the trees weep black sap.",
                 "\"The old growth is dying anyway. I'm only helping it along.\""],
                ["Venn falls, and green creeps back over the grey where he stood."]),
     "r1_n09": ("The Rot-Hart", "necromancer",
                ["A great stag, its antlers hung with grave-moss, stalks the blighted glade.",
                 "The blight has taken it. Put it to rest."],
                ["The Rot-Hart lies down among the ferns. Flowers open where it fell."]),
     "r1_n10": ("Hedge-Witch Morrow", "druid",
                ["A witch in a hut of living thorn trades in favours from the wild.",
                 "\"You want through my hedge? Pay in sweat.\""],
                ["Morrow's hedge parts with a sigh. \"Paid in full. Off you go.\""]),
     "r1_n11": ("Warden Oakheart", "druid",
                ["The oldest tree in Mossgrave opens its eyes.",
                 "\"I have watched druids grow for a thousand years. Grow, little sapling. Show me.\""],
                ["Oakheart creaks with something like laughter. \"Then come to my roots.\""]),
     "r1_n12": ("Oakheart, the Deep Root", "druid",
                ["The ground splits. The Warden's roots run under the whole of the wilds.",
                 "\"All of Mossgrave is my body. You cannot cut it all down.\""],
                ["Oakheart settles, a forest at peace. \"The wilds will remember you. The Cinderfall Steps will not be so kind.\""]),
    }),
 "rogue": dict(
    zone="The Duskchapel Undercroft", strata="HOLLOW", strata2="EMBER", skin="start_dusk",
    layout=(330, 470, -11, 150, 110, True), lessons=["GUARD", "SWIFT", "VENOM", "PIERCE", "DODGE"],
    theme=r"shadow|cutpurse|shiv|knife|knives|smoke|venom|alley|fence|quickfinger|pilfer|saboteur|turncoat|nightblade|tripwire|lookout|borrowed|vanish|poison|nightshade|blackout|veil|cutpurse|dust|fang",
    foes={
     "r1_n01": ("Fence-Mother Agathe", "rogue",
                ["Under Duskchapel's candlelit crypts, the thieves' guild takes on new blood.",
                 "\"So you want to be one of us. Lift my purse, darling — if you can.\""],
                ["Agathe checks her belt and finds it lighter. \"Ha! Welcome to the family.\""]),
     "r1_n02": ("Crypt-Guard Bolsover", "warrior",
                ["A hulking guard minds the chapel door, paid by the night and paid well.",
                 "\"Nobody gets past old Bolsover. Nobody.\""],
                ["Bolsover wakes up an hour later with his keys missing and no idea why."]),
     "r1_n04": ("The Candle-Snuffer", "rogue",
                ["Every light in the nave goes out at once. Someone is very good at this.",
                 "\"You're in my dark now.\""],
                ["You light a candle. The Snuffer is gone — but so is the dark."]),
     "r1_n05": ("Quickfinger Lisle", "rogue",
                ["A pickpocket half your age darts out of the crowd at the vigil market.",
                 "\"Race you to the bottom of your pockets!\""],
                ["Lisle hands back your coin purse with a wink. \"Fair's fair. You're faster.\""]),
     "r1_n06": ("Nightshade Priest Ozias", "necromancer",
                ["The chapel's own priest brews poison in the communion cup.",
                 "\"A sip for the faithful. A gulp for thieves.\""],
                ["Ozias chokes on his own sermon. You leave the cup well alone."]),
     "r1_n09": ("Sexton Graul's Diggers", "warrior",
                ["A gang of grave-robbers has claimed the lower crypts, and they don't share.",
                 "\"These dead are spoken for, rat.\""],
                ["The diggers scatter into the tunnels, leaving their loot — and their shovels."]),
     "r1_n10": ("Veilwalker Sable", "rogue",
                ["A thief who is there, then not, then behind you.",
                 "\"You'll never land a hit on what you can't see.\""],
                ["Sable is suddenly very easy to see, sitting on the floor. \"...Lucky.\""]),
     "r1_n11": ("Warden Mortimer, Master of Keys", "rogue",
                ["The guild's Master of Keys guards the vault beneath the altar.",
                 "\"Every lock in Duskchapel answers to me. Let's see if you do.\""],
                ["Mortimer tosses you a key. \"The vault, then. He's waiting.\""]),
     "r1_n12": ("Mortimer, Guildmaster of Knives", "rogue",
                ["The vault door swings open on a man with a knife for every finger.",
                 "\"I taught half this city to steal. I'll teach you to lose.\""],
                ["Mortimer bows from the floor. \"The guild is yours, Guildmaster. Rob the Cinderfall Steps for me.\""]),
    }),
 "astrologist": dict(
    zone="The Starfall Heights", strata="TIDE", strata2="DAWN", skin="start_astral",
    layout=(340, 270, 9, 152, 110, True), lessons=["GUARD", "WARD", "RITUAL", "SWIFT", "HAND"],
    theme=r"star|comet|gravity|eclipse|lunar|orbit|heaven|astral|void|celestial|constellation|falling|moon|oracle|omen|inver|gazer|seer|starborn",
    foes={
     "r1_n01": ("Star-Reader Ulla", "astrologist",
                ["In the observatory-spires of Starfall Reach, the first lesson is always the sky.",
                 "\"The stars said you'd come tonight. Let's see if they also said you'd win.\""],
                ["Ulla marks a new point on her star chart. \"You're on the map now.\""]),
     "r1_n02": ("The Orrery Sentinel", "astrologist",
                ["A great brass orrery has woken and set itself to guard the stair.",
                 "Its planets wheel faster as you approach."],
                ["The planets slow, then stop. The sentinel ticks once, and is still."]),
     "r1_n04": ("Eclipse Cultist Morwen", "necromancer",
                ["Robed figures chant on the dark side of the peak, calling for the sun to go out.",
                 "\"When the eclipse comes, every star you lean on goes dark.\""],
                ["The chanting stops. Overhead, the stars come back one by one."]),
     "r1_n05": ("The Comet-Touched Hermit", "astrologist",
                ["An old hermit lives in the crater where a star fell. The light never quite left him.",
                 "\"It spoke to me as it fell. It said you'd be trouble.\""],
                ["The hermit chuckles. \"It was right, you know. It's always right.\""]),
     "r1_n06": ("Lunar Acolyte Sera", "battlemage",
                ["An acolyte of the moon practises her rites on the highest terrace.",
                 "\"The moon pulls the tide. Let's see what it pulls out of you.\""],
                ["Sera's rite falls apart in silver dust. \"Next full moon,\" she promises."]),
     "r1_n09": ("The Starborn Colossus", "astrologist",
                ["Something made of night and constellations climbs over the ridge.",
                 "It fell from the sky long ago. It wants to go home."],
                ["The colossus breaks apart into stars, and they rise — home at last."]),
     "r1_n10": ("Void-Gazer Teodric", "astrologist",
                ["A scholar has stared into the dark between the stars for far too long.",
                 "\"There is nothing there, you know. Nothing at all. It's beautiful.\""],
                ["Teodric blinks, as if waking. \"Oh. You're still here. How... bright.\""]),
     "r1_n11": ("Warden Celestine of the High Spire", "astrologist",
                ["At the top of the highest spire, the Warden turns the great lens.",
                 "\"I chart every fate in the Reach. Yours ends here — or begins.\""],
                ["Celestine looks up from the lens. \"Begins, then. Come up to the dome.\""]),
     "r1_n12": ("Celestine, Who Turns the Heavens", "astrologist",
                ["The dome opens. The stars wheel overhead at her command.",
                 "\"The heavens obey me. Shall I bring them down on you?\""],
                ["The sky settles back into its old shape. \"The Heights are yours. Read the Cinderfall Steps in the stars — they burn.\""]),
    }),
 "warrior": dict(
    zone="The Emberhold Passes", strata="EMBER", strata2="DAWN", skin="start_ember",
    layout=(1220, 390, 180, 150, 115, True), lessons=["GUARD", "SWIFT", "PIERCE", "ARMOR", "EXALTED"],
    theme=r"forge|anvil|ember|cinder|flame|fire|blaze|furnace|lava|magma|war|berserk|blood|veteran|champion|titan|raider|scout|vanguard|pike|impaler|duelist|charge|banner|warlord|marshal|cleave|sergeant",
    foes={
     "r1_n01": ("Pit-Master Dunn", "warrior",
                ["Every warrior of Emberhold starts in the pit, under the forge-light.",
                 "\"Fists up, fresh meat. Hit me like you mean it.\""],
                ["Dunn spits and grins. \"You'll bleed fine. Off to the passes with you.\""]),
     "r1_n02": ("Shieldbearer Orla", "paladin",
                ["A shieldbearer holds the narrow pass, her tower shield planted in the rock.",
                 "\"You want through? Go through me.\""],
                ["Orla hefts her dented shield onto her back. \"Through me it is. Well fought.\""]),
     "r1_n04": ("Forge-Golem Mk. IV", "warrior",
                ["A smoking iron golem stomps out of an abandoned forge, its furnace roaring.",
                 "It was built for war. Nobody ever told it the war ended."],
                ["The furnace in its chest goes cold. The golem finally rests."]),
     "r1_n05": ("The Cinder Raiders", "rogue",
                ["Raiders on fire-hardened horses come screaming down the slope.",
                 "\"Grab what burns, leave the rest!\""],
                ["The raiders wheel away up the mountain, lighter by a few horses."]),
     "r1_n06": ("Spearwife Halla", "warrior",
                ["A veteran with a spear as long as two men waits at the ford.",
                 "\"Shields won't save you from this.\""],
                ["Halla plants her spear in the riverbed. \"Huh. Maybe they would.\""]),
     "r1_n09": ("The Ashen Berserker", "warrior",
                ["A berserker covered in ash and old scars roars from the top of the ridge.",
                 "There is nothing left in him but the fight."],
                ["The berserker falls silent at last. Ash drifts down like snow."]),
     "r1_n10": ("Lone Champion Brannoc", "warrior",
                ["A single champion stands on the bridge, alone by choice.",
                 "\"I fight best with no one beside me.\""],
                ["Brannoc sits down on the bridge. \"Maybe I'll try a friend next time.\""]),
     "r1_n11": ("Warden Ironhand", "warrior",
                ["At the gates of Emberhold, the Warden of the Forge keeps the last trial.",
                 "\"Every blade in this city was tested on my anvil. Now you.\""],
                ["Ironhand nods once. \"Tempered. Now come and face the forge itself.\""]),
     "r1_n12": ("Ironhand, the Forge-Tyrant", "warrior",
                ["The great forge bursts into flame behind him. The mountain shakes.",
                 "\"Emberhold was built on blood and iron. Add yours.\""],
                ["The forge-fire dies to embers. \"Emberhold salutes you,\" Ironhand rasps. \"Take the Cinderfall Steps — they're hotter still.\""]),
    }),
}


# Calibration (bot-vs-bot, each class's starter deck against its zone, 30 seeds per fight): "soft" makes a
# zone's decks prefer less stat-efficient cards, "vigor" trims a fight's enemy Vigor. Tuned so no fight is
# harder than the old Fallow Reach fight on the same node — and the early ones are clearly easier.
TUNING = {
    "battlemage": {"soft": 1.5, "vigor": {"r1_n02": -2, "r1_n04": -4, "r1_n10": -4}},
    "necromancer": {"soft": 0.75, "vigor": {"r1_n05": -2, "r1_n09": -4, "r1_n10": -2, "r1_n11": -6}},
    "paladin": {"soft": 4.5, "vigor": {"r1_n02": -2, "r1_n04": -6, "r1_n05": -6, "r1_n06": -4, "r1_n09": -6, "r1_n10": -6, "r1_n11": -8, "r1_n12": -8}, "cap": 5, "heavy": 4, "tier": -2},
    "druid": {"soft": 4.5, "vigor": {"r1_n02": -2, "r1_n04": -4, "r1_n05": -4, "r1_n06": -4, "r1_n09": -4, "r1_n10": -4, "r1_n11": -6, "r1_n12": -6}, "cap": 5, "heavy": 4, "tier": -1},
    "rogue": {"soft": 0.0, "vigor": {}},
    "astrologist": {"soft": 4.5, "vigor": {"r1_n02": -2, "r1_n04": -4, "r1_n05": -4, "r1_n09": -4, "r1_n10": -4}},
    "warrior": {"soft": 4.5, "vigor": {"r1_n02": -2, "r1_n04": -4, "r1_n05": -4, "r1_n10": -4}},
}


if os.environ.get("START_ZONE_TUNING"):     # calibration runs only
    TUNING = json.load(open(os.environ["START_ZONE_TUNING"]))


def efficiency(c):
    if c["type"] != "CREATURE":
        return 2.0
    return (c.get("attack", 0) + c.get("vigor", 0) + len(c.get("keywords", []))) / max(1, c["cost"])


def load_cards():
    cards = {}
    for f in sorted(glob.glob(str(ROOT / "client" / "content" / "cards" / "*.json"))):
        for c in json.load(open(f)):
            cards.setdefault(c["id"], c)
    art = ROOT / "client" / "content" / "art"
    return [c for c in cards.values()
            if c["type"] in ("CREATURE", "RITUAL") and (art / f"{c['id']}.webp").exists() and not c["id"].startswith("tut_")]


def kw(c):
    return {k.split(":")[0] for k in c.get("keywords", [])}


def rarity_ok(c, tier, used):
    r = c["rarity"]
    caps = {0: (0, 0), 1: (0, 0), 2: (4, 0), 3: (8, 1), 4: (10, 3), 5: (10, 5)}[tier]
    if r == "COMMON":
        return True
    if r == "UNCOMMON":
        return used["UNCOMMON"] < caps[0]
    if r == "RARE":
        return used["RARE"] < caps[1]
    return False


def build_deck(cards, z, tier, lesson, seed):
    rng = random.Random(seed)
    theme = re.compile(z["theme"])
    p, s = z["strata"], z["strata2"]

    def score(c):
        sc = 0
        if c["strata"] == p: sc += 10
        elif c["strata"] == s: sc += 4
        if theme.search(c["id"]): sc += 6
        return sc + rng.random() * 3 - z.get("soft", 0) * efficiency(c)

    pool = sorted([c for c in cards if c["strata"] in (p, s)], key=score, reverse=True)
    deck, used = [], {"UNCOMMON": 0, "RARE": 0}
    want_rituals = 12 if z["strata"] == "TIDE" else 8
    if lesson == "RITUAL":
        want_rituals += 3
    max_cost = 5 if tier <= 1 else z.get("cap", 9)

    def take(c):
        deck.append(c)
        if c["rarity"] in used:
            used[c["rarity"]] += 1

    # the lesson's keyword shows up for sure: 4 cards that carry it (on-theme first, then any stratum)
    if lesson in LESSONS and lesson not in ("RITUAL", "HAND", "ELITE", "WARDEN", "BOSS"):
        carriers = [c for c in pool if lesson in kw(c)] + \
                   sorted([c for c in cards if lesson in kw(c) and c["strata"] not in (p, s)], key=score, reverse=True)
        for c in carriers:
            if len([d for d in deck if lesson in kw(d)]) >= 4: break
            if c not in deck and c["cost"] <= max_cost and (rarity_ok(c, tier, used) or c["rarity"] == "COMMON"):
                take(c)
    # curve: cheap / middle / heavy slots
    heavy = z.get("heavy", 8)
    slots = {"lo": 12, "mid": 13, "hi": 5} if tier <= 1 else {"lo": 10 + (8 - heavy) // 2, "mid": 12 + (8 - heavy + 1) // 2, "hi": heavy}
    def band(c): return "lo" if c["cost"] <= 2 else ("mid" if c["cost"] <= 4 else "hi")
    for c in deck: slots[band(c)] -= 1
    rituals = sum(1 for c in deck if c["type"] == "RITUAL")
    for c in pool:
        if len(deck) >= 30: break
        if c in deck or c["cost"] > max_cost or not rarity_ok(c, tier, used): continue
        if slots[band(c)] <= 0: continue
        if c["type"] == "RITUAL" and rituals >= want_rituals: continue
        if c["type"] == "CREATURE" and (30 - len(deck)) <= (want_rituals - rituals): continue
        take(c); slots[band(c)] -= 1
        if c["type"] == "RITUAL": rituals += 1
    # top up with anything legal (curve first loosened, then strata)
    for c in pool + sorted(cards, key=score, reverse=True):
        if len(deck) >= 30: break
        if c not in deck and c["cost"] <= max_cost and rarity_ok(c, tier, used):
            take(c)
    assert len(deck) == 30, (z["zone"], tier, len(deck))
    deck.sort(key=lambda c: (c["cost"], c["id"]))
    return [c["id"] for c in deck]


def build_drops(cards, z, tier, deck, seed, signature):
    rng = random.Random(seed)
    theme = re.compile(z["theme"])
    p, s = z["strata"], z["strata2"]
    def pick(rarity, n):
        c = [x for x in cards if x["rarity"] == rarity and x["strata"] in (p, s)]
        c.sort(key=lambda x: ((x["strata"] == p) * 4 + bool(theme.search(x["id"])) * 3 + rng.random() * 4), reverse=True)
        return [x["id"] for x in c[:n]]
    drops = []
    if signature:
        drops.append({"card_id": signature, "rate": 1.0})
    for cid in pick("COMMON", 12): drops.append({"card_id": cid, "rate": 0.4})
    for cid in pick("UNCOMMON", 6 + (tier >= 3) * 2): drops.append({"card_id": cid, "rate": 0.25})
    for cid in pick("RARE", 2 + (tier >= 3) * 2):
        if cid != signature: drops.append({"card_id": cid, "rate": 0.1})
    return drops


SHORT = {"Forge-Golem Mk. IV": "Forge-Golem", "The Comet-Touched Hermit": "The Hermit", "The Starborn Colossus": "Star Colossus"}


def short_name(name):
    """The map label: "Warden Seraphine of the Tide-Gate" → "Warden Seraphine" (the full name is on the fight card)."""
    if name in SHORT:
        return SHORT[name]
    n = name.split(", ")[0]
    n = re.split(r" of (the )?", n)[0]
    if len(n) > 16:
        n = " ".join(n.split()[-2:])
    if len(n) > 16:
        n = n.split()[-1]
    return n


def layout(spec):
    """FABLE-069: the path spans the whole map plate (Trikzos: the first zones were squeezed into a corner).
    spec = (start x, start y, heading in degrees, step along the path, step across it, mirror)."""
    x0, y0, angle, along, across, mirror = spec
    shape = list(SHAPE)
    shape[10], shape[11] = (0.05, 5.1), (1.05, 5.85)    # the Warden and the boss step off the line so labels don't touch
    r = math.radians(angle)
    dx, dy = math.cos(r), math.sin(r)
    out = {}
    for n, (u, v) in zip(NODES, shape):
        if mirror: u = -u
        out[n] = [round(x0 + v * along * dx - u * across * dy), round(y0 + v * along * dy + u * across * dx)]
    return out

def main():
    cards = load_cards()
    by_id = {c["id"]: c for c in cards}
    wayfarer = next(e for e in json.load(open(ROOT / "content" / "encounters" / "region_01_early.json"))["encounters"]
                    if e["id"] == "r1_duel_wayfarer")
    zones_out, encounters = {}, []
    for ci, (cls, z) in enumerate(ZONES.items()):
        tune = TUNING.get(cls, {})
        z = dict(z, soft=tune.get("soft", 0.0), cap=tune.get("cap", 9), heavy=tune.get("heavy", 8))
        lessons = dict(zip(["r1_n02", "r1_n05", "r1_n06", "r1_n09", "r1_n10"], z["lessons"]))
        lessons.update({"r1_n04": "ELITE", "r1_n11": "WARDEN", "r1_n12": "BOSS"})
        # the n09 elite teaches its keyword, the n04 elite teaches what an elite is
        node_enc = {}
        signatures_used = set()
        for node in FIGHTS:
            tier, vigor, shard, dig, frag, modifier = NODE_RULES[node]
            vigor += tune.get("vigor", {}).get(node, 0)
            name, opp, intro, outro = z["foes"][node]
            eid = f"sz_{cls}_{node[3:]}"
            seed = (ci + 1) * 1000 + int(node[-2:])
            lesson = lessons.get(node)
            if node == "r1_n01":
                deck = list(wayfarer["deck"])            # the guided first duel scripts against exactly this deck
            else:
                deck = build_deck(cards, z, max(1, tier + tune.get("tier", 0)), lesson if lesson not in ("ELITE", "WARDEN", "BOSS") else None, seed)
            signature = None
            if node in ("r1_n11", "r1_n12"):
                rares = [c for c in deck if by_id.get(c, {}).get("rarity") == "RARE" and c not in signatures_used]
                rares.sort(key=lambda c: (by_id[c]["strata"] != z["strata"], -by_id[c].get("power_score", 0), c))
                if not rares:   # a softened deck may hold no rare: the signature then comes from the zone's own stratum
                    rares = sorted((c["id"] for c in cards if c["rarity"] == "RARE" and c["strata"] == z["strata"] and c["id"] not in signatures_used),
                                   key=lambda c: (not re.search(z["theme"], c), c))
                if rares:
                    signature = rares[0]
                    signatures_used.add(signature)
            e = {
                "id": eid, "name": name, "short_name": short_name(name), "class": opp,
                "portrait": f"res://content/art/classes/{opp}_{'mf'[seed % 2]}.webp",
                "deck": deck,
                "dialogue_intro": intro, "dialogue_outro": outro,
                "shard_reward": shard, "dig_charge_reward": dig,
            }
            if frag:
                strata = z["strata"] if frag[0] == "P" else z["strata2"]
                e["fragment_reward"] = f"{strata.lower()}:{frag[1]}"
            if modifier:
                e["modifier"] = modifier
            if node == "r1_n01":
                e["is_tutorial"] = True
            if node == "r1_n12":
                e["opening_rule"] = "root_choked"
            if lesson:
                e["lesson"] = list(LESSONS[lesson])
            e["drops"] = build_drops(cards, z, tier, deck, seed + 7, signature)
            e["enemy_vigor"] = vigor
            encounters.append(e)
            node_enc[node] = eid
        zones_out[cls] = {
            "name": z["zone"], "strata": z["strata"], "strata2": z["strata2"], "board_skin": z["skin"],
            "positions": layout(z["layout"]), "encounters": node_enc,
        }
    zones_doc = {"_doc": "FABLE-068: each class's starting zone. Overlays region_01 (same node ids and unlocks). Generated by tools/gen_start_zones.py.",
                 "base_region": "region_01", "zones": zones_out}
    pack = {"_doc": "FABLE-068: the class starting zones' encounters. Generated by tools/gen_start_zones.py.", "encounters": encounters}
    for base in (ROOT / "content", ROOT / "client" / "content"):
        (base / "start_zones.json").write_text(json.dumps(zones_doc, indent=1, ensure_ascii=False) + "\n")
        # one encounter per line: small diffs, small file
        body = ",\n".join("  " + json.dumps(e, ensure_ascii=False) for e in encounters)
        (base / "encounters" / "start_zones.json").write_text(
            "{\n \"_doc\": " + json.dumps(pack["_doc"]) + ",\n \"encounters\": [\n" + body + "\n ]\n}\n")
    print(f"{len(zones_out)} zones, {len(encounters)} encounters")


if __name__ == "__main__":
    main()
