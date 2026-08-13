# Supabase Schema — Relic Ledger (P7-02)

## Tables

### profiles
| Column | Type | Notes |
|---|---|---|
| id | uuid | PK, matches auth.users.id |
| display_name | text | | 
| created_at | timestamptz | |
| last_sync_at | timestamptz | |

### relics
| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| profile_id | uuid | FK → profiles.id |
| relic_id | text | Game-side relic identifier |
| stratum | text | DAWN, EMBER, HOLLOW, TIDE, VERDANT |
| minted_at | timestamptz | |
| tx_hash | text | Blockchain proof (future) |

### match_records
| Column | Type | Notes |
|---|---|---|
| id | uuid | PK |
| profile_id | uuid | FK → profiles.id |
| content_version | text | ensures replay safety |
| seed | int | RNG seed |
| actions | jsonb | Full action log |
| result | text | win/loss/draw |
| played_at | timestamptz | |

## Local-first
All data written to local SQLite first. Synced to Supabase when connection
available. Conflicts resolved by "last-write-wins" on `updated_at`.