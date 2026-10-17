# Audit: what the game shows vs. what we agreed (FABLE-064)

A walk through the UI and the engine against the current design. **Fixed** items landed in FABLE-064;
**Open** items are scheduled.

## Fixed in FABLE-064

| Where | What was wrong | Now |
|---|---|---|
| Duel | Damage numbers were net Vigor diffs per lane, coloured by the *victim's* stratum, never tied to a source; one End Turn with burns, triggers and deaths collapsed into one number. | The engine records every hit, heal, death, cast, trigger and keyword (`GameState.Events`); the duel plays them in order, coloured by the attacker, with the cause named. |
| Duel | Rituals had no visual at all; enemy rituals had no sound either. | Every ritual — yours and the enemy's — is shown large mid-board with its effect glyphs, then a COUNTERED stamp when a Sigil stops it. |
| Duel | Relic / artifact / creature abilities fired silently. The artifact "trigger flash" never ran because `HasTriggeredThisTurn` was never set. | Three renditions: rune bloom over a creature, plate flash + arc for an artifact, full-screen veil for a Relic waking. `HasTriggeredThisTurn` is set when an artifact fires and reset at its owner's next turn (its SPENT look is real now). |
| Duel | Guard, Ward, Dodge, Armor, Venom, Pierce, Swift, Exalted, Unearth, shields did their job with no cue. | Named callouts with glyphs at the lane. |
| Duel | Death was a crumble with no reason. | A skull rises with the cause (VENOM, BURNED, DESTROYED, TRIBUTE, FRAGILE…). |
| Duel | A heal to a player pulsed the screen edge as if it were a hit. | Heals show +N at the crest; only damage pulses. |
| Duel | No place explained the symbols. | Symbol Key: Settings → "Symbol key", and the **?** button under Concede in any fight. |
| Arena | Two Region-1 elites advertised modifiers ("All creatures have +1 attack", "Starts with a GUARD token") that the engine never applied. | Removed. A modifier is only shown when the engine really runs it. |
| Difficulty | Every authored foe had 25 Vigor, generated foes 25+; starter decks lost often in zone 1. | Gentle ramp: Region 1 foes 18 / 20 / 22 (boss 22), everything after 22; generated foes start at 22 and their decks aim 15% leaner. The Tower is untouched. |
| Artifact plate | Every trigger flash logged "no meta flash_tween". | Fixed. |

## Open (scheduled)

| Where | Gap | Plan |
|---|---|---|
| Choose Your Path | Says "Each path begins in its own town, with its own tale", but every class starts in The Fallow Reach; the class town is text only. | **FABLE-065**: seven starting zones, one per class, themed foes and names; the first zone teaches one thing per place. |
| Tutorial | Only the Wayfarer duel teaches; the 13-popup chain and `tutorial_steps.json` are dead code. | FABLE-065 replaces them with the lesson zone. |
| World | `atlas.json` opens the endless world after Region 1 (a testing value; the comment says Region 4 at launch). | Decide with FABLE-065: the class zone leads into the shared chain, the Crossroads opens after its Warden. |
| Store | Target API 34 / APK; Play needs API 36 + AAB + 16 KB pages; billing plugin breaks the Godot 4.3 export. | **FABLE-066**: Godot upgrade, AAB, billing. |
| Packs | Refunds are not clawed back. | Voided Purchases API, after store launch. |
| Co-op | No team drop system yet. | Later, per Trikzos. |
