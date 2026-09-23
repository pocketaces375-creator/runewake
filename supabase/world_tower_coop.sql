-- ═══════════════════════════════════════════════════════════════════════════
--  Runewake — Supabase migration FABLE-020: shared world, Tower, co-op
-- ═══════════════════════════════════════════════════════════════════════════
--
--  Run AFTER schema.sql. Paste into the Supabase SQL editor and run once.
--  Idempotent (IF NOT EXISTS / OR REPLACE / DROP-then-CREATE): safe to re-run.
--
--  THREE SYSTEMS
--
--  1. THE SHARED WORLD. The world itself is never stored: every phone
--     generates the same pages from the same seed (engine/World). What IS
--     stored is who found what:
--       world_blips   one row per place anyone has EVER discovered, with the
--                     first discoverer. The client never reads this table
--                     directly (RLS hides it) — it asks two RPCs:
--         discover_blip(id)      "I just entered this place" → were you FIRST?
--                                Only the first discoverer is ever told; the
--                                40,000th finds out nothing (by design).
--         page_discoveries(key)  which places on this page has anyone found?
--                                (drives dimmed dots vs. roads into nothing)
--       player_blips  what each player has visited / cleared (mirrors the save,
--                     gives the server its own record for later anti-cheat).
--
--  2. THE TOWER. 100 authored floors. A floor's definition (jsonb) is served
--     from here so floors can be tuned without an app update, and a floor's
--     content is invisible until the floor opens. A floor's BOSS opens the
--     NEXT floor for everyone once `unlock_threshold` DIFFERENT players have
--     beaten it. Floor 1 starts open.
--
--  3. CO-OP / RAIDS / PVP. An expedition is a lobby of up to 5 players. Each
--     player fights on their own board (engine/Coop); the phones stay in step
--     over a Supabase Realtime channel "exp:<id>", sending only their moves.
--     These tables are the lobby and the result; the moves never touch Postgres.
--
--  Everything is RLS-protected; the client only ever uses the anon key plus
--  the player's own JWT. Writes that must be trusted go through SECURITY
--  DEFINER functions with their own checks.
-- ───────────────────────────────────────────────────────────────────────────

-- ═══════════════════════════════════════════════════════════════════════════
--  1. THE SHARED WORLD
-- ═══════════════════════════════════════════════════════════════════════════

create table if not exists public.world_blips (
  blip_id          text primary key,                 -- "w1:elvenwood:0:3:17"
  page_key         text not null,                    -- "w1:elvenwood:0:3"
  first_discoverer uuid references auth.users(id) on delete set null,
  discovered_at    timestamptz not null default now()
);
create index if not exists world_blips_page on public.world_blips(page_key);
alter table public.world_blips enable row level security;
-- No policies: nobody reads or writes this table directly. See the RPCs.

create table if not exists public.player_blips (
  user_id     uuid not null references auth.users(id) on delete cascade,
  blip_id     text not null,
  state       smallint not null default 1,            -- 1 visited, 2 cleared
  updated_at  timestamptz not null default now(),
  primary key (user_id, blip_id)
);
alter table public.player_blips enable row level security;
drop policy if exists "player_blips: read own" on public.player_blips;
create policy "player_blips: read own" on public.player_blips for select using (auth.uid() = user_id);

-- Shape check shared by the RPCs: w<version>:<biome>:<instance>:<page>:<index>
create or replace function public.is_blip_id(p text) returns boolean
language sql immutable as $$
  select p ~ '^w[0-9]{1,4}:[a-z0-9_]{1,40}:[0-9]{1,6}:[0-9]{1,3}:[0-9]{1,3}$'
$$;

-- "I just entered this place." Returns {"first": true} only for the first
-- player ever. Idempotent for the same player.
create or replace function public.discover_blip(p_blip_id text)
returns jsonb language plpgsql security definer set search_path = public as $$
declare
  v_first boolean := false;
  v_rows  integer;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  if not public.is_blip_id(p_blip_id) then raise exception 'bad blip id'; end if;

  insert into public.world_blips (blip_id, page_key, first_discoverer)
  values (p_blip_id, regexp_replace(p_blip_id, ':[0-9]+$', ''), auth.uid())
  on conflict (blip_id) do nothing;
  get diagnostics v_rows = row_count;
  v_first := v_rows = 1;

  insert into public.player_blips (user_id, blip_id, state)
  values (auth.uid(), p_blip_id, 1)
  on conflict (user_id, blip_id) do nothing;

  return jsonb_build_object('first', v_first);
