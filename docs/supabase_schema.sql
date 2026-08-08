-- ===================================================================
-- Runewake: The Buried Age — Supabase schema additions
-- ===================================================================

-- Crash reports (uploaded by the client on startup after a crash)
create table if not exists crash_reports (
  id uuid primary key default gen_random_uuid(),
  received_at timestamptz default now(),
  app_version text,
  platform text,
  exception_type text,
  message text,
  stack_trace text,
  godot_version text
);

-- No RLS on crash reports — append-only, no personal data
alter table crash_reports enable row level security;
create policy "insert only" on crash_reports for insert with check (true);