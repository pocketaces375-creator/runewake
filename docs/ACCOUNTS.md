# Accounts & Cloud Save — how it works (FABLE-018)

Runewake's backend is **Supabase** (hosted Postgres + Auth + REST). The client
talks to it with plain `HttpClient`; there is no SDK. Everything below is
offline-first: no config, no network, or a dashboard toggle off changes nothing
about play — it changes one line on the Account panel.

## The player's experience

1. **First launch.** The game signs in **anonymously** (`POST /auth/v1/signup`,
   empty body). No email, no password, no prompt. The player is now a real
   `auth.users` row with a JWT, and the title screen's top-right chip reads
   `Guest 3F2A · Backed up`.
2. **Playing.** Every local save (`SaveManager.Save()`) marks the install
   dirty; ~6 s after the last save in a burst, the whole progression is
   upserted to `player_saves` as one jsonb blob.
3. **Linking.** Account panel → *Link an email* → 6-digit code → done. The
   **user id does not change**, so nothing about the save moves; it just
   becomes recoverable. The chip now shows the email.
4. **New phone.** Account panel → *Sign in on another phone* → email → code.
   This phone adopts that account's cloud save. The guest session it had is
   archived to `user://supabase_session.previous.json`, never deleted — a
   guest's save is only findable through its user id.
5. **Reinstall.** Fresh install, no session, cloud has a save → the save is
   pulled silently on first launch (only if there is no local progress yet).
6. **Conflict.** If BOTH this phone and the cloud moved since they last
   spoke, nobody guesses. The panel opens with two cards — *This phone* /
   *Cloud*, each summarised (level, nodes, shards, relics, when, which
   device) — and the player picks. The other is replaced.

## The merge rule (`CloudSaveSync.Decide`, pure, tested)

| cloud row | this install ever synced? | local saved since? | decision |
|---|---|---|---|
| none | — | no progress | NoOp |
| none | — | has progress | **PushLocal** |
| exists | never | no local progress / not dirty | **PullCloud** (reinstall) |
| exists | never | dirty | **Conflict** |
| exists | yes | cloud newer & local dirty | **Conflict** |
| exists | yes | cloud newer | **PullCloud** |
| exists | yes | local dirty | **PushLocalNewer** |
| exists | yes | neither | NoOp |

"Cloud newer" means the server's `updated_at` is more than 1 s after the
`updated_at` we recorded at our last successful push or pull. The server
stamps `updated_at` with a trigger; the client's value is ignored.

## The schema (`supabase/schema.sql`)

| table | key | what | who can do what |
|---|---|---|---|
| `profiles` | `id` = auth user | display name, created/last seen | read/update own; created by trigger |
| `player_saves` | `user_id` | `save_json` jsonb = `CloudSaveBundle`, `save_version`, `device_id`, `app_version`, `updated_at` | read/insert/update own |
| `relic_instances` | `relic_instance_id` | the Lost Relic ledger, owned by `user_id` | read/insert/update own |
| `crash_reports` | `id` | append-only | insert by anyone, read by nobody |

Every table has Row Level Security on. **There is no service-role key
anywhere in the client.** The anon key shipped in the APK identifies the
project; RLS is what protects the data. `tools/supabase_schema_check.sh`
proves every policy locally; `tools/supabase_smoke.py` proves them on the
live project.

## The wire format (`CloudSaveBundle` → `ProgressionSnapshot`)

```
{ "v": 1,
  "profiles_json": "<raw user://profiles.json>",
  "decks_json":    "<raw user://decks.json>",
  "slots": { "0": { "v": 1, "shards": …, "cleared_nodes": […], "relics": […], … },
             "1": { … } } }
```

`ProgressionSnapshot` is a deliberately dumb, all-settable copy of
`ProgressionState`, because (a) the state's collections are get-only and a
serializer cannot fill them without tricks, and (b) it is a **format** — once
uploaded it is a promise to every phone that will download it. Add a field to
`ProgressionState` → add it to the snapshot, or it silently does not survive
a reinstall. `AccountsTests.ProgressionSnapshot_round_trip` checks that
field-by-field with reflection.

## Files on the phone (`user://`)

| file | what |
|---|---|
| `supabase_session.json` | access + refresh token, user id, email, is_anonymous |
| `supabase_session.previous.json` | the last session that was replaced (never deleted) |
| `cloud_sync.json` | `LastSyncedAt` (server stamp), `Dirty`, `LastError` |
| `data/account_id.txt` | device id (pre-existing file, reused) |

## Config

`client/supabase_config.json` — **gitignored**, baked into the APK on the
build machine. `client/supabase_config.example.json` is the template.
`user://supabase_config.json` overrides it for dev. Read by
`SyncManager.LoadConfig()`.

## Dashboard settings not expressible in SQL

- Authentication → Providers → **Anonymous sign-ins: ON**
- Authentication → Providers → **Email: ON**
- Authentication → Email Templates → *Magic Link* and *Change Email Address*
  must contain `{{ .Token }}` so the 6-digit code is in the mail (the client
  uses codes, not links — a link opens a browser, a code stays in the game)
- Authentication → Rate limits: the defaults are fine for a beta

## Code map

| file | role |
|---|---|
| `engine/Supabase/SupabaseAuth.cs` | signup (anonymous), refresh, link email + confirm, sign-in code + confirm, sign-out |
| `engine/Supabase/SupabaseSession.cs` | the persisted session |
| `engine/Supabase/CloudSaveSync.cs` | `player_saves` fetch/push + `Decide` |
| `engine/Supabase/ProgressionSnapshot.cs` | wire format + `CloudSaveBundle` |
| `engine/Supabase/RelicLedgerSync.cs` | relic ledger, now under the user's JWT (`AccessToken`) |
| `client/scripts/supabase/SyncManager.cs` | the brain: startup sync, debounced push, conflict, panel API |
| `client/scripts/AccountPanel.cs` | the UI |
| `client/scripts/Main.cs` | account chip, signal wiring, config load |
| `tests/Supabase/AccountsTests.cs` | 9 tests / 93 assertions, mock HTTP |
| `tools/supabase_smoke.py` | 21 live checks against the real project |
| `tools/supabase_schema_check.sh` | schema + RLS on a throwaway local Postgres |
