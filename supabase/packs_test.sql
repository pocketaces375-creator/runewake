-- FABLE-061. Exercises packs.sql. Every OK: line is an assertion; any FAIL stops the run.
-- Run after local_auth_stub.sql + schema.sql + packs.sql (tools/supabase_packs_check.sh does all of it).
\set ON_ERROR_STOP on
reset role;
delete from public.pack_grants; delete from public.pack_purchases; delete from public.card_discoveries; delete from public.pack_catalog;
delete from auth.users where id in ('a0000000-0000-0000-0000-00000000000a','b0000000-0000-0000-0000-00000000000b');
insert into auth.users (id, is_anonymous) values
  ('a0000000-0000-0000-0000-00000000000a', false),
  ('b0000000-0000-0000-0000-00000000000b', false);

create or replace function pg_temp.act_as(uid text) returns void language sql as $$
  select set_config('request.jwt.claims', json_build_object('sub', uid, 'role', 'authenticated')::text, false);
$$;
create or replace function pg_temp.check(ok boolean, what text) returns text language plpgsql as $$
begin if not ok then raise exception 'FAIL: %', what; end if; return 'OK: ' || what; end $$;

-- a small world: old map cards of every rarity, a fresh one, a tower one, an excluded relic
insert into public.pack_catalog (card_id, rarity, excluded) values
  ('vrd_c_old_common', 'COMMON', false), ('emb_c_old_common2', 'COMMON', false),
  ('vrd_u_old_uncommon', 'UNCOMMON', false),
  ('vrd_r_old_rare', 'RARE', false), ('tid_r_old_rare2', 'RARE', false),
  ('vrd_x_old_relic', 'RELIC', false),
  ('hol_c_fresh_common', 'COMMON', false),
  ('hol_r_tower_rare', 'RARE', false),
  ('dwn_c_lost_relic', 'COMMON', true);
insert into public.card_discoveries (card_id, source, discovered_at) values
  ('vrd_c_old_common', 'map',  now() - interval '120 days'),
  ('emb_c_old_common2', 'seed', now() - interval '91 days'),
  ('vrd_r_old_rare', 'map',  now() - interval '100 days'),
  ('tid_r_old_rare2', 'map',  now() - interval '95 days'),
  ('vrd_x_old_relic', 'map',  now() - interval '200 days'),
  ('hol_c_fresh_common', 'map', now() - interval '30 days'),
  ('hol_r_tower_rare', 'tower', now() - interval '300 days'),
  ('dwn_c_lost_relic', 'map', now() - interval '300 days');

-- ── the shop's view, before any Uncommon is old enough ──────────────────────
set role anon;
select pg_temp.check((public.pack_status()->>'can_sell')::boolean = false, 'no Uncommon in the pool yet: packs cannot be sold');
select pg_temp.check((public.pack_status()->>'commons')::int = 2, 'two Commons are old enough (fresh, tower and excluded ones are not)');
select pg_temp.check((public.pack_status()->>'waiting')::int = 1, 'one card is still inside its 90 days');
select pg_temp.check(jsonb_array_length(public.pack_status()->'products') = 2, 'the shop lists both products');

-- ── discovery ───────────────────────────────────────────────────────────────
set role authenticated;
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check(public.discover_cards(array['vrd_u_old_uncommon','vrd_c_old_common','NOT A CARD'], 'map') = 1,
  'Alice discovers one new card (the known one and the junk id change nothing)');
select pg_temp.check(public.discover_cards(array['vrd_u_old_uncommon'], 'map') = 0, 'discovering it again changes nothing');
do $$ begin
  perform public.discover_cards(array['vrd_c_x'], 'shop');
  raise exception 'FAIL: a bad source was accepted';
exception when others then
  if sqlerrm like 'FAIL:%' then raise; end if;
end $$;
select 'OK: an unknown source is refused';
reset role;
select pg_temp.check((select source = 'map' and first_discoverer = 'a0000000-0000-0000-0000-00000000000a'
                      from public.card_discoveries where card_id = 'vrd_u_old_uncommon'), 'the first discoverer is recorded');
select pg_temp.check((select count(*) from public.pack_pool() where card_id = 'vrd_u_old_uncommon') = 0,
  'a card discovered today is NOT in packs');
update public.card_discoveries set discovered_at = now() - interval '90 days 1 minute' where card_id = 'vrd_u_old_uncommon';
select pg_temp.check((select count(*) from public.pack_pool() where card_id = 'vrd_u_old_uncommon') = 1,
  '90 days later it is');
select pg_temp.check((select count(*) from public.pack_pool() where card_id = 'hol_r_tower_rare') = 0, 'a card first found in the Tower never is');
select pg_temp.check((select count(*) from public.pack_pool() where card_id = 'dwn_c_lost_relic') = 0, 'an excluded card (Lost Relic) never is');
select pg_temp.check((public.pack_status()->>'can_sell')::boolean, 'now packs can be sold');

