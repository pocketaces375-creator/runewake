CURRENT STATE:
CURRENT PHASE: P7
LAST COMPLETED TICKET: P7-08 (Pre-launch validation pass) — APK SHA-256 e080306ec8770b3e98b516e229c84068af9791591be46c8d1170f946d67255fe, 443/443 C# tests
IN PROGRESS: none

KNOWN TEST COUNTS (last confirmed):
  C#: 443/443
  Python: 221/221

PHASE 7 STATUS: All agent-executable tickets complete.

REMAINING HUMAN-ACTION ITEMS:
  P7-06 — Install APK on physical device, play through full campaign loop, verify r1_n02 unlocks
  P7-06 — TestFlight submission (iOS) + Play Store internal testing (Android)
  P7-Launch — App Store and Google Play public submission

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-11 complete. Tutorial complete. Supabase sync complete. Telemetry + settings complete. Store listings complete. Crash reporting complete. Pre-launch validation complete.

KNOWN BUGS FIXED IN P7-08:
  - SettingsScene.tscn referenced res://client/scripts/settings/SettingsScene.cs (wrong exported path) — fixed to res://scripts/settings/SettingsScene.cs
  - MapScene.cs settings button scene path updated to match

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking launch.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, regression test still missing)

OPEN ITEMS:
[
  {
    "id": "P7-06",
    "title": "TestFlight/closed beta with 50+ players",
    "status": "open — requires human action"
  },
  {
    "id": "P7-Launch",
    "title": "App Store and Play Store submission",
    "status": "open — requires human action"
  }
]
