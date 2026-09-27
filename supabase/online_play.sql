-- ═══════════════════════════════════════════════════════════════════════════
--  Runewake — Supabase migration FABLE-038: online play (PvP duels + co-op)
-- ═══════════════════════════════════════════════════════════════════════════
--
--  Run AFTER schema.sql and world_tower_coop.sql. Paste into the Supabase SQL
--  editor and run once. Idempotent: safe to re-run.
--
--  WHAT THIS ADDS
--
--  world_tower_coop.sql gave every expedition (co-op, raid, PvP) a lobby, but
--  the moves were meant to travel over Supabase Realtime, which the phone never
--  learned to speak. This replaces that transport with something every client
--  already has — plain HTTPS through PostgREST — and adds what a lobby needs to
--  be used by real people:
--
--    invite CODES      six characters, no confusable letters. "Host a duel"
--                      shows one; a friend types it in. (create_expedition_v2,
--                      find_expedition)
--    the MOVE LOG      expedition_moves: one row per move, in order. A phone
--                      posts its own moves (post_move) and polls for everyone
--                      else's (moves_since). Turn-based play needs nothing
--                      faster than a poll every second or two, and the log is
--                      also the replay: seed + moves = the whole match.
--    PRESENCE          last_seen on each member, refreshed by every poll, so a
--                      phone can tell when the other player has gone.
--    READY / LEAVE     set_ready, leave_expedition.
--    one LOBBY call    lobby(id) returns everything a lobby screen shows.
--
--  Every function checks auth.uid() and membership itself (SECURITY DEFINER),
--  so RLS on the tables is belt-and-braces, not the only guard.
-- ───────────────────────────────────────────────────────────────────────────

-- ── invite codes ───────────────────────────────────────────────────────────
alter table public.expeditions add column if not exists code text;
create unique index if not exists expeditions_code_open on public.expeditions(code) where state in ('open', 'running');

-- 6 characters from an alphabet with no 0/O, 1/I/L: 28^6 ≈ 480 million codes.
create or replace function public.new_expedition_code() returns text
language plpgsql volatile as $$
declare
  alphabet constant text := 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
  v_code text; i int;
begin
  loop
    v_code := '';
    for i in 1..6 loop
      v_code := v_code || substr(alphabet, 1 + floor(random() * length(alphabet))::int, 1);
    end loop;
    exit when not exists (select 1 from public.expeditions e where e.code = v_code and e.state in ('open', 'running'));
  end loop;
  return v_code;
end $$;

-- ── presence ───────────────────────────────────────────────────────────────
alter table public.expedition_members add column if not exists last_seen timestamptz not null default now();

-- ── the move log ───────────────────────────────────────────────────────────
create table if not exists public.expedition_moves (
  id            bigserial primary key,
  expedition_id uuid not null references public.expeditions(id) on delete cascade,
  seat          integer not null,
  seq           integer not null,          -- per-seat, from 0, no gaps
  payload       jsonb not null,            -- engine NetMessage (t = move | hash | leave | concede)
  created_at    timestamptz not null default now(),
  unique (expedition_id, seat, seq)
);
create index if not exists expedition_moves_feed on public.expedition_moves(expedition_id, id);
alter table public.expedition_moves enable row level security;
drop policy if exists "moves: members read" on public.expedition_moves;
create policy "moves: members read" on public.expedition_moves for select
  using (public.is_expedition_member(expedition_id));
grant select on public.expedition_moves to authenticated;
grant usage, select on sequence public.expedition_moves_id_seq to authenticated;

-- ── create (v2: returns the code too) ──────────────────────────────────────
create or replace function public.create_expedition_v2(p_kind text, p_target text, p_max integer, p_display_name text, p_class text, p_deck jsonb)
returns jsonb language plpgsql security definer set search_path = public as $$
declare v_id uuid; v_code text;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  -- one open lobby per host: hosting again abandons the old one
  update public.expeditions set state = 'abandoned', ended_at = now() where host = auth.uid() and state = 'open';
  v_code := public.new_expedition_code();
  insert into public.expeditions (kind, host, target, max_players, code)
  values (p_kind, auth.uid(), p_target,
          case p_kind when 'pvp1v1' then 2 when 'pvp2v2' then 4 else least(greatest(coalesce(p_max, 5), 2), 5) end,
          v_code)
  returning id into v_id;
  insert into public.expedition_members (expedition_id, user_id, seat, display_name, class_id, deck)
  values (v_id, auth.uid(), 0, p_display_name, p_class, p_deck);
  return jsonb_build_object('id', v_id, 'code', v_code);
end $$;

-- "My friend gave me this code."
create or replace function public.find_expedition(p_code text)
returns jsonb language plpgsql security definer set search_path = public as $$
declare v record;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  select e.id, e.kind, e.target, e.state, e.max_players,
         (select count(*) from public.expedition_members m where m.expedition_id = e.id and m.status <> 'left') as players,
         (select m.display_name from public.expedition_members m where m.expedition_id = e.id and m.seat = 0) as host_name
    into v
    from public.expeditions e
   where e.code = upper(trim(p_code)) and e.state in ('open', 'running')
   order by e.created_at desc limit 1;
  if v.id is null then return null; end if;
  return jsonb_build_object('id', v.id, 'kind', v.kind, 'target', v.target, 'state', v.state,
                            'max_players', v.max_players, 'players', v.players, 'host_name', v.host_name);
end $$;

