CURRENT PHASE: P6
LAST COMPLETED TICKET: P4-05 (Deck builder screen with collection filtering) — commit 51e0fbe, 400/400 C# tests
NEXT TICKET: P6-10 (Pipeline orchestration + one 60-card set end to end)
AFTER THAT: P6-11 (Stage-schema continuity + report hardening)

NOTE: P5-01 through P5-06 and P6-01 through P6-09 were all completed in prior sessions (see AGENT_LOG.md). The open items list was stale — P4-05 was the last outstanding item and is now closed.

KNOWN TEST COUNTS (last confirmed):
  C#: 400/400
  Python: ~168/171 (3 pre-existing test_generate.py failures documented in TECH_DEBT.md)

KEY FILES FOR P6-10:
  pipeline/modules/generate.py      (stage 1 — already built)
  pipeline/modules/validate.py      (stage 2 — already built)
  pipeline/modules/score.py         (stage 3 — already built)
  pipeline/modules/simulate.py      (stage 4 — already built)
  pipeline/modules/dedupe_moderate.py (stage 5+6 — already built)
  pipeline/modules/art.py           (stage 7 — already built, supports --skip-api)
  pipeline/modules/publish.py       (stage 8 — already built)
  pipeline/seeds/ember_01.json      (60-card EMBER seed)
  pipeline/config.yaml

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking P6-10.

TECH DEBT:
  - 3 test_generate.py failures (pre-existing, tracked)
  - API key not inherited in subprocess environments (run_e2e.sh wrapper is the fix, being built in P6-10)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, no regression test)

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete.

PHASE 7 (Ship) not yet started.