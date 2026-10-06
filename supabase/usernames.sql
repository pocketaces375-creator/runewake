-- ═══════════════════════════════════════════════════════════════════════════
--  Runewake — Supabase migration FABLE-054: usernames + challenge by name
-- ═══════════════════════════════════════════════════════════════════════════
--
--  Run AFTER schema.sql, world_tower_coop.sql and online_play.sql. Paste into the
--  Supabase SQL editor and run once. Idempotent: safe to re-run.
--
--  WHAT THIS ADDS
--
--    USERNAMES     profiles.username — what other players see instead of an
--                  email. Unique, ignoring case. 3–16 letters/numbers/spaces/_/-.
--                  (The phone also filters crude words before it ever asks.)
--                  claim_username, my_username, username_free.
--    CHALLENGES    duel_challenges: "host a duel, then challenge a friend by
--                  username". The friend's online screen shows the invite and
--                  joins the host's lobby with one tap.
--                  challenge_player, my_challenges, answer_challenge.
--
--  Every function checks auth.uid() itself (SECURITY DEFINER); the tables are
--  closed to direct access.
-- ───────────────────────────────────────────────────────────────────────────

-- ── usernames ─────────────────────────────────────────────────────────────
alter table public.profiles add column if not exists username text;
create unique index if not exists profiles_username_ci on public.profiles (lower(username)) where username is not null;

create or replace function public.username_shape_ok(p text) returns boolean
language sql immutable as $$
  select p is not null
     and length(p) between 3 and 16
     and p ~ '^[A-Za-z0-9]([A-Za-z0-9 _-]*[A-Za-z0-9])?$'
     and p !~ '  '
     and p ~ '[A-Za-z]'
$$;

-- 'ok' | 'taken' | 'invalid'
create or replace function public.claim_username(p_name text)
returns text language plpgsql security definer set search_path = public as $$
declare v text := trim(coalesce(p_name, ''));
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  if not public.username_shape_ok(v) then return 'invalid'; end if;
  if exists (select 1 from public.profiles where lower(username) = lower(v) and id <> auth.uid()) then return 'taken'; end if;
  insert into public.profiles (id, display_name, username) values (auth.uid(), v, v)
  on conflict (id) do update set username = excluded.username, display_name = excluded.display_name;
  return 'ok';
exception when unique_violation then
  return 'taken';
end $$;

create or replace function public.my_username()
returns text language sql stable security definer set search_path = public as $$
  select username from public.profiles where id = auth.uid()
$$;

create or replace function public.username_free(p_name text)
returns boolean language sql stable security definer set search_path = public as $$
  select public.username_shape_ok(trim(p_name))
     and not exists (select 1 from public.profiles where lower(username) = lower(trim(p_name)) and id <> auth.uid())
$$;

-- ── challenges ────────────────────────────────────────────────────────────
create table if not exists public.duel_challenges (
  id            uuid primary key default gen_random_uuid(),
  from_user     uuid not null references auth.users(id) on delete cascade,
  to_user       uuid not null references auth.users(id) on delete cascade,
  from_name     text not null,
  expedition_id uuid not null references public.expeditions(id) on delete cascade,
  status        text not null default 'open' check (status in ('open', 'accepted', 'declined', 'gone')),
  created_at    timestamptz not null default now()
);
create index if not exists duel_challenges_to on public.duel_challenges (to_user, status);
alter table public.duel_challenges enable row level security;
-- no policies: only the functions below touch it

-- 'ok' | 'no_such_player' | 'self' | 'no_lobby' | 'no_username'
create or replace function public.challenge_player(p_name text, p_expedition uuid)
returns text language plpgsql security definer set search_path = public as $$
declare v_to uuid; v_from text;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  select username into v_from from public.profiles where id = auth.uid();
  if v_from is null then return 'no_username'; end if;
  select id into v_to from public.profiles where lower(username) = lower(trim(p_name));
  if v_to is null then return 'no_such_player'; end if;
  if v_to = auth.uid() then return 'self'; end if;
  if not exists (select 1 from public.expeditions e where e.id = p_expedition and e.host = auth.uid() and e.state = 'open') then return 'no_lobby'; end if;
  -- one open challenge per pair: a fresh one replaces the old
  update public.duel_challenges set status = 'gone' where from_user = auth.uid() and to_user = v_to and status = 'open';
  insert into public.duel_challenges (from_user, to_user, from_name, expedition_id) values (auth.uid(), v_to, v_from, p_expedition);
  return 'ok';
end $$;

-- open challenges to me from the last 15 minutes whose lobby is still open
create or replace function public.my_challenges()
returns jsonb language sql stable security definer set search_path = public as $$
  select coalesce(jsonb_agg(jsonb_build_object('id', c.id, 'from_name', c.from_name, 'code', e.code, 'expedition_id', e.id) order by c.created_at desc), '[]'::jsonb)
    from public.duel_challenges c join public.expeditions e on e.id = c.expedition_id
   where c.to_user = auth.uid() and c.status = 'open' and e.state = 'open' and c.created_at > now() - interval '15 minutes'
$$;

-- returns the lobby code to join when accepted, null otherwise
create or replace function public.answer_challenge(p_id uuid, p_accept boolean)
returns text language plpgsql security definer set search_path = public as $$
declare v_code text;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  update public.duel_challenges set status = case when p_accept then 'accepted' else 'declined' end
   where id = p_id and to_user = auth.uid() and status = 'open';
  if not found or not p_accept then return null; end if;
  select e.code into v_code from public.duel_challenges c join public.expeditions e on e.id = c.expedition_id
   where c.id = p_id and e.state = 'open';
  return v_code;
end $$;

grant execute on function public.claim_username(text), public.my_username(), public.username_free(text),
  public.challenge_player(text, uuid), public.my_challenges(), public.answer_challenge(uuid, boolean) to authenticated;