-- ── rolling ─────────────────────────────────────────────────────────────────
create temp table rolls as select (public.roll_pack()).* from generate_series(1, 400);
select pg_temp.check((select bool_and(cardinality(cards) = 3) from rolls), 'every pack has 3 cards');
select pg_temp.check((select bool_and(rarities[1] = 'COMMON' and rarities[2] = 'UNCOMMON' and rarities[3] in ('RARE','RELIC')) from rolls),
  'every pack is Common + Uncommon + Rare-or-Relic');
select pg_temp.check((select bool_and(cards[1] <> cards[2] and cards[2] <> cards[3] and cards[1] <> cards[3]) from rolls), 'no card twice in a pack');
select pg_temp.check(not exists (select 1 from rolls, unnest(cards) c where c not in (select card_id from public.pack_pool())),
  'every card rolled is in the pool');
select pg_temp.check((select count(*) filter (where rarities[3] = 'RELIC') between 16 and 72 from rolls),
  'about 1 in 10 Rares is a Relic (' || (select count(*) filter (where rarities[3] = 'RELIC') from rolls) || ' of 400)');

-- ── granting a purchase (the edge function, as the service role) ────────────
create temp table g1 as select public.grant_purchase('a0000000-0000-0000-0000-00000000000a', 'tok-1', 'rw_pack_5', 'GPA.1', true) as r;
select pg_temp.check((select (r->>'already')::boolean = false and jsonb_array_length(r->'grants') = 5 from g1), '5 packs for the 5-pack');
create temp table g2 as select public.grant_purchase('a0000000-0000-0000-0000-00000000000a', 'tok-1', 'rw_pack_5', 'GPA.1', true) as r;
select pg_temp.check((select (g2.r->>'already')::boolean and g2.r->'grants' = g1.r->'grants' from g1, g2),
  'the same token again returns the SAME packs, rolls nothing new');
select pg_temp.check((select count(*) from public.pack_grants) = 5, 'still only 5 packs exist');
do $$ begin
  perform public.grant_purchase('b0000000-0000-0000-0000-00000000000b', 'tok-1', 'rw_pack_5', 'GPA.1', false);
  raise exception 'FAIL: another account got the same purchase';
exception when others then
  if sqlerrm like 'FAIL:%' then raise; end if;
end $$;
select 'OK: another account cannot claim the same purchase token';
select pg_temp.check((public.grant_purchase('b0000000-0000-0000-0000-00000000000b', 'tok-2', 'rw_pack_1', 'GPA.2', false)->'grants'->0->'cards') is not null,
  'Bob buys a single pack');

-- ── a phone cannot give itself packs ────────────────────────────────────────
set role authenticated;
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
do $$ begin
  perform public.grant_purchase('a0000000-0000-0000-0000-00000000000a', 'tok-free', 'rw_pack_5', null, false);
  raise exception 'FAIL: a player called grant_purchase';
exception when insufficient_privilege then null;
end $$;
select 'OK: a player cannot call grant_purchase';
do $$ begin
  perform public.roll_pack();
  raise exception 'FAIL: a player rolled a pack';
exception when insufficient_privilege then null;
end $$;
select 'OK: a player cannot roll packs';

-- ── picking packs up on the phone ───────────────────────────────────────────
select pg_temp.check(jsonb_array_length(public.my_pending_packs()) = 5, 'Alice is owed 5 packs');
select pg_temp.check((select count(*) from public.pack_grants) = 5, 'and sees only her own packs (RLS)');
select pg_temp.act_as('b0000000-0000-0000-0000-00000000000b');
select pg_temp.check(jsonb_array_length(public.my_pending_packs()) = 1, 'Bob is owed his 1');
select pg_temp.check(public.mark_packs_applied(array(select id from public.pack_grants)) = 1, 'Bob can only mark his own pack as received');
reset role;
create temp table alice_ids as select array_agg(id) ids from public.pack_grants where user_id = 'a0000000-0000-0000-0000-00000000000a';
grant select on alice_ids to authenticated;
set role authenticated;
select pg_temp.check(public.mark_packs_applied((select ids from alice_ids)) = 0, 'Bob cannot mark Alice''s packs');
select pg_temp.act_as('a0000000-0000-0000-0000-00000000000a');
select pg_temp.check(public.mark_packs_applied((select ids from alice_ids)) = 5, 'Alice''s phone stores her 5 packs');
select pg_temp.check(jsonb_array_length(public.my_pending_packs()) = 0, 'and is owed nothing more');
select pg_temp.check(public.mark_packs_applied((select ids from alice_ids)) = 0, 'marking twice changes nothing');
reset role;
select 'OK: ALL PACK CHECKS PASSED';
