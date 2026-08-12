CURRENT PHASE: P7
LAST COMPLETED TICKET: Art direction — card art fills face with text overlays, atmospheric title screen — APK SHA-256 6a92d0db6c1d203d22f45d450fea7a2ec0e7e005d383903b234864a5426e0699

KNOWN TEST COUNTS (last confirmed):
  C#: 463/463
  Python: 221/221 (all passing)

PHASE 7 STATUS: All agent-executable feature tickets complete. Python test fixes complete. Art integration complete. Art direction polish complete. Smoke test script — FINAL REMAINING AGENT ITEM.

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-11 complete. Tutorial complete. Supabase sync complete. Telemetry + settings complete. Store listings complete. Crash reporting complete. Dev menu removed. Art assets integrated (15 cards across all 5 strata). Card art fills face with overlays. Atmospheric title screen.

REMAINING AGENT ITEMS:
  - [FINAL] Write, run, and commit client/smoke_test.sh — exported Linux build starts without crashing, pixel validation passes

REMAINING HUMAN-ACTION ITEMS:
  P7-H1 — Install APK on physical device, play through full campaign loop, verify r1_n02 unlocks after r1_n01 clear
  P7-H2 — TestFlight submission (iOS) + Play Store internal testing (Android)
  P7-Launch — App Store and Google Play public submission

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking launch.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Combat design gap: attacking has no meaningful cost (post-launch, not blocking)

OPEN ITEMS:
[
  {
    "id": "SMOKE-TEST",
    "title": "client/smoke_test.sh: exported Linux build smoke test",
    "status": "in-progress"
  },
  {
    "id": "P7-H1",
    "title": "Device QA: install APK and play full campaign loop",
    "status": "human-action"
  },
  {
    "id": "P7-H2",
    "title": "TestFlight (iOS) + Play Store internal testing (Android)",
    "status": "human-action"
  },
  {
    "id": "P7-Launch",
    "title": "App Store and Google Play public submission",
    "status": "human-action"
  }
]
