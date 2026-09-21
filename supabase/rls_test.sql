-- FABLE-018. Exercises every policy in schema.sql as two users and as the bare anon key.
-- Run by tools/supabase_schema_check.sh against a throwaway local Postgres. Every
-- "must be 0" line and every NOTICE beginning OK: is an assertion.
\set ON_ERROR_STOP on
-- Two users sign up (anonymous). The trigger must create their profiles.
insert into auth.users (id, is_anonymous) values
  ('11111111-1111-1111-1111-111111111111', true),
  ('22222222-2222-2222-2222-222222222222', true);
select 'profiles auto-created: ' || count(*) from public.profiles;

-- ── Act as ALICE (what PostgREST does per request) ──
set role authenticated;
select set_config('request.jwt.claims', '{"sub":"11111111-1111-1111-1111-111111111111","role":"authenticated"}', false);

insert into public.player_saves (user_id, save_version, save_json, device_id, app_version)
  values ('11111111-1111-1111-1111-111111111111', 1, '{"shards":30}', 'alice-phone', '0.9');
select 'alice sees her own save: ' || count(*) from public.player_saves;

-- Alice tries to write a save AS BOB. Must be refused.
do $$ begin
  insert into public.player_saves (user_id, save_version, save_json)
    values ('22222222-2222-2222-2222-222222222222', 1, '{"shards":999999}');
  raise exception 'RLS FAILED: alice wrote bob''s save';
exception when insufficient_privilege then
  raise notice 'OK: alice cannot write bob''s save (%)', sqlerrm;
end $$;

-- Alice tries to READ everyone's saves. Must see only her own.
select 'alice sees ' || count(*) || ' save(s) total' from public.player_saves;

-- Server stamps updated_at regardless of what the client sent
update public.player_saves set save_json = '{"shards":31}', updated_at = '2001-01-01'
  where user_id = '11111111-1111-1111-1111-111111111111';
select case when updated_at > now() - interval '1 minute'
  then 'OK: server stamped updated_at, ignored client 2001 value'
  else 'FAIL: client updated_at was accepted' end from public.player_saves;

-- Alice's profile got touched
select 'OK: profile last_seen bumped' from public.profiles
  where id='11111111-1111-1111-1111-111111111111' and last_seen_at > now() - interval '1 minute';

-- Alice can read her own profile only
select 'alice sees ' || count(*) || ' profile(s)' from public.profiles;

-- Alice mints a relic; then tries to mint one for bob
insert into public.relic_instances (relic_instance_id, user_id, card_id) values ('r1','11111111-1111-1111-1111-111111111111','emb_c_x');
do $$ begin
  insert into public.relic_instances (relic_instance_id, user_id, card_id) values ('r2','22222222-2222-2222-2222-222222222222','emb_c_x');
  raise exception 'RLS FAILED: alice minted a relic for bob';
exception when insufficient_privilege then
  raise notice 'OK: alice cannot mint for bob';
end $$;

-- ── Now BOB ──
select set_config('request.jwt.claims', '{"sub":"22222222-2222-2222-2222-222222222222","role":"authenticated"}', false);
select 'bob sees ' || count(*) || ' save(s)  (must be 0)' from public.player_saves;
select 'bob sees ' || count(*) || ' relic(s) (must be 0)' from public.relic_instances;

-- ── Anonymous role with NO jwt (the bare anon key, no user) ──
reset role; set role anon;
select set_config('request.jwt.claims', '', false);
set role anon;
select set_config('request.jwt.claims', '', false);
select 'bare anon key sees ' || count(*) || ' save(s) (must be 0)' from public.player_saves;
select 'bare anon key sees ' || count(*) || ' profile(s) (must be 0)' from public.profiles;
do $$ begin
  insert into public.player_saves (user_id, save_version, save_json) values ('11111111-1111-1111-1111-111111111111', 1, '{}');
  raise exception 'RLS FAILED: bare anon key wrote a save';
exception when insufficient_privilege then raise notice 'OK: bare anon key cannot write a save'; end $$;
insert into public.crash_reports (app_version, message) values ('0.9','boom');
select 'OK: bare anon key filed a crash report';
select 'bare anon key can read ' || count(*) || ' crash report(s) (must be 0)' from public.crash_reports;
reset role;
select 'ground truth as postgres: ' || (select count(*) from public.player_saves) || ' save, ' || (select count(*) from public.crash_reports) || ' crash report';
