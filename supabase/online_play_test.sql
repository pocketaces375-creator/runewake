-- FABLE-038. Exercises online_play.sql as two players (Alice hosts, Bob joins by code)
-- and a stranger (Cara). Every line that starts OK: is an assertion; any FAIL stops the run.
-- Run after local_auth_stub.sql + schema.sql + world_tower_coop.sql + online_play.sql.
\set ON_ERROR_STOP on
reset role;
delete from auth.users where id in ('a0000000-0000-0000-0000-00000000000a','b0000000-0000-0000-0000-00000000000b','c0000000-0000-0000-0000-00000000000c');
insert into auth.users (id, is_anonymous) values
  ('a0000000-0000-0000-0000-00000000000a', true),
  ('b0000000-0000-0000-0000-00000000000b', true),
  ('c0000000-0000-0000-0000-00000000000c', true);
delete from public.expeditions;

create or replace function pg_temp.act_as(uid text) returns void language sql as $$
  select set_config('request.jwt.claims', json_build_object('sub', uid, 'role', 'authenticated')::text, false);
$$;
create or replace function pg_temp.check(ok boolean, what text) returns text language plpgsql as $$
begin if not ok then raise exception 'FAIL: %', what; end if; return 'OK: ' || what; end $$;

set role authenticated;

-- ── host + code ───────────────────────────────────────────────────────────
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
create temp table t as select public.create_expedition_v2('pvp1v1', 'pvp', 2, 'Alice', 'battlemage', '["a","b"]'::jsonb) as r;
select pg_temp.check((select length(r->>'code') = 6 from t), 'hosting returns a 6-character code');
select pg_temp.check((select (r->>'code') !~ '[01OIL]' from t), 'the code has no confusable characters');
select pg_temp.check((select (public.lobby((r->>'id')::uuid)->>'state') = 'open' from t), 'the lobby is open');
select pg_temp.check((select jsonb_array_length(public.lobby((r->>'id')::uuid)->'members') = 1 from t), 'the host is its only member');

-- hosting again abandons the first lobby
select public.create_expedition_v2('pvp1v1', 'pvp', 2, 'Alice', 'battlemage', '["a","b"]'::jsonb) as r2 \gset
select pg_temp.check((select (public.lobby((r->>'id')::uuid)->>'state') = 'abandoned' from t), 'hosting a second time abandons the first lobby');
delete from t; insert into t select :'r2'::jsonb;

-- ── find + join ───────────────────────────────────────────────────────────
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check((select public.find_expedition(lower(r->>'code'))->>'host_name' = 'Alice' from t), 'Bob finds the lobby by code (any case)');
select pg_temp.check(public.find_expedition('ZZZZZZ') is null, 'a wrong code finds nothing');
select pg_temp.check((select public.join_expedition((r->>'id')::uuid, 'Bob', 'druid', '["c","d"]'::jsonb) = 1 from t), 'Bob takes seat 1');
select pg_temp.check((select jsonb_array_length(public.lobby((r->>'id')::uuid)->'members') = 2 from t), 'the lobby shows both');
select pg_temp.act_as('c0000000-0000-0000-0000-00000000000c');
do $$ begin
  perform public.join_expedition((select (r->>'id')::uuid from t), 'Cara', 'rogue', '[]'::jsonb);
  raise exception 'FAIL: a third player joined a 1v1';
exception when others then
  if sqlerrm like 'FAIL:%' then raise; end if;
  raise notice 'OK: a 1v1 is full at two (%)', sqlerrm;
end $$;

-- ── ready, start ──────────────────────────────────────────────────────────
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select public.set_ready((select (r->>'id')::uuid from t), true);
select pg_temp.check((select (public.lobby((r->>'id')::uuid)->'members'->1->>'status') = 'ready' from t), 'Bob is ready');
do $$ begin
  perform public.post_move((select (r->>'id')::uuid from t), 0, '{"t":"move"}'::jsonb);
  raise exception 'FAIL: a move was accepted before the start';
