CURRENT PHASE: P7
LAST COMPLETED TICKET: P7-CLEANUP (Dev menu removal) — commit 09f37c9, 463/463 C# tests green
IN PROGRESS: Fix 3 pre-existing pipeline/tests/test_generate.py failures

KNOWN TEST COUNTS (last confirmed):
  C#: 463/463
  Python: 221/221 (3 of these currently failing in test_generate.py)

PHASE 7 STATUS: All agent-executable feature tickets complete. One low-priority cleanup item in progress (Python test fixes). Ship-blocking dev menu removal done.

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-11 complete. Tutorial complete. Supabase sync complete. Telemetry + settings complete. Store listings complete. Crash reporting complete. Pre-launch validation complete. Dev menu removed.

REMAINING AGENT ITEMS:
  - [IN PROGRESS] Fix 3 pre-existing failures in pipeline/tests/test_generate.py (see TECH_DEBT.md for exact changes needed)

REMAINING HUMAN-ACTION ITEMS:
  P7-H1 — Install APK on physical device, play through full campaign loop, verify r1_n02 unlocks
  P7-H2 — TestFlight submission (iOS) + Play Store internal testing (Android)
  P7-Launch — App Store and Google Play public submission

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking launch.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, regression test still missing)
  - 3 pre-existing Python test_generate.py failures (IN PROGRESS — assertions only, no module logic changes needed)