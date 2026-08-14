# HERMES_STATUS.md

## Completed Tasks

|**TASK-DSL-6 (2026-08-14):** Partner-slot mechanics — PARTNER_CHARGES_GTE condition, FORGE op with spend_from PARTNER_SLOT (all charges, +1/+1 per charge, HIGHEST_COST target, tiebreak OLDEST_IN_PLAY, charges kept if no creature — R25). 19 new unit tests passing. All 587 legacy tests green.

**TASK-DSL-7 (2026-08-14):** Keyword handlers — ANCESTRAL_SHIELD (first enemy spell each turn that would drop an ally below 1 vigor clamps it to 1 — clamp not prevention, damage triggers still fire, one use, until your next turn — R1) and STEALTH_STRIKE (no counter-damage for that attack, decided at declaration — R8). Added field `AncestralShieldUsedThisTurn` on CardInstance, `TryAncestralShieldClamp` and `ResetAncestralShields` in KeywordHandlers, wired in EffectExecutor.ApplyDamage and DuelEngine turn-start reset. STEALTH_STRIKE skips defender counter-damage in DuelEngine.ApplyAttack. 7 new unit tests passing. All 594 tests green.- TEMPO-247: 24/7 tempo (budget 48), no-progress breaker added, cool-down 15 min, cron 15 min, PID-lock first act verified. Manual run: parsed fine, hit TASK-T1 sticky block. 8 brakes intact.
- FINISH-247: 24/7 tempo (budget 48), heartbeat hourly, T1 split into T1a+T1b, block cleared. Manual run: TASK-T1a in progress. 9 brakes intact.