exception when others then
  if sqlerrm like 'FAIL:%' then raise; end if;
  raise notice 'OK: no moves before the host starts (%)', sqlerrm;
end $$;
do $$ begin
  perform public.start_expedition((select (r->>'id')::uuid from t));
  raise exception 'FAIL: a guest started the match';
exception when others then
  if sqlerrm like 'FAIL:%' then raise; end if;
  raise notice 'OK: only the host can start';
end $$;

select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check((select public.start_expedition((r->>'id')::uuid) is not null from t), 'the host starts and gets a seed');
select pg_temp.check((select (public.lobby((r->>'id')::uuid)->>'seed') is not null from t), 'the seed is in the lobby for everyone');
select pg_temp.check((select public.find_expedition(r->>'code')->>'state' = 'running' from t), 'the code still finds it while running');

-- ── moves ─────────────────────────────────────────────────────────────────
select pg_temp.check((select public.post_move((r->>'id')::uuid, 0, '{"t":"move","seat":0,"seq":0}'::jsonb) > 0 from t), 'Alice posts move 0');
select pg_temp.check((select public.post_move((r->>'id')::uuid, 1, '{"t":"move","seat":0,"seq":1}'::jsonb) > 0 from t), 'Alice posts move 1');
select pg_temp.check((select public.post_move((r->>'id')::uuid, 1, '{"t":"move","seat":0,"seq":1}'::jsonb)
                       = public.post_move((r->>'id')::uuid, 1, '{"t":"move","seat":0,"seq":1}'::jsonb) from t), 'retrying move 1 is idempotent');
do $$ begin
  perform public.post_move((select (r->>'id')::uuid from t), 5, '{"t":"move"}'::jsonb);
  raise exception 'FAIL: a sequence gap was accepted';
exception when others then
  if sqlerrm like 'FAIL:%' then raise; end if;
  raise notice 'OK: a sequence gap is refused (%)', sqlerrm;
end $$;

select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check((select jsonb_array_length(public.moves_since((r->>'id')::uuid, 0)) = 2 from t), 'Bob sees both of Alice''s moves');
select pg_temp.check((select (public.moves_since((r->>'id')::uuid, 0)->0->>'seat')::int = 0 from t), 'the seat on a move is the poster''s, from the membership');
select pg_temp.check((select public.post_move((r->>'id')::uuid, 0, '{"t":"move","seat":1,"seq":0}'::jsonb) > 0 from t), 'Bob posts his move 0');
select pg_temp.check((select jsonb_array_length(public.moves_since((r->>'id')::uuid,
        (public.moves_since((r->>'id')::uuid, 0)->1->>'id')::bigint)) = 1 from t), 'the cursor returns only what is newer');

-- ── a stranger sees nothing ───────────────────────────────────────────────
select pg_temp.act_as('c0000000-0000-0000-0000-00000000000c');
do $$ begin
  perform public.moves_since((select (r->>'id')::uuid from t), 0);
  raise exception 'FAIL: a stranger read the move log';
exception when others then
  if sqlerrm like 'FAIL:%' then raise; end if;
  raise notice 'OK: a stranger cannot read the moves';
end $$;
select pg_temp.check((select count(*) from public.expedition_moves) = 0, 'a stranger sees no move rows directly (RLS)');

-- ── leave + finish ────────────────────────────────────────────────────────
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select public.leave_expedition((select (r->>'id')::uuid from t));
select pg_temp.check((select (public.lobby((r->>'id')::uuid)->'members'->1->>'status') = 'left' from t), 'leaving a running match marks the seat left');
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select public.finish_expedition((select (r->>'id')::uuid from t), '{"winner":0}'::jsonb);
select pg_temp.check((select (public.lobby((r->>'id')::uuid)->>'state') = 'done' from t), 'the match is done');
select pg_temp.check((select public.find_expedition(r->>'code') is null from t), 'a finished match''s code is free again');

reset role;
select 'ALL FABLE-038 SUPABASE CHECKS PASSED';
