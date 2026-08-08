CURRENT PHASE: P7
LAST COMPLETED TICKET: P7-01b (Tutorial gap closure — all 8 steps auto-wired) — commit bb41f5b, 409/409 C# tests
IN PROGRESS: P7-02 (Supabase account + relic ledger sync)

KNOWN TEST COUNTS (last confirmed):
  C#: 409/409
  Python: 221/221

PHASE 7 REMAINING TICKETS:
  P7-02 — Supabase account + relic ledger sync (IN PROGRESS)
  P7-03 — Telemetry
  P7-04 — Settings/accessibility
  P7-05 — App Store and Play listings, privacy policy, age rating
  P7-06 — TestFlight/closed beta with 50+ players
  P7-07 — Crash reporting
  P7-08 — Launch

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-11 complete. Tutorial (P7-01/P7-01b) complete.

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking P7.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, no regression test)

OPEN ITEMS:
[
  {
    "id": "P7-02",
    "title": "Supabase account + relic ledger sync",
    "status": "in_progress"
  },
  {
    "id": "P7-03",
    "title": "Telemetry",
    "status": "open"
  },
  {
    "id": "P7-04",
    "title": "Settings/accessibility",
    "status": "open"
  },
  {
    "id": "P7-05",
    "title": "App Store and Play listings + privacy policy + age rating",
    "status": "open"
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