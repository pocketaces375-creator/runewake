CURRENT PHASE: P7
LAST COMPLETED TICKET: P6-11 (Stage-schema continuity + report hardening) — commit 48c404a, 400/400 C# tests, 221/221 Python tests
IN PROGRESS: P7-01 (Onboarding tutorial: lanes, Excavate, runes beats)

NOTE: All Phase 6 tickets are complete. Phase 7 (Ship) is now active.
The P4-05 item in OPEN_ITEMS was stale — P4-05 was confirmed closed at commit 51e0fbe.

KNOWN TEST COUNTS (last confirmed):
  C#: 400/400
  Python: 221/221 (3 pre-existing test_generate.py failures were resolved by P6-10/P6-11)

PHASE 7 REMAINING TICKETS:
  P7-01 — Onboarding tutorial (IN PROGRESS)
  P7-02 — Supabase account + relic ledger sync
  P7-03 — Telemetry
  P7-04 — Settings/accessibility
  P7-05 — App Store and Play listings, privacy policy, age rating
  P7-06 — TestFlight/closed beta with 50+ players
  P7-07 — Crash reporting
  P7-08 — Launch

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-8 complete with orchestration.

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking P7.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, no regression test)

OPEN ITEMS: []