end $$;

-- "I beat / completed this place."
create or replace function public.clear_blip(p_blip_id text)
returns void language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  if not public.is_blip_id(p_blip_id) then raise exception 'bad blip id'; end if;
  insert into public.world_blips (blip_id, page_key, first_discoverer)
  values (p_blip_id, regexp_replace(p_blip_id, ':[0-9]+$', ''), auth.uid())
  on conflict (blip_id) do nothing;
  insert into public.player_blips (user_id, blip_id, state)
  values (auth.uid(), p_blip_id, 2)
  on conflict (user_id, blip_id) do update set state = 2, updated_at = now();
end $$;

-- Which places on this page has ANYONE found? Ids only — never who.
create or replace function public.page_discoveries(p_page_key text)
returns setof text language sql stable security definer set search_path = public as $$
  select blip_id from public.world_blips where page_key = p_page_key
$$;

grant execute on function public.discover_blip(text)    to authenticated;
grant execute on function public.clear_blip(text)       to authenticated;
grant execute on function public.page_discoveries(text) to authenticated;

-- Supabase grants table privileges to `authenticated` by default; RLS is what
-- actually restricts rows. Stated explicitly so the file also works on a bare
-- Postgres (tools/supabase_schema_check.sh) and never depends on defaults.
grant select on public.player_blips to authenticated;

-- ═══════════════════════════════════════════════════════════════════════════
--  2. THE TOWER
-- ═══════════════════════════════════════════════════════════════════════════

create table if not exists public.tower_floors (
  floor             integer primary key check (floor between 1 and 100),
  version           integer not null default 1,        -- bump when the definition changes
  title             text not null,
  theme             text,
  definition        jsonb not null,                    -- engine/Tower TowerFloorDef
  unlock_threshold  integer not null default 100,      -- DIFFERENT players who must beat this boss
  is_open           boolean not null default false,
  opened_at         timestamptz
);
alter table public.tower_floors enable row level security;
drop policy if exists "tower_floors: read open" on public.tower_floors;
create policy "tower_floors: read open" on public.tower_floors for select using (is_open);

create table if not exists public.tower_boss_clears (
  floor       integer not null references public.tower_floors(floor) on delete cascade,
  user_id     uuid not null references auth.users(id) on delete cascade,
  raid_id     uuid,
  cleared_at  timestamptz not null default now(),
  primary key (floor, user_id)                          -- one row per player per floor: UNIQUE clears
);
alter table public.tower_boss_clears enable row level security;
drop policy if exists "tower_boss_clears: read own" on public.tower_boss_clears;
create policy "tower_boss_clears: read own" on public.tower_boss_clears for select using (auth.uid() = user_id);

create table if not exists public.tower_player_floors (
  user_id        uuid not null references auth.users(id) on delete cascade,
  floor          integer not null references public.tower_floors(floor) on delete cascade,
  cleared_places jsonb not null default '[]'::jsonb,   -- place ids on this floor
  boss_cleared   boolean not null default false,
  best_turns     integer,
  updated_at     timestamptz not null default now(),
  primary key (user_id, floor)
);
alter table public.tower_player_floors enable row level security;
drop policy if exists "tower_player_floors: read own"   on public.tower_player_floors;
drop policy if exists "tower_player_floors: upsert own" on public.tower_player_floors;
drop policy if exists "tower_player_floors: update own" on public.tower_player_floors;
create policy "tower_player_floors: read own"   on public.tower_player_floors for select using (auth.uid() = user_id);
create policy "tower_player_floors: upsert own" on public.tower_player_floors for insert with check (auth.uid() = user_id);
create policy "tower_player_floors: update own" on public.tower_player_floors for update using (auth.uid() = user_id) with check (auth.uid() = user_id);

-- Public progress of every open floor plus the next locked one:
-- how many different players have felled each boss, out of how many needed.
create or replace function public.tower_status()
returns table (floor integer, title text, is_open boolean, unique_clears bigint, unlock_threshold integer, opened_at timestamptz)
language sql stable security definer set search_path = public as $$
  select f.floor, f.title, f.is_open,
         (select count(*) from public.tower_boss_clears c where c.floor = f.floor),
         f.unlock_threshold, f.opened_at
  from public.tower_floors f
  where f.is_open or f.floor = (select coalesce(max(floor), 0) + 1 from public.tower_floors where is_open)
  order by f.floor
