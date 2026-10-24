-- ═══════════════════════════════════════════════════════════════════════════
--  Runewake — Supabase migration FABLE-104: online play hardening
-- ═══════════════════════════════════════════════════════════════════════════
--
--  Run AFTER world_tower_coop.sql and online_play.sql (paste into the Supabase
--  SQL editor and run once). Idempotent: safe to re-run. The same two changes
--  are also made in those two files, so a fresh project gets them too.
--
--    1. expeditions can be read only by their members. The old policy also let
--       every signed-in player list every OPEN lobby (GET /rest/v1/expeditions
--       ?state=eq.open&select=code) - and the six-letter code is the only gate
--       on a private duel. find_expedition() is SECURITY DEFINER and does not
--       need the policy.
--    2. post_move accepts rows while the expedition is 'running' OR 'done'. The
--       winner's phone reports the result as the last move is made; that move
--       (and its hash) could be refused as "expedition is done", so the loser
--       never saw the end of the match.
-- ───────────────────────────────────────────────────────────────────────────

drop policy if exists "expeditions: read open or mine" on public.expeditions;
create policy "expeditions: read open or mine" on public.expeditions for select
  using (public.is_expedition_member(id));

create or replace function public.post_move(p_id uuid, p_seq integer, p_payload jsonb)
returns bigint language plpgsql security definer set search_path = public as $$
declare v_seat integer; v_state text; v_next integer; v_id bigint;
begin
  select m.seat into v_seat from public.expedition_members m where m.expedition_id = p_id and m.user_id = auth.uid();
  if v_seat is null then raise exception 'not a member'; end if;
  select state into v_state from public.expeditions where id = p_id;
  if v_state not in ('running', 'done') then raise exception 'expedition is %', v_state; end if;
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

grant execute on function public.post_move(uuid, integer, jsonb) to authenticated;
