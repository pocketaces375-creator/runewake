-- ═══════════════════════════════════════════════════════════════════════════
--  Runewake — Supabase migration FABLE-061: card packs bought with real money
-- ═══════════════════════════════════════════════════════════════════════════
--
--  Run AFTER schema.sql. Then run pack_catalog.sql (the card list, regenerated
--  by tools/gen_pack_catalog.py whenever cards change). Idempotent: safe to re-run.
--
--  THE RULES (Trikzos)
--    • A pack is 3 cards: 1 Common, 1 Uncommon, 1 Rare — and 1 in 10 of those
--      Rares is a Relic instead. No card twice in one pack.
--    • Packs only circulate OLDER cards. A card enters packs 90 days after it was
--      first discovered on the map (by anyone). Cards first found in the Tower —
--      where content is pushed and the rarest things come from — never do.
--      Lost Relics and tokens never do either (pack_catalog.excluded).
--    • 1 pack = $0.25, 5 packs = $0.99 (Google Play products rw_pack_1 / rw_pack_5).
--
--  HOW A PURCHASE BECOMES CARDS
--    phone ── Google Play purchase ──► purchase token
--    phone ── token ──► edge function verify-pack-purchase (supabase/functions)
--      edge function ── asks Google: is this token really paid? ──► yes
--      edge function ── grant_purchase(user, token, product) ──► this file rolls
--                       the packs HERE, on the server, and records them
--      edge function ── tells Google to consume the token (it can be bought again)
--    phone ◄── the rolled packs ── stores them as unopened packs, then
--    phone ── mark_packs_applied(ids)
--  A pack that was paid for but never reached the phone (app killed, no signal)
--  stays in my_pending_packs() until the phone picks it up. A token can only
--  ever be granted once, to one account.
--
--  The client never writes these tables directly (RLS). Rolling and granting is
--  service-role only: a phone cannot give itself packs.
-- ───────────────────────────────────────────────────────────────────────────

create extension if not exists pgcrypto;

-- ── the cards that can ever be in a pack ────────────────────────────────────
create table if not exists public.pack_catalog (
  card_id    text primary key,
  rarity     text not null check (rarity in ('COMMON','UNCOMMON','RARE','RELIC')),
  excluded   boolean not null default false,          -- Lost Relics, tokens, tower-only: never in packs
  updated_at timestamptz not null default now()
);
alter table public.pack_catalog enable row level security;

-- ── when each card was first discovered, anywhere ───────────────────────────
create table if not exists public.card_discoveries (
  card_id          text primary key,
  source           text not null check (source in ('map','tower','seed')),
  first_discoverer uuid references auth.users(id) on delete set null,
  discovered_at    timestamptz not null default now()
);
alter table public.card_discoveries enable row level security;

-- ── what can be bought ──────────────────────────────────────────────────────
create table if not exists public.pack_products (
  product_id text primary key,                          -- the Google Play product id
  packs      integer not null check (packs between 1 and 50),
  active     boolean not null default true
);
alter table public.pack_products enable row level security;
insert into public.pack_products (product_id, packs) values ('rw_pack_1', 1), ('rw_pack_5', 5)
on conflict (product_id) do nothing;

-- ── every purchase token ever granted (one row per token, forever) ──────────
create table if not exists public.pack_purchases (
  purchase_token text primary key,
  user_id        uuid not null references auth.users(id) on delete cascade,
  product_id     text not null references public.pack_products(product_id),
  order_id       text,
  packs          integer not null,
  is_test        boolean not null default false,        -- a Play license-tester purchase (no money moved)
  created_at     timestamptz not null default now()
);
alter table public.pack_purchases enable row level security;
drop policy if exists "pack_purchases: read own" on public.pack_purchases;
create policy "pack_purchases: read own" on public.pack_purchases for select using (auth.uid() = user_id);

-- ── every pack ever rolled ──────────────────────────────────────────────────
create table if not exists public.pack_grants (
  id             uuid primary key default gen_random_uuid(),
  user_id        uuid not null references auth.users(id) on delete cascade,
  purchase_token text references public.pack_purchases(purchase_token) on delete cascade,
  cards          text[] not null,
  rarities       text[] not null,
  created_at     timestamptz not null default now(),
  applied_at     timestamptz                             -- the phone has it (null = still owed)
);
create index if not exists pack_grants_owed on public.pack_grants(user_id) where applied_at is null;
alter table public.pack_grants enable row level security;
drop policy if exists "pack_grants: read own" on public.pack_grants;
create policy "pack_grants: read own" on public.pack_grants for select using (auth.uid() = user_id);

