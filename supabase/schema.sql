-- ═══════════════════════════════════════════════════════════════════════════
--  Runewake — Supabase schema  (FABLE-018, accounts + cloud save)
-- ═══════════════════════════════════════════════════════════════════════════
--
--  Paste the whole file into the Supabase SQL editor and run it once. It is
--  idempotent: every statement is IF NOT EXISTS / OR REPLACE / DROP-then-CREATE,
--  so running it twice is safe.
--
--  THE MODEL, IN ONE PARAGRAPH
--  A player is a row in auth.users. They get there by ANONYMOUS sign-in the
--  first time the game opens — no email, no password, no friction — and the
--  client stores the resulting session. Later they may LINK an email to that
--  same user (Supabase "update user" + OTP), which makes the account
--  recoverable on another phone. Their whole progression is ONE jsonb blob in
--  player_saves, keyed by their user id, last-write-wins on updated_at. Row
--  Level Security means a user can only ever see their own rows; the anon key
--  shipped in the APK cannot read anyone's save without that user's JWT.
--
--  Every table below is protected by RLS. There is no service-role usage
--  anywhere in the client. If you add a table, add its policies in the same
--  commit or it is invisible to the game.
--
-- ───────────────────────────────────────────────────────────────────────────

create extension if not exists pgcrypto;

-- ── profiles ──────────────────────────────────────────────────────────────
-- One per auth user, created by trigger. Public-ish identity: what other
-- players could eventually see (display name). Never holds the save.
create table if not exists public.profiles (
  id            uuid primary key references auth.users(id) on delete cascade,
  display_name  text,
  created_at    timestamptz not null default now(),
  last_seen_at  timestamptz not null default now()
);

alter table public.profiles enable row level security;

drop policy if exists "profiles: read own"   on public.profiles;
drop policy if exists "profiles: update own" on public.profiles;
create policy "profiles: read own"   on public.profiles for select using (auth.uid() = id);
create policy "profiles: update own" on public.profiles for update using (auth.uid() = id) with check (auth.uid() = id);
-- No insert policy on purpose: rows are created by the trigger below, which
-- runs as the function owner, not as the user.

-- ── player_saves ──────────────────────────────────────────────────────────
-- The cloud copy of ProgressionState. One row per user. The client upserts
-- the whole blob; the server stamps updated_at. Merge rule is last-write-wins
-- on updated_at, decided client-side (see SyncManager) — the server never
-- merges, it only stores.
create table if not exists public.player_saves (
  user_id        uuid primary key references auth.users(id) on delete cascade,
  save_version   integer not null default 1,      -- ProgressionSnapshot.Version
  save_json      jsonb   not null,
  device_id      text,                            -- which install wrote it last
  app_version    text,
  updated_at     timestamptz not null default now()
);

alter table public.player_saves enable row level security;

drop policy if exists "saves: read own"   on public.player_saves;
drop policy if exists "saves: insert own" on public.player_saves;
drop policy if exists "saves: update own" on public.player_saves;
create policy "saves: read own"   on public.player_saves for select using (auth.uid() = user_id);
create policy "saves: insert own" on public.player_saves for insert with check (auth.uid() = user_id);
create policy "saves: update own" on public.player_saves for update using (auth.uid() = user_id) with check (auth.uid() = user_id);

-- The client sends updated_at = now-ish, but the SERVER's clock is the one
-- that decides ordering. Overwrite whatever the client sent.
create or replace function public.stamp_updated_at()
returns trigger language plpgsql as $$
begin
  new.updated_at := now();
  return new;
end $$;

drop trigger if exists player_saves_stamp on public.player_saves;
create trigger player_saves_stamp
  before insert or update on public.player_saves
  for each row execute function public.stamp_updated_at();

-- ── relic_instances ───────────────────────────────────────────────────────
-- The Lost Relic ledger. Kept compatible with engine/Supabase/RelicLedgerSync
-- (same column names it already posts), but now owned by a real user, not a
-- device id.
create table if not exists public.relic_instances (
  relic_instance_id  text primary key,
  user_id            uuid not null references auth.users(id) on delete cascade,
  card_id            text not null,
  acquirer_name      text not null default '',
  acquired_at        text not null default '',
  site               text not null default '',
  discovery_index    integer not null default 0,
  engraving_style    text not null default '',
  created_at         timestamptz not null default now()
);

create index if not exists relic_instances_user_idx on public.relic_instances(user_id);

alter table public.relic_instances enable row level security;

drop policy if exists "relics: read own"   on public.relic_instances;
drop policy if exists "relics: insert own" on public.relic_instances;
drop policy if exists "relics: update own" on public.relic_instances;
create policy "relics: read own"   on public.relic_instances for select using (auth.uid() = user_id);
create policy "relics: insert own" on public.relic_instances for insert with check (auth.uid() = user_id);
create policy "relics: update own" on public.relic_instances for update using (auth.uid() = user_id) with check (auth.uid() = user_id);

-- ── crash_reports ─────────────────────────────────────────────────────────
-- Append-only, from client/scripts/telemetry/CrashReporter. No user id on
-- purpose: a crash report is not personal data and must be sendable before
-- sign-in has happened.
create table if not exists public.crash_reports (
  id              uuid primary key default gen_random_uuid(),
  received_at     timestamptz not null default now(),
  app_version     text,
  platform        text,
  exception_type  text,
  message         text,
  stack_trace     text,
  godot_version   text
);

alter table public.crash_reports enable row level security;
drop policy if exists "crash: insert only" on public.crash_reports;
create policy "crash: insert only" on public.crash_reports for insert with check (true);
-- No select policy: the anon key can write a report and can never read them.

-- ── new-user trigger ──────────────────────────────────────────────────────
-- Creates the profile row the moment auth.users gets a row, for anonymous
-- and email users alike. SECURITY DEFINER so it can insert despite there being
-- no insert policy on profiles.
create or replace function public.handle_new_user()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles (id, display_name)
  values (new.id, coalesce(new.raw_user_meta_data->>'display_name', null))
  on conflict (id) do nothing;
  return new;
end $$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
  after insert on auth.users
  for each row execute function public.handle_new_user();

-- ── touch profile on save ─────────────────────────────────────────────────
-- last_seen_at is a free "is this account alive" signal; costs nothing.
create or replace function public.touch_profile_on_save()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  update public.profiles set last_seen_at = now() where id = new.user_id;
  return new;
end $$;

drop trigger if exists player_saves_touch_profile on public.player_saves;
create trigger player_saves_touch_profile
  after insert or update on public.player_saves
  for each row execute function public.touch_profile_on_save();

-- ═══════════════════════════════════════════════════════════════════════════
--  Dashboard settings that are NOT expressible in SQL — do these by hand:
--    Authentication → Providers → Anonymous sign-ins  → ENABLE
--    Authentication → Providers → Email               → ENABLE
--        "Confirm email" may stay on; the client uses the OTP code flow.
--    Authentication → Email Templates → Magic Link     → make sure {{ .Token }}
--        appears in the body, so the 6-digit code is in the email.
-- ═══════════════════════════════════════════════════════════════════════════
