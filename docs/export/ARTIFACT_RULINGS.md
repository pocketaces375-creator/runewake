# RUNEWAKE — Artifact Rulings v1.0 (timing & edge cases — the test-suite contract)

Author: Claude · Audience: Hermes (encode these as tests), Trikzos (veto anything that reads wrong)
Authority: where card text in FIELD_EFFECT_SPEC §5 / ARTIFACT_CLASSES §§4–7 is ambiguous, THIS file is the answer. Tests assert these rulings verbatim. If the engine as built contradicts a ruling, the ruling wins; log CONFLICT if the fix is expensive.

## General rulings (apply to all Artifacts)
- G1 — Trigger ordering. Multiple Artifact triggers on one event: active player's Artifacts first, then non-active player's; within a player, slot order (left, then right). Through the normal TriggerBus queue, never interleaved mid-effect.
- G2 — End-of-turn stacking. All "at end of turn" Artifact effects resolve BEFORE "until end of turn" effects expire (an end-of-turn heal still sees Icon's +1 attack buffs, which then expire normally).
- G3 — Suppression scope. While Suppressed: passive off, triggers don't fire, Charges frozen (no gain, no spend, no loss). Continuous passives switch off immediately; permanent buffs the Artifact granted earlier remain — they belong to the creature now.
- G4 — Suppression duration. Counted in the suppressed player's turns: "1 turn" = until the end of that player's next turn. Re-applying from the same source id refreshes; a different source extends.
- G5 — Turn-scoped counters (attacks/spells/deaths this turn) reset at the START of every turn, both players tracked independently; Artifact conditions read the OWNER's counter unless the text names the opponent.
- G6 — Mirror matches. Identical passives (same card id) never stack; all triggers fire independently. Each player's Charges/marks are their own.
- G7 — "Creature died" = left play to any death, either side, any turn, unless text says friendly/enemy.
- G8 — Charges are per-card, cap 3, visible to both players. Charge-full effects fire immediately on the 3rd Charge unless the card says "at end of turn".

## Per-card rulings
### Warrior
- R1 — Ancestral Blade: arms when 3+ friendly creatures attack in one turn; lasts until the start of your next turn; protects against the FIRST enemy spell/ability (not combat damage) that would reduce a friendly creature below 1 vigor — set to 1 instead. One use, then disarms. Clamp, not prevention (damage triggers still fire).
- R2 — Bulwark passive: +0/+1 applies at end of your turn to each friendly creature that did not attack this turn; expires at the start of your next turn. Creatures played this turn count as "did not attack".
- R3 — Bulwark trigger "no attackers": true iff zero friendly creatures attacked during your most recent completed turn. Prevents the first 2 combat damage to the first friendly creature attacked each enemy turn.
### Battle Mage
- R4 — Warden's Focus spend: Charges auto-spend on the next friendly spell with ≥1 creature target; bonus to the FIRST creature target only; damage spells +1 damage per Charge, heal spells +1 healing; a spell doing neither does NOT spend.
- R5 — Mantle passive "first attack against them each turn": resets at the start of EVERY turn (both players').
- R6 — Mantle trigger: each enemy creature attack on your character queues one 1-attunement spell discount; stacks; expires end of that turn; spells only.
### Thief
- R7 — Whisperfang "exactly one": evaluated at END of your turn; counts friendly creatures that declared attacks (attacked-and-died still counts). Zero ≠ one; draw at end of turn (G2 order).
- R8 — Whisperfang passive: stealth-strike to the first attack declaration each of your turns, decided at declaration.
- R9 — Duskfang charges: each friendly creature dealing damage to the enemy character = 1 Charge, max 1 per creature per turn. At 3: BOTH enemy Artifacts suppressed 1 turn (G4), immediately, then reset to 0.
- R10 — Twin daggers: same dagger twice = one passive (no stack), two independent triggers with separate Charge pools.
### Cleric
- R11 — Censer heal: start of your turn, 1 to "most wounded" = greatest missing vigor; tie → owner chooses (AI: highest cost). Before draw. No wounded creature = no heal.
- R12 — Censer charge: max 1/turn, gained at end of any turn where ≥1 friendly creature took combat damage and survived. Full-heal at 3 fires at end of turn (G2), then reset.
- R13 — Icon passive: EVERY friendly heal event grants +1 attack until end of turn — but only heals restoring ≥1 actual vigor (overheal excluded).
- R14 — Icon trigger: friendly creature death on any turn → heal your character 2.
### Ranger
- R15 — Prey marking: start of Ranger's turn, BEFORE all other turn-start effects and before draw. Highest attack; tie → longest in play. No enemies = no mark. Mark persists until your next turn start even if a bigger creature appears.
- R16 — Prey death: if Prey dies during the Ranger's turn (any cause), Bow draws 1 at the moment of death (max once/turn); NO re-mark until next turn.
- R17 — Quiver spillover: once per turn, when the 2nd friendly attack on Prey resolves; later attackers don't repeat it.
- R18 — Suppression vs Prey: Bow suppressed at turn start = no new mark; existing mark persists; mark state itself never removed by suppression.
### Necromancer
- R19 — Grimoire discount: while ≥1 creature died this turn (any side), each creature you play costs 1 less (all of them, not just first). Floor = engine's minimum-cost rule, else 0.
- R20 — Grimoire Revenant: summon resolves at end of whichever turn the 3rd Charge landed (can be opponent's turn), G2 ordering; needs board space — board full = summon lost, Charges still reset. Real 3/3 token, class-agnostic, no element tag.
- R21 — Phylactery armor: evaluated at damage-application time, attack damage only, compares creature counts in play right now.
- R22 — Phylactery drain: every enemy creature death, any turn, including self-sacrifice → heal your character 1.
### Runesmith
- R23 — Forgehammer forge: the FIRST creature entering play under your control on your turn gets +0/+1 permanent. NO cost threshold. Tokens count. Permanent survives suppression (G3).
- R24 — Hammer Charge: every friendly creature entering play on your turn = 1 Charge (cap 3), tokens included. No cost condition.
- R25 — Anvil trigger: end of YOUR turn, iff zero friendly attacks this turn AND ≥1 friendly creature AND partner Charges ≥1: spend ALL partner Charges, +1/+1 per Charge to highest-cost creature (tie → longest in play), permanent. No creature = nothing happens, Charges KEPT.
- R26 — Anvil passive: +1 attack to friendly creatures with any permanent stat buff from any source, checked continuously.

## Test-suite note
Every ruling = at least one test. Spec §10 checklist applies on top (zone integrity, suppression symmetry, N-slot generalization, AI never attacks Artifact slots). Naming: Ruling_R15_PreyTieBreaksOldest style.