grant select on public.pack_purchases, public.pack_grants to authenticated;

-- ═══════════════════════════════════════════════════════════════════════════
--  THE POOL
-- ═══════════════════════════════════════════════════════════════════════════

-- The cards a pack can contain right now.
create or replace function public.pack_pool()
returns table (card_id text, rarity text)
language sql stable security definer set search_path = public as $$
  select c.card_id, c.rarity
  from public.pack_catalog c
  join public.card_discoveries d on d.card_id = c.card_id
  where not c.excluded
    and d.source in ('map', 'seed')
    and d.discovered_at <= now() - interval '90 days'
$$;
revoke execute on function public.pack_pool() from public, anon, authenticated;

-- What the shop shows: how many cards are in packs, the odds, whether packs can be sold, and when the
-- next card joins. Readable by anyone (it is the odds disclosure Google Play requires).
create or replace function public.pack_status()
returns jsonb language sql stable security definer set search_path = public as $$
  with pool as (select * from public.pack_pool()),
  counts as (
    select count(*) filter (where rarity = 'COMMON')   as commons,
           count(*) filter (where rarity = 'UNCOMMON') as uncommons,
           count(*) filter (where rarity = 'RARE')     as rares,
           count(*) filter (where rarity = 'RELIC')    as relics
    from pool
  ),
  waiting as (
    select min(d.discovered_at + interval '90 days') as next_at, count(*) as n
    from public.pack_catalog c join public.card_discoveries d on d.card_id = c.card_id
    where not c.excluded and d.source in ('map','seed') and d.discovered_at > now() - interval '90 days'
  )
  select jsonb_build_object(
    'commons', counts.commons, 'uncommons', counts.uncommons, 'rares', counts.rares, 'relics', counts.relics,
    'can_sell', counts.commons > 0 and counts.uncommons > 0 and (counts.rares + counts.relics) > 0,
    'waiting', waiting.n,
    'next_card_at', waiting.next_at,
    'relic_chance', case when counts.relics = 0 then 0 when counts.rares = 0 then 1 else 0.10 end,
    'products', (select coalesce(jsonb_agg(jsonb_build_object('product_id', product_id, 'packs', packs) order by packs), '[]'::jsonb)
                 from public.pack_products where active)
  )
  from counts, waiting
$$;
grant execute on function public.pack_status() to anon, authenticated;

-- ═══════════════════════════════════════════════════════════════════════════
--  DISCOVERY (the phone reports cards it found on the map or in the Tower)
-- ═══════════════════════════════════════════════════════════════════════════

-- Records the FIRST discovery of each card; later discoveries change nothing.
-- Returns how many of these cards nobody had discovered before.
create or replace function public.discover_cards(p_card_ids text[], p_source text)
returns integer language plpgsql security definer set search_path = public as $$
declare v_n integer;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  if p_source not in ('map', 'tower') then raise exception 'bad source %', p_source; end if;
  if coalesce(array_length(p_card_ids, 1), 0) > 80 then raise exception 'too many cards at once'; end if;
  insert into public.card_discoveries (card_id, source, first_discoverer)
  select distinct x, p_source, auth.uid()
  from unnest(p_card_ids) as x
  where x ~ '^[a-z]{3}_[a-z]_[a-z0-9_]{2,60}$'
  on conflict (card_id) do nothing;
  get diagnostics v_n = row_count;
  return v_n;
end $$;
grant execute on function public.discover_cards(text[], text) to authenticated;

-- ═══════════════════════════════════════════════════════════════════════════
--  ROLLING AND GRANTING (server only)
-- ═══════════════════════════════════════════════════════════════════════════