$$;

-- "I (a member of this raid) beat this floor's boss." Counts a unique clear
-- and opens the next floor for everyone when the threshold is reached.
-- v1 trusts the client's victory; v2 should require the raid's replay
-- (seed + moves) and re-simulate it before counting.
create or replace function public.record_tower_boss_clear(p_floor integer, p_raid_id uuid default null)
returns jsonb language plpgsql security definer set search_path = public as $$
declare
  v_open boolean; v_threshold integer; v_count bigint; v_opened boolean := false; v_first boolean;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  select is_open, unlock_threshold into v_open, v_threshold from public.tower_floors where floor = p_floor;
  if v_open is distinct from true then raise exception 'floor % is not open', p_floor; end if;

  insert into public.tower_boss_clears (floor, user_id, raid_id) values (p_floor, auth.uid(), p_raid_id)
  on conflict (floor, user_id) do nothing;
  select count(*) into v_count from public.tower_boss_clears where floor = p_floor;
  v_first := v_count = 1 and exists (select 1 from public.tower_boss_clears where floor = p_floor and user_id = auth.uid());

  insert into public.tower_player_floors (user_id, floor, boss_cleared) values (auth.uid(), p_floor, true)
  on conflict (user_id, floor) do update set boss_cleared = true, updated_at = now();

  if v_count >= v_threshold then
    update public.tower_floors set is_open = true, opened_at = now()
    where floor = p_floor + 1 and not is_open;
    get diagnostics v_count = row_count;
    v_opened := v_count = 1;
    select count(*) into v_count from public.tower_boss_clears where floor = p_floor;
  end if;

  return jsonb_build_object('unique_clears', v_count, 'threshold', v_threshold,
                            'next_floor_opened', v_opened, 'first_ever', v_first);
end $$;

grant select on public.tower_floors, public.tower_boss_clears to authenticated;
grant select, insert, update on public.tower_player_floors to authenticated;
grant execute on function public.tower_status()                          to authenticated;
grant execute on function public.record_tower_boss_clear(integer, uuid)  to authenticated;

-- ═══════════════════════════════════════════════════════════════════════════
--  3. CO-OP EXPEDITIONS, RAIDS, PVP
-- ═══════════════════════════════════════════════════════════════════════════

create table if not exists public.expeditions (
  id           uuid primary key default gen_random_uuid(),
  kind         text not null check (kind in ('coop', 'raid', 'pvp1v1', 'pvp2v2')),
  host         uuid not null references auth.users(id) on delete cascade,
  target       text not null,                 -- blip id, or 'tower:<floor>:boss'
  max_players  integer not null default 5 check (max_players between 2 and 5),
  seed         bigint,                        -- set at start: every board derives its seed from this
  state        text not null default 'open' check (state in ('open', 'running', 'done', 'abandoned')),
  result       jsonb,
  created_at   timestamptz not null default now(),
  started_at   timestamptz,
  ended_at     timestamptz
);
create index if not exists expeditions_open on public.expeditions(state, kind) where state = 'open';

create table if not exists public.expedition_members (
  expedition_id uuid not null references public.expeditions(id) on delete cascade,
  user_id       uuid not null references auth.users(id) on delete cascade,
  seat          integer not null check (seat between 0 and 4),
  display_name  text,
  class_id      text,
  deck          jsonb,                        -- card ids, fixed at start
  status        text not null default 'joined' check (status in ('joined', 'ready', 'knocked_out', 'won', 'left')),
  joined_at     timestamptz not null default now(),
  primary key (expedition_id, user_id),
  unique (expedition_id, seat)
);

alter table public.expeditions        enable row level security;
alter table public.expedition_members enable row level security;

create or replace function public.is_expedition_member(p_id uuid) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.expedition_members where expedition_id = p_id and user_id = auth.uid())
$$;

drop policy if exists "expeditions: read open or mine" on public.expeditions;
create policy "expeditions: read open or mine" on public.expeditions for select
  using (state = 'open' or public.is_expedition_member(id));
