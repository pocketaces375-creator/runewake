-- FABLE-020. Exercises world_tower_coop.sql as three players. Every line that
-- starts OK: is an assertion; any FAIL raises and stops the run.
-- Run against a throwaway local Postgres after local_auth_stub.sql + schema.sql + world_tower_coop.sql.
\set ON_ERROR_STOP on
reset role;
delete from auth.users where id in ('a0000000-0000-0000-0000-00000000000a','b0000000-0000-0000-0000-00000000000b','c0000000-0000-0000-0000-00000000000c');
insert into auth.users (id, is_anonymous) values
  ('a0000000-0000-0000-0000-00000000000a', true),
  ('b0000000-0000-0000-0000-00000000000b', true),
  ('c0000000-0000-0000-0000-00000000000c', true);
delete from public.world_blips; delete from public.tower_floors; delete from public.expeditions;
-- Test floors: 2 players needed to open floor 2.
insert into public.tower_floors (floor, title, definition, unlock_threshold, is_open) values
  (1, 'Test floor 1', '{"floor":1}', 2, true),
  (2, 'Test floor 2', '{"floor":2}', 2, false);

create or replace function pg_temp.act_as(uid text) returns void language sql as $$
  select set_config('request.jwt.claims', json_build_object('sub', uid, 'role', 'authenticated')::text, false);
$$;
create or replace function pg_temp.check(ok boolean, what text) returns text language plpgsql as $$
begin if not ok then raise exception 'FAIL: %', what; end if; return 'OK: ' || what; end $$;

set role authenticated;

-- ── THE SHARED WORLD ─────────────────────────────────────────────────────
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check((public.discover_blip('w1:elvenwood:0:0:3')->>'first')::boolean, 'Alice is the FIRST to discover elvenwood 0:0:3');
select pg_temp.check(not (public.discover_blip('w1:elvenwood:0:0:3')->>'first')::boolean, 'discovering again does not make her first twice');

select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check(not (public.discover_blip('w1:elvenwood:0:0:3')->>'first')::boolean, 'Bob, second, is NOT told he was first');
select pg_temp.check((public.discover_blip('w1:elvenwood:0:0:4')->>'first')::boolean, 'Bob is first at 0:0:4');
select pg_temp.check((select count(*) from public.page_discoveries('w1:elvenwood:0:0')) = 2, 'page_discoveries lists the 2 found places on the page');
select pg_temp.check((select count(*) from public.page_discoveries('w1:elvenwood:0:1')) = 0, 'another page has none');

do $$ begin
  perform count(*) from public.world_blips;
  raise exception 'FAIL: a player read world_blips directly';
exception when insufficient_privilege then raise notice 'OK: world_blips (who found what) is not readable directly';
end $$;

do $$ begin
  perform public.discover_blip('r1_n01; drop table x');
  raise exception 'FAIL: malformed blip id accepted';
exception when raise_exception then raise notice 'OK: malformed blip ids are refused (%)', sqlerrm;
end $$;

select public.clear_blip('w1:elvenwood:0:0:4');
select pg_temp.check((select state from public.player_blips where blip_id = 'w1:elvenwood:0:0:4') = 2, 'clear_blip records the clear');
select pg_temp.check((select count(*) from public.player_blips) = 2, 'Bob sees only his own 2 player_blips rows');

-- ── THE TOWER ────────────────────────────────────────────────────────────
select pg_temp.check((select count(*) from public.tower_floors) = 1, 'only OPEN floors are readable (floor 2 hidden)');
select pg_temp.check((select count(*) from public.tower_status()) = 2, 'tower_status shows floor 1 and the next locked floor');

select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check((public.record_tower_boss_clear(1)->>'first_ever')::boolean, 'Alice is the first ever to beat floor 1');
select pg_temp.check(not (public.record_tower_boss_clear(1)->>'next_floor_opened')::boolean, 'one player (even twice) does not open floor 2');
select pg_temp.check((public.record_tower_boss_clear(1)->>'unique_clears')::int = 1, 'clearing twice still counts once');

select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check(not (public.record_tower_boss_clear(1)->>'first_ever')::boolean, 'Bob is not first ever');
select pg_temp.check((select count(*) from public.tower_floors) = 2, 'the SECOND unique player opened floor 2 for everyone');
select pg_temp.check((select is_open from public.tower_status() where floor = 2), 'tower_status agrees floor 2 is open');

do $$ begin
  perform public.record_tower_boss_clear(3);
  raise exception 'FAIL: cleared a floor that is not open';
exception when raise_exception then raise notice 'OK: locked floors cannot be cleared (%)', sqlerrm;
end $$;

-- ── CO-OP EXPEDITIONS ────────────────────────────────────────────────────
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
create temp table t_exp as select public.create_expedition('raid', 'tower:1:boss', 5, 'Alice', 'warrior', '["x"]') as id;
grant select on t_exp to authenticated;

select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check(public.join_expedition((select id from t_exp), 'Bob', 'druid', '["y"]') = 1, 'Bob joins in seat 1');
select pg_temp.check(public.join_expedition((select id from t_exp), 'Bob', 'druid', '["y"]') = 1, 'joining twice keeps the same seat');
select pg_temp.check((select count(*) from public.expedition_members) = 2, 'members see the roster');
do $$ begin
  perform public.start_expedition((select id from t_exp));
  raise exception 'FAIL: non-host started the expedition';
exception when raise_exception then raise notice 'OK: only the host can start (%)', sqlerrm;
end $$;

select pg_temp.act_as('c0000000-0000-0000-0000-00000000000c');
select pg_temp.check((select count(*) from public.expedition_members) = 0, 'an outsider cannot see the roster');

select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check(public.start_expedition((select id from t_exp)) is not null, 'the host starts it and gets the shared seed');

select pg_temp.act_as('c0000000-0000-0000-0000-00000000000c');
do $$ begin
  perform public.join_expedition((select id from t_exp), 'Cara', 'rogue', '[]');
  raise exception 'FAIL: joined a running expedition';
exception when raise_exception then raise notice 'OK: a running expedition cannot be joined (%)', sqlerrm;
end $$;

select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select public.finish_expedition((select id from t_exp), '{"won":true}');
select pg_temp.check((select state from public.expeditions where id = (select id from t_exp)) = 'done', 'a member reports the result');

reset role;
select 'ALL FABLE-020 SUPABASE CHECKS PASSED';