-- One pack: a Common, an Uncommon, and a Rare (1 in 10: a Relic). Raises if the pool cannot fill it.
create or replace function public.roll_pack(out cards text[], out rarities text[])
language plpgsql volatile security definer set search_path = public as $$
declare v_c text; v_u text; v_r text; v_rr text;
begin
  select p.card_id into v_c from public.pack_pool() p where p.rarity = 'COMMON'   order by random() limit 1;
  select p.card_id into v_u from public.pack_pool() p where p.rarity = 'UNCOMMON' order by random() limit 1;
  v_rr := case when random() < 0.10 then 'RELIC' else 'RARE' end;
  select p.card_id into v_r from public.pack_pool() p where p.rarity = v_rr order by random() limit 1;
  if v_r is null then                                   -- no Relic (or no Rare) yet: take the other
    v_rr := case when v_rr = 'RELIC' then 'RARE' else 'RELIC' end;
    select p.card_id into v_r from public.pack_pool() p where p.rarity = v_rr order by random() limit 1;
  end if;
  if v_c is null or v_u is null or v_r is null then
    raise exception 'pack pool is not ready (needs a Common, an Uncommon and a Rare that are 90+ days old)';
  end if;
  cards := array[v_c, v_u, v_r];
  rarities := array['COMMON', 'UNCOMMON', v_rr];
end $$;
revoke execute on function public.roll_pack() from public, anon, authenticated;

-- Called by the edge function (service role) AFTER Google confirmed the payment. Idempotent per token.
create or replace function public.grant_purchase(p_user uuid, p_token text, p_product text, p_order text, p_is_test boolean default false)
returns jsonb language plpgsql volatile security definer set search_path = public as $$
declare v_owner uuid; v_packs integer; r record; i integer;
begin
  if p_user is null or coalesce(p_token, '') = '' then raise exception 'missing user or token'; end if;
  select user_id into v_owner from public.pack_purchases where purchase_token = p_token for update;
  if found then
    if v_owner <> p_user then raise exception 'this purchase belongs to another account'; end if;
    return jsonb_build_object('already', true, 'grants', (
      select coalesce(jsonb_agg(jsonb_build_object('id', g.id, 'cards', g.cards, 'rarities', g.rarities) order by g.created_at), '[]'::jsonb)
      from public.pack_grants g where g.purchase_token = p_token));
  end if;
  select packs into v_packs from public.pack_products where product_id = p_product and active;
  if v_packs is null then raise exception 'unknown product %', p_product; end if;
  insert into public.pack_purchases (purchase_token, user_id, product_id, order_id, packs, is_test)
  values (p_token, p_user, p_product, p_order, v_packs, coalesce(p_is_test, false));
  for i in 1..v_packs loop
    select * into r from public.roll_pack();
    insert into public.pack_grants (user_id, purchase_token, cards, rarities) values (p_user, p_token, r.cards, r.rarities);
  end loop;
  return jsonb_build_object('already', false, 'grants', (
    select jsonb_agg(jsonb_build_object('id', g.id, 'cards', g.cards, 'rarities', g.rarities) order by g.created_at)
    from public.pack_grants g where g.purchase_token = p_token));
end $$;
revoke execute on function public.grant_purchase(uuid, text, text, text, boolean) from public, anon, authenticated;
do $$ begin
  if exists (select 1 from pg_roles where rolname = 'service_role') then
    grant execute on function public.grant_purchase(uuid, text, text, text, boolean) to service_role;
    grant execute on function public.roll_pack(), public.pack_pool() to service_role;
  end if;
end $$;

-- Packs paid for that this account's phone has not picked up yet (a crash, no signal, a reinstall).
create or replace function public.my_pending_packs()
returns jsonb language sql stable security definer set search_path = public as $$
  select coalesce(jsonb_agg(jsonb_build_object('id', g.id, 'cards', g.cards, 'rarities', g.rarities) order by g.created_at), '[]'::jsonb)
  from public.pack_grants g where g.user_id = auth.uid() and g.applied_at is null
$$;
grant execute on function public.my_pending_packs() to authenticated;

-- The phone has stored these packs in its save: stop owing them.
create or replace function public.mark_packs_applied(p_ids uuid[])
returns integer language plpgsql security definer set search_path = public as $$
declare v_n integer;
begin
  if auth.uid() is null then raise exception 'not signed in'; end if;
  update public.pack_grants set applied_at = now()
  where user_id = auth.uid() and id = any(p_ids) and applied_at is null;
  get diagnostics v_n = row_count;
  return v_n;
end $$;
grant execute on function public.mark_packs_applied(uuid[]) to authenticated;
