# Runewake — Artifact Classes v1.0

**Author:** Claude (design lead) · **Date:** 2026-08-12
**Status:** ACTIVE — Engine work is GO per Trikzos confirmation

---

## Class Roster (7 classes, 14 Artifacts)

### 1. Warrior (Sword + Shield)
See FIELD_EFFECT_SPEC.md §5.

### 2. Battle Mage (Wand + Aura)
See FIELD_EFFECT_SPEC.md §5.

### 3. Thief (Dagger + Dagger)
See FIELD_EFFECT_SPEC.md §5. Twin rule: identical daggers = trigger fires twice, passive does not stack.

### 4. Cleric (Dawnlit Censer + Icon of the Unbroken)

**Dawnlit Censer.** *Passive:* Your healing effects grant +1 attack to the healed creature until end of turn. *Trigger:* WHEN you heal a creature that has already attacked this turn: gain 1 Charge (max 3). Spend all Charges automatically when you cast a spell that heals — it heals +1 per Charge spent.

**Icon of the Unbroken.** *Passive:* Your creatures have +0/+1 while at full health. *Trigger:* WHEN a friendly creature would be destroyed while at full health: it survives with 1 health instead (once per turn).

**Design note:** Makes heal-timing the core decision. Heal before combat = buff the attack. Heal after combat = save the survivor. This is the first class where holding a spell for the right moment is built into the Artifact pair, not the spells themselves.

### 5. Ranger (Heartwood Bow + Quiver of Whispers)

**Heartwood Bow.** *Passive:* At the start of your turn, mark the enemy creature with the highest attack as **Prey** (one Prey marker exists at a time; new mark replaces old). *Trigger:* WHEN a friendly creature attacks a Prey target: it gains +1 attack for that attack.

**Quiver of Whispers.** *Passive:* Your creatures have +1 attack while attacking a Prey target. *Trigger:* WHEN Prey is destroyed: draw a card.

**Prey marker:** A new reusable engine primitive — a single mutable reference on each `PlayerState` tracking "which enemy creature is currently marked as Prey for this player." Not a keyword on the creature card. Cleared when the marked creature leaves play or is replaced by a new mark. This is the only new engine primitive introduced for the entire 14-Artifact roster.

**Design note:** Each turn you choose: follow the Bow's mark (the biggest threat) for the +1/+1 and potential card, or make the smarter tactical attack that doesn't follow Prey. The opportunity cost IS the decision.

### 6. Necromancer (Grimoire of the Hollow Court + Phylactery of the Pale King)

**Grimoire of the Hollow Court.** *Passive:* Whenever a creature dies, you gain 1 Charge (max 3). *Trigger:* WHEN you reach 3 Charges: automatically revive the most recently deceased creature as a 1/1 Skeleton token under your control. Resets Charges to 0.

**Phylactery of the Pale King.** *Passive:* Your creatures have +0/+1 while your Barrow has at least 3 cards. *Trigger:* WHEN one of your creatures dies: Bury 1 card from your deck.

**Design note:** Profits from deaths on both sides. "Bad" trades (trading your 2/2 for their 3/3) become the plan. The Grímóire rewards frequent small deaths (swarm + trades); the Phylactery fattens your board when you're already ahead on attrition. Combo: trade aggressively → build charges → free tokens → barrow grows for +0/+1.

### 7. Runesmith (Forgehammer of Depth + Anvil of Unmaking)

**Forgehammer of Depth.** *Passive:* The first creature you summon each turn that costs 3 or more enters with +1/+1. *Trigger:* WHEN you summon a creature costing 5 or more: gain 1 Charge (max 3). Spend all Charges to give that creature +1/+1 per Charge spent (in addition to the passive).

**Anvil of Unmaking.** *Passive:* If you did not attack this turn, your next creature summoned next turn costs 1 less. *Trigger:* WHEN you end your turn without attacking: forge a permanent +0/+1 counter on each friendly creature that has not attacked this turn.

**Design note:** The most direct inversion of the always-attack problem. Holding back your entire board is a real strategy if you're Runesmith — you build board presence, reduce future costs, and let the opponent come to you. Every turn is a question: "do I want the forged buffs + discount, or do I need to swing now?"

---

## Class balance note (v1.1 updated)

With deck cards class-agnostic, Artifact pairs carry all class identity. Target power: each Artifact pair should generate roughly 1 rune of value per turn when played into its pattern. Triggers should be opt-in (opponent or owner can play around them), never a pure clock. If an Artifact would win the game with zero deck support, cut it.

Variant framework (§8) allows per-slot-pool alternates as post-launch content. At launch: one fixed pair per class. 7 classes × 2 Artifacts = 14 launch Artifacts.

---

## Artifact visual states (all 14):
- Ready (idle glow)
- Triggered (fire animation ≤ 0.8s, non-blocking)
- Charged (pips or intensity scaling with Charge count)
- Suppressed (chained/frosted/dimmed — full-art state change, turn-counter pip)