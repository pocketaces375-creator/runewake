CURRENT STATE:
CURRENT PHASE: P7
LAST COMPLETED TICKET: P7-08 (Pre-launch validation pass) — APK SHA-256 e080306ec8770b3e98b516e229c84068af9791591be46c8d1170f946d67255fe, 443/443 C# tests
IN PROGRESS: Dev menu removal (ship-blocker)

KNOWN TEST COUNTS (last confirmed):
  C#: 443/443
  Python: 221/221

PHASE 7 STATUS: All agent-executable feature tickets complete. One ship-blocking cleanup item in progress (dev menu removal).

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-11 complete. Tutorial complete. Supabase sync complete. Telemetry + settings complete. Store listings complete. Crash reporting complete. Pre-launch validation complete.

REMAINING AGENT ITEMS:
  - [IN PROGRESS] Remove DevMenu.cs, DEV button from Main.cs, DeleteSave() from SaveManager.cs
  - Fix 3 pre-existing failures in pipeline/tests/test_generate.py (medium priority, not blocking store submission)

REMAINING HUMAN-ACTION ITEMS:
  P7-H1 — Install APK on physical device, play through full campaign loop, verify r1_n02 unlocks
  P7-H2 — TestFlight submission (iOS) + Play Store internal testing (Android)
  P7-Launch — App Store and Google Play public submission

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking launch.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, regression test still missing)
  - 3 pre-existing Python test_generate.py failures (see TECH_DEBT.md for details)
