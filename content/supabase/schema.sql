-- ===================================================================
-- Runewake: The Buried Age — Supabase schema (v1)
-- Run ONCE in the Supabase SQL Editor before any client connects.
-- Not executed by the agent — this is the DDL reference.
-- ===================================================================

-- Account identity (device-anchored, no auth required for v1)
-- Each device gets its own account UUID via get_or_create_account().
-- RLS in v1 is deliberately wide open — see note below.
create table if not exists accounts (
  account_id uuid primary key default gen_random_uuid(),
  device_id text unique not null,
  created_at timestamptz default now()
);

-- RPC: get or create account by device_id
-- Returns the existing account_id if the device already registered.
create or replace function get_or_create_account(device_id text)
returns uuid language plpgsql as $$
declare aid uuid;
begin
  insert into accounts(device_id) values(device_id)
  on conflict(device_id) do nothing;
  select account_id into aid from accounts where accounts.device_id = get_or_create_account.device_id;
  return aid;
end;
$$;

-- Relic ledger: one row per minted Lost Relic instance
-- relic_instance_id is the client-generated UUID (stable across sync).
create table if not exists relic_instances (
  relic_instance_id uuid primary key,
  account_id uuid references accounts(account_id) on delete cascade,
  card_id text not null,
  acquirer_name text not null,
  acquired_at date not null,
  site text not null,
  discovery_index int not null,
  engraving_style text not null,
  synced_at timestamptz default now()
);

-- ===================================================================
-- Row-Level Security (v1)
-- NOTE: In v1 we use anonymous device-anchored identity without a real
-- JWT auth system. The RLS policies below use `true` (allow all) so
-- that the anon key can read/write every account's relics.
--
-- When real auth is added, replace with:
--   using (account_id = (select account_id from accounts
--     where device_id = current_setting('request.jwt.claims', true)::json->>'sub'))
-- ===================================================================
alter table relic_instances enable row level security;

create policy relic_select_all on relic_instances
  for select using (true);

create policy relic_insert_all on relic_instances
  for insert with check (true);

create policy relic_update_all on relic_instances
  for update using (true);