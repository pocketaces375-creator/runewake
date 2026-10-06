-- FABLE-054. Exercises usernames.sql as Alice, Bob and Cara. Every OK: line is an assertion; any FAIL stops the run.
-- Run after local_auth_stub.sql + schema.sql + world_tower_coop.sql + online_play.sql + usernames.sql.
\set ON_ERROR_STOP on
reset role;
delete from auth.users where id in ('a0000000-0000-0000-0000-00000000000a','b0000000-0000-0000-0000-00000000000b','c0000000-0000-0000-0000-00000000000c');
insert into auth.users (id, is_anonymous) values
  ('a0000000-0000-0000-0000-00000000000a', false),
  ('b0000000-0000-0000-0000-00000000000b', false),
  ('c0000000-0000-0000-0000-00000000000c', false);
delete from public.expeditions;

create or replace function pg_temp.act_as(uid text) returns void language sql as $$
  select set_config('request.jwt.claims', json_build_object('sub', uid, 'role', 'authenticated')::text, false);
$$;
create or replace function pg_temp.check(ok boolean, what text) returns text language plpgsql as $$
begin if not ok then raise exception 'FAIL: %', what; end if; return 'OK: ' || what; end $$;

set role authenticated;

-- ── usernames ─────────────────────────────────────────────────────────────
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check(public.my_username() is null, 'a new account has no username');
select pg_temp.check(public.claim_username('ab') = 'invalid', 'too short is refused');
select pg_temp.check(public.claim_username('no!pe') = 'invalid', 'punctuation is refused');
select pg_temp.check(public.claim_username('Fictive') = 'ok', 'Alice claims Fictive');
select pg_temp.check(public.my_username() = 'Fictive', 'and it is hers');
select pg_temp.check(public.claim_username('Fictive') = 'ok', 'claiming her own name again is fine');
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check(public.claim_username('fictive') = 'taken', 'Bob cannot take it in another case');
select pg_temp.check(public.username_free('FICTIVE') = false, 'the live check says taken');
select pg_temp.check(public.username_free('Adam') = true, 'the live check says Adam is free');
select pg_temp.check(public.claim_username('  Adam  ') = 'ok', 'Bob claims Adam (trimmed)');
select pg_temp.check(public.my_username() = 'Adam', 'Bob is Adam');
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check(public.claim_username('Trikzos') = 'ok', 'Alice can change her name');
select pg_temp.act_as('c0000000-0000-0000-0000-00000000000c');
select pg_temp.check(public.claim_username('Fictive') = 'ok', 'her old name is free again');

-- ── challenges ────────────────────────────────────────────────────────────
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
create temp table t as select public.create_expedition_v2('pvp1v1', 'pvp', 2, 'Trikzos', 'battlemage', '["a","b"]'::jsonb) as r;
select pg_temp.check((select public.challenge_player('nobody here', (r->>'id')::uuid) = 'no_such_player' from t), 'an unknown name is reported');
select pg_temp.check((select public.challenge_player('trikzos', (r->>'id')::uuid) = 'self' from t), 'you cannot challenge yourself');
select pg_temp.check((select public.challenge_player('adam', (r->>'id')::uuid) = 'ok' from t), 'Alice challenges Adam (any case)');
select pg_temp.check((select public.challenge_player('adam', (r->>'id')::uuid) = 'ok' from t), 'challenging again replaces the first');
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check(jsonb_array_length(public.my_challenges()) = 1, 'Bob sees exactly one challenge');
select pg_temp.check(public.my_challenges()->0->>'from_name' = 'Trikzos', 'from Trikzos, by name');
select pg_temp.check((select public.my_challenges()->0->>'code' = r->>'code' from t), 'with the lobby code');
select pg_temp.check(public.challenge_player('trikzos', gen_random_uuid()) = 'no_lobby', 'a challenge needs your own open lobby');
select (public.my_challenges())->0->>'id' as cid \gset
select pg_temp.act_as('c0000000-0000-0000-0000-00000000000c');
select pg_temp.check(jsonb_array_length(public.my_challenges()) = 0, 'Cara sees none of it');
select pg_temp.check(public.answer_challenge(:'cid'::uuid, true) is null, 'Cara cannot answer Bob''s challenge');
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check((select public.answer_challenge(:'cid'::uuid, true) = r->>'code' from t), 'Bob accepts and gets the code to join');
select pg_temp.check(jsonb_array_length(public.my_challenges()) = 0, 'an answered challenge is gone');
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select public.create_expedition_v2('pvp1v1', 'pvp', 2, 'Trikzos', 'battlemage', '[]'::jsonb) as r2 \gset
select pg_temp.check(public.challenge_player('adam', (:'r2'::jsonb->>'id')::uuid) = 'ok', 'a new lobby, a new challenge');
select public.create_expedition_v2('pvp1v1', 'pvp', 2, 'Trikzos', 'battlemage', '[]'::jsonb) as r3 \gset
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check(jsonb_array_length(public.my_challenges()) = 0, 'a challenge to an abandoned lobby disappears');
do $$ begin
  perform 1 from public.duel_challenges;
  raise exception 'FAIL: players can read the challenge table directly';
exception when insufficient_privilege then null;
end $$;
select 'OK: players cannot read the challenge table directly';
select 'ALL FABLE-054 SUPABASE CHECKS PASSED';
