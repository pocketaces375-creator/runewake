-- FABLE-018. Minimal stand-in for what Supabase provides out of the box, so the real
-- schema.sql can be applied and RLS exercised locally. NOT deployed anywhere.
create schema if not exists auth;
create table if not exists auth.users (
  id uuid primary key default gen_random_uuid(),
  email text,
  is_anonymous boolean not null default false,
  raw_user_meta_data jsonb default '{}'::jsonb,
  created_at timestamptz default now()
);
-- Supabase's auth.uid() reads the JWT claims PostgREST sets per request.
create or replace function auth.uid() returns uuid language sql stable as $$
  select nullif(coalesce(nullif(current_setting('request.jwt.claims', true), ''), '{}')::jsonb->>'sub', '')::uuid
$$;
-- The role PostgREST switches to for a signed-in user.
do $$ begin
  if not exists (select 1 from pg_roles where rolname='authenticated') then create role authenticated nologin; end if;
  if not exists (select 1 from pg_roles where rolname='anon') then create role anon nologin; end if;
end $$;
grant usage on schema public to anon, authenticated;