-- ── ready / leave / presence ───────────────────────────────────────────────
create or replace function public.set_ready(p_id uuid, p_ready boolean)
returns void language plpgsql security definer set search_path = public as $$
begin
  update public.expedition_members set status = case when p_ready then 'ready' else 'joined' end, last_seen = now()
   where expedition_id = p_id and user_id = auth.uid() and status in ('joined', 'ready');
  if not found then raise exception 'not a member of an open lobby'; end if;
end $$;

create or replace function public.leave_expedition(p_id uuid)
returns void language plpgsql security definer set search_path = public as $$
declare v_state text; v_host uuid;
begin
  select state, host into v_state, v_host from public.expeditions where id = p_id;
  if v_state is null then return; end if;
  if v_state = 'open' then
    if v_host = auth.uid() then
      update public.expeditions set state = 'abandoned', ended_at = now() where id = p_id;
    else
      delete from public.expedition_members where expedition_id = p_id and user_id = auth.uid();
    end if;
  else
    update public.expedition_members set status = 'left', last_seen = now()
     where expedition_id = p_id and user_id = auth.uid();
  end if;
end $$;

-- ── the lobby, in one call ─────────────────────────────────────────────────
-- Also refreshes the caller's last_seen: a phone that is polling is present.
create or replace function public.lobby(p_id uuid)
returns jsonb language plpgsql security definer set search_path = public as $$
declare e record; members jsonb;
begin
  if not public.is_expedition_member(p_id) then raise exception 'not a member'; end if;
  update public.expedition_members set last_seen = now() where expedition_id = p_id and user_id = auth.uid();
  select * into e from public.expeditions where id = p_id;
  select coalesce(jsonb_agg(jsonb_build_object(
           'seat', m.seat, 'user_id', m.user_id, 'display_name', m.display_name, 'class_id', m.class_id,
           'deck', m.deck, 'status', m.status,
           'seconds_ago', greatest(0, floor(extract(epoch from (now() - m.last_seen))))::int,
           'me', m.user_id = auth.uid()) order by m.seat), '[]'::jsonb)
    into members
    from public.expedition_members m where m.expedition_id = p_id;
  return jsonb_build_object('id', e.id, 'kind', e.kind, 'target', e.target, 'state', e.state, 'seed', e.seed,
                            'code', e.code, 'host', e.host, 'max_players', e.max_players, 'result', e.result,
                            'members', members);
end $$;

-- ── moves ──────────────────────────────────────────────────────────────────
-- The seat comes from the membership, never from the client. Sequence numbers
-- must arrive in order (per seat) — a gap means a lost message, and lockstep
-- cannot skip.
create or replace function public.post_move(p_id uuid, p_seq integer, p_payload jsonb)
returns bigint language plpgsql security definer set search_path = public as $$
declare v_seat integer; v_state text; v_next integer; v_id bigint;
begin
  select m.seat into v_seat from public.expedition_members m where m.expedition_id = p_id and m.user_id = auth.uid();
  if v_seat is null then raise exception 'not a member'; end if;
  select state into v_state from public.expeditions where id = p_id;
  if v_state <> 'running' then raise exception 'expedition is %', v_state; end if;
  select coalesce(max(seq) + 1, 0) into v_next from public.expedition_moves where expedition_id = p_id and seat = v_seat;
  if p_seq < v_next then
    -- a retry of a move that already landed: idempotent
    select id into v_id from public.expedition_moves where expedition_id = p_id and seat = v_seat and seq = p_seq;
    return v_id;
  end if;
  if p_seq > v_next then raise exception 'sequence gap: expected %, got %', v_next, p_seq; end if;
  insert into public.expedition_moves (expedition_id, seat, seq, payload) values (p_id, v_seat, p_seq, p_payload)
  returning id into v_id;
  update public.expedition_members set last_seen = now() where expedition_id = p_id and user_id = auth.uid();
  return v_id;
end $$;

-- Everything after a cursor, oldest first, everyone's seats included (the
-- caller ignores its own). Capped so a stale phone catches up in pages.
create or replace function public.moves_since(p_id uuid, p_after bigint)
returns jsonb language plpgsql security definer set search_path = public as $$
declare rows jsonb;
begin
  if not public.is_expedition_member(p_id) then raise exception 'not a member'; end if;
  update public.expedition_members set last_seen = now() where expedition_id = p_id and user_id = auth.uid();
  select coalesce(jsonb_agg(jsonb_build_object('id', v.id, 'seat', v.seat, 'seq', v.seq, 'payload', v.payload) order by v.id), '[]'::jsonb)
    into rows
    from (select * from public.expedition_moves where expedition_id = p_id and id > p_after order by id limit 200) v;
  return rows;
end $$;

grant execute on function public.create_expedition_v2(text, text, integer, text, text, jsonb) to authenticated;
grant execute on function public.find_expedition(text)          to authenticated;
grant execute on function public.set_ready(uuid, boolean)       to authenticated;
grant execute on function public.leave_expedition(uuid)         to authenticated;
grant execute on function public.lobby(uuid)                    to authenticated;
grant execute on function public.post_move(uuid, integer, jsonb) to authenticated;
grant execute on function public.moves_since(uuid, bigint)      to authenticated;

-- A finished or abandoned lobby's moves are kept 7 days for replays, then swept.
create or replace function public.sweep_old_expeditions() returns integer
language plpgsql security definer set search_path = public as $$
declare n integer;
begin
  with gone as (
    delete from public.expeditions
     where (state in ('done', 'abandoned') and coalesce(ended_at, created_at) < now() - interval '7 days')
        or (state = 'open' and created_at < now() - interval '1 day')
        or (state = 'running' and coalesce(started_at, created_at) < now() - interval '2 days')
    returning 1)
  select count(*) into n from gone;
  return n;
end $$;