drop policy if exists "members: read my expeditions" on public.expedition_members;
create policy "members: read my expeditions" on public.expedition_members for select
  using (public.is_expedition_member(expedition_id));
drop policy if exists "members: update own row" on public.expedition_members;
create policy "members: update own row" on public.expedition_members for update
  using (auth.uid() = user_id) with check (auth.uid() = user_id);

create or replace function public.create_expedition(p_kind text, p_target text, p_max integer, p_display_name text, p_class text, p_deck jsonb)
returns uuid language plpgsql security definer set search_path = public as $$
declare v_id uuid;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  insert into public.expeditions (kind, host, target, max_players)
  values (p_kind, auth.uid(), p_target,
          case p_kind when 'pvp1v1' then 2 when 'pvp2v2' then 4 else least(greatest(coalesce(p_max, 5), 2), 5) end)
  returning id into v_id;
  insert into public.expedition_members (expedition_id, user_id, seat, display_name, class_id, deck)
  values (v_id, auth.uid(), 0, p_display_name, p_class, p_deck);
  return v_id;
end $$;

create or replace function public.join_expedition(p_id uuid, p_display_name text, p_class text, p_deck jsonb)
returns integer language plpgsql security definer set search_path = public as $$
declare v_seat integer; v_max integer; v_state text;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  select max_players, state into v_max, v_state from public.expeditions where id = p_id for update;
  if v_state is distinct from 'open' then raise exception 'expedition is not open'; end if;
  select seat into v_seat from public.expedition_members where expedition_id = p_id and user_id = auth.uid();
  if v_seat is not null then return v_seat; end if;
  select min(s) into v_seat from generate_series(0, v_max - 1) s
   where s not in (select seat from public.expedition_members where expedition_id = p_id);
  if v_seat is null then raise exception 'expedition is full'; end if;
  insert into public.expedition_members (expedition_id, user_id, seat, display_name, class_id, deck)
  values (p_id, auth.uid(), v_seat, p_display_name, p_class, p_deck);
  return v_seat;
end $$;

-- Host only. Fixes the seed; from here every phone can build every board.
create or replace function public.start_expedition(p_id uuid)
returns bigint language plpgsql security definer set search_path = public as $$
declare v_seed bigint;
begin
  v_seed := (random() * 9007199254740991)::bigint;
  update public.expeditions set state = 'running', seed = v_seed, started_at = now()
   where id = p_id and host = auth.uid() and state = 'open';
  if not found then raise exception 'only the host can start an open expedition'; end if;
  return v_seed;
end $$;

-- Any member may report the end; the first report wins (all phones compute
-- the same result from the same moves, so they agree).
create or replace function public.finish_expedition(p_id uuid, p_result jsonb)
returns void language plpgsql security definer set search_path = public as $$
begin
  if not public.is_expedition_member(p_id) then raise exception 'not a member'; end if;
  update public.expeditions set state = 'done', result = p_result, ended_at = now()
   where id = p_id and state = 'running';
end $$;

grant select on public.expeditions to authenticated;
grant select, update on public.expedition_members to authenticated;
grant execute on function public.create_expedition(text, text, integer, text, text, jsonb) to authenticated;
grant execute on function public.join_expedition(uuid, text, text, jsonb)               to authenticated;
grant execute on function public.start_expedition(uuid)                                  to authenticated;
grant execute on function public.finish_expedition(uuid, jsonb)                          to authenticated;

-- Realtime: moves travel on the private broadcast channel "exp:<expedition id>".
-- With Realtime Authorization enabled (Project Settings → Realtime → private
-- channels), only members may send or receive on their expedition's channel.
do $$
begin
  if exists (select 1 from information_schema.tables where table_schema = 'realtime' and table_name = 'messages') then
    execute 'drop policy if exists "exp channel: members receive" on realtime.messages';
    execute 'drop policy if exists "exp channel: members send" on realtime.messages';
    execute $p$create policy "exp channel: members receive" on realtime.messages for select to authenticated
      using (realtime.topic() like 'exp:%' and public.is_expedition_member(substring(realtime.topic() from 5)::uuid))$p$;
    execute $p$create policy "exp channel: members send" on realtime.messages for insert to authenticated
      with check (realtime.topic() like 'exp:%' and public.is_expedition_member(substring(realtime.topic() from 5)::uuid))$p$;
  end if;
end $$;
