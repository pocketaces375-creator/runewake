-- FABLE-022: make sure the app (anon or signed-in) can write crash reports and duel exit traces.
-- Safe to re-run. Reading stays closed: there is no select policy.
grant insert on public.crash_reports to anon, authenticated;
