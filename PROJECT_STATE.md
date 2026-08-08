CURRENT PHASE: P7
LAST COMPLETED TICKET: P7-01 (Onboarding tutorial) — commit 51b6296, 407/407 C# tests, 221/221 Python tests — NOTE: has 3 wiring gaps being closed by P7-01b patch
IN PROGRESS: P7-01b (Wire Excavate/Barrow and Rune step auto-advance — P7-01 gap closure)

KNOWN TEST COUNTS (last confirmed):
  C#: 407/407
  Python: 221/221

PHASE 7 REMAINING TICKETS:
  P7-01b — P7-01 gap closure: Excavate/Barrow auto-advance + Rune step wiring (IN PROGRESS)
  P7-02 — Supabase account + relic ledger sync
  P7-03 — Telemetry
  P7-04 — Settings/accessibility
  P7-05 — App Store and Play listings, privacy policy, age rating
  P7-06 — TestFlight/closed beta with 50+ players
  P7-07 — Crash reporting
  P7-08 — Launch

CAMPAIGN STATUS: Complete through P5-06. Region 1 full content in place. Lost Relic minting working. Save/load persistence complete. Pipeline stages 1-11 complete.

OPEN QUESTIONS: Q1 (HOLLOW moderation) — awaiting human decision, not blocking P7.

TECH DEBT:
  - API key not inherited in subprocess environments (run_e2e.sh wrapper exists)
  - Pacing values provisional until art lands
  - Exported build filesystem path I/O (fixed in P3-02, no regression test)

OPEN ITEMS:
[
  {
    "id": "P7-01b",
    "title": "P7-01 gap closure: Excavate/Barrow and Rune step auto-advance wiring",
    "status": "in_progress"
  },
  {
    "id": "P7-02",
    "title": "Supabase account + relic ledger sync",
    "status": "open"
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