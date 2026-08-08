CURRENT PHASE: P7
LAST COMPLETED TICKET: P7-03/P7-04 (Telemetry + settings/accessibility) — commit 1ae058d, 443/443 C# tests
IN PROGRESS: P7-05 (App Store and Play listings, privacy policy, age rating)

KNOWN TEST COUNTS (last confirmed):
  C#: 443/443
  Python: 221/221

PHASE 7 REMAINING TICKETS:
  P7-05 — App Store and Play listings, privacy policy, age rating (IN PROGRESS)
  P7-06 — TestFlight/closed beta with 50+ players
  P7-07 — Crash reporting
  P7-08 — Launch

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-11 complete. Tutorial complete. Supabase sync complete. Telemetry + settings complete.

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking P7.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, no regression test)

OPEN ITEMS:
[
  {
    "id": "P7-05",
    "title": "App Store and Play listings + privacy policy + age rating",
    "status": "in_progress"
  },
  {
    "id": "P7-06",
    "title": "TestFlight/closed beta with 50+ players",
    "status": "open"
  },
  {
    "id": "P7-07",
    "title": "Crash reporting",
    "status": "open"
  },
  {
    "id": "P7-08",
    "title": "Launch",
    "status": "open"
  }
]