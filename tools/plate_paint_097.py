# FABLE-097: the four approved paintings with their lettering KEPT - only what changes in play is erased.
import os, subprocess, sys
from pathlib import Path
O, REF = Path(sys.argv[1]), Path(sys.argv[2])
KEEP = (" Everything not named here stays EXACTLY as it is - every other word, the frames, panels, strips, plaques, glow, corners, "
        "lighting and colours - identical composition, framing and proportions, lining up pixel for pixel with the original.")
JOBS = [
    ("SettingsScene", "v8_settings.png",
     "Edit this image: erase the four slider tracks with their knobs and the four percentage numbers beside them; erase the two toggle "
     "switches; erase all the lettering and the small arrow on the 'Card style: Shadow & rim' strip and on the 'Title music: The Gods of "
     "Old Answer' strip, leaving those two strips as blank parchment; erase any version line. Keep every other word exactly as it is: "
     "SETTINGS, Master, Music, Effects, Ambience, Mute all sound, Reduce motion, Symbol key - what every sign in a fight means, Replay the "
     "tutorial, Replay the intro, Audio credits, Save check - is my progress safe?, Reset all progress, and the small arrows beside them." + KEEP),
    ("RewardsScene", "v8_daily_rewards.png",
     "Edit this image: erase the line 'DAY 1 OF 7 IS WAITING'; erase the words on the six bronze buttons ('Claim Day 1', '1 to go', "
     "'3 to go', '5 to go', '3 to go', 'Open Chest'), leaving the blank plaques with their red wax seals; erase '0 PLAYED · 0 WON TODAY'; "
     "erase the four small counters '0 / 1', '0 / 3', '0 / 5', '0 / 3' under the progress bars but keep the dark bar tracks; erase the "
     "sentence 'Claim everything else today to open it (0 of 5)'; erase '0 Shards' in the top right corner and the line '1 reward ready "
     "to claim · New daily rewards in 12h 18m' at the bottom. Make the first runestone (DAY 1) an unlit grey stone exactly like the other "
     "six - no warm gold glow around it - with its lettering 'DAY 1', '50', 'Shards' in the same glowing cyan as the others. Keep every "
     "other word: COME BACK EACH DAY, DAILY REWARDS, DAILY LOGIN, Missed a day? Your week waits - it never resets., the seven stones' "
     "DAY labels and rewards, TODAY'S MATCHES, Any duel counts - campaign or online., PLAY 1 MATCH / 25 Shards, PLAY 3 MATCHES / 120 "
     "Shards, PLAY 5 MATCHES / 75 Shards, WIN 3 MATCHES / an Uncommon card + 90 Shards, DAILY CHEST, 190 SHARDS, Back." + KEEP),
    ("DeckBuilderScene", "v8_deck_forge.png",
     "Edit this image: erase every playing card and card picture - the large card on the left, the twelve cards in the centre display "
     "case together with the captions under them ('In your deck', 'Owned ×1'...), the thirty small cards in the tray at the bottom, and "
     "the two artifact pictures with their gilded frames - leaving plain wood where they were; erase the deck name 'DROWNED ...', "
     "'THE DECK YOU PLAY', '30 / 30  ready to play' and the green bar under it, the numbers '2.7' and '27 / 3 / 0' (keep the captions "
     "AVG COST and the one beside it, but change 'KIT' to 'RIT' in it), the bars and numbers of the curve chart (keep the 0 1 2 3 4 5 6 7+ "
     "axis), the coloured faction bar and the line 'Verdant 7  Ember 8  Tide 10  Hollow 4', the four lines 'Card draw 6', 'Removal 4', "
     "'Guard 7', 'Pierce 6', the words '✓ Playing' on their plaque, 'TIDE · RITUAL · COST 1', 'ABYSSAL GAZE', 'Excavate 2' with its rules "
     "text and the quotation under it, 'In your deck' on the bottom-left plaque, '30 / 30' and 'Deck full' in the tray, and 'Search cards' "
     "in the search box; remove the teal gem behind 'All' so it reads as plain text like the tabs beside it. Keep every other word: DECK "
     "FORGE, All, Synergy, Verdant, Ember, Tide, Hollow, Dawn, Rename, AVG COST, CURVE, FACTIONS, WHAT IT DOES, Save deck, Back, My decks, "
     "New deck, YOUR DECK, ARTIFACTS, tap a card to take it out, MINIMIZE." + KEEP),
    ("ReliquaryScene", "v2_reliquary_codex.png",
     "Edit this image: repaint the row of tabs along the top edge of the pages as TEN tabs of equal width, all in the same dark "
     "carved-wood style with cream lettering and none of them highlighted, reading left to right: Cards, Relics, Verdant, Ember, Tide, "
     "Hollow, Dawn, Everything, Found, Missing (no coloured squares before the names); erase every card on the pages together with their "
     "small numbers, 'NEW' tags and 'One copy' captions, leaving blank parchment; erase '391 / 391 runes found' and the bar under it on "
     "the ribbon (keep the ribbon); erase '0 Shards' on the middle plaque at the foot (keep the plaque). Keep 'THE CODEX OF RUNES', "
     "'RELIQUARY', 'Back' and 'Shop' exactly as they are." + KEEP),
]
e = dict(os.environ, SPEND_TAG="fable-097")
for l in (Path.home() / ".hermes/.env").read_text().splitlines():
    if l.startswith("OPENROUTER_API_KEY="): e.setdefault("OPENROUTER_API_KEY", l.split("=", 1)[1].strip().strip("'\""))
for key, ref, prompt in JOBS:
    out = O / f"{key}_words.png"; r = REF / ref
    if not r.exists(): print(f"FAILED {key} ? no reference {ref}", flush=True); continue
    q = subprocess.run([sys.executable, "/home/fictive/runewake/pipeline/gen_image_any.py", prompt, str(out), "--model", "google/gemini-3-pro-image",
                        "--aspect", "16:9", "--ref", str(r)], env=e, capture_output=True, text=True, timeout=480)
    c = next((x.split()[1] for x in q.stdout.splitlines() if x.startswith("COST ")), "?")
    print(f"IMAGE {key} {c}" if q.returncode == 0 and out.exists() else f"FAILED {key} {c} {q.stderr[-100:]}".replace("\n", " "), flush=True)
