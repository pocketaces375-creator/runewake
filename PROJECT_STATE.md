CURRENT PHASE: P6
LAST COMPLETED TICKET: P6-10 (Pipeline orchestration + end-to-end ember_01 set) — commit edf5a36, 400/400 C# tests, 215/215 Python tests
IN PROGRESS: P6-11 (Stage-schema continuity + report hardening)
NEXT TICKET: P7-01 (Phase 7 — Ship: onboarding tutorial, first 3 duels teach lanes, Excavate, runes)

NOTE: All Phase 5 and Phase 6 tickets P6-01 through P6-10 are complete. P6-11 is the final Phase 6 ticket.

KNOWN TEST COUNTS (last confirmed):
  C#: 400/400
  Python: 215/215 (3 pre-existing test_generate.py failures appear resolved or merged into overall count)

KEY FILES FOR P6-11:
  pipeline/modules/orchestrate.py   (stage orchestrator — built in P6-10)
  pipeline/modules/publish.py       (stage 8 — check output format for pack_version/pack_hash field names)
  pipeline/tests/test_orchestrate.py (18 existing tests)

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking P6-11.

TECH DEBT:
  - 3 test_generate.py failures (may now be resolved — verify)
  - API key not inherited in subprocess environments (run_e2e.sh wrapper built in P6-10)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, no regression test)
  - Stage-schema discontinuity (being fixed in P6-11)

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete.

PHASE 7 (Ship) not yet started.

OPEN ITEMS: []