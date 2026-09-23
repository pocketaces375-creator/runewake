#!/usr/bin/env bash
# tools/supabase_schema_check.sh — FABLE-018
# Apply supabase/schema.sql to a throwaway local Postgres (with a stub of the
# auth schema Supabase provides), twice (idempotency), then run rls_test.sql
# and assert every isolation check. Needs postgresql-16 installed; no network.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PGBIN="${PGBIN:-/usr/lib/postgresql/16/bin}"
PORT="${PGPORT_CHECK:-5499}"
D="$(mktemp -d /tmp/rwpg.XXXXXX)"
trap 'su postgres -c "$PGBIN/pg_ctl -D $D/data stop -m immediate" >/dev/null 2>&1 || true; rm -rf "$D"' EXIT
chown postgres:postgres "$D" 2>/dev/null || true
su postgres -c "$PGBIN/initdb -D $D/data -A trust -U postgres" >/dev/null
su postgres -c "$PGBIN/pg_ctl -D $D/data -l $D/log -o '-p $PORT -k /tmp -c listen_addresses=' start" >/dev/null
sleep 1
P="psql -h /tmp -p $PORT -U postgres -v ON_ERROR_STOP=1 -q"
$P -c "create database rw;" >/dev/null
$P -d rw -f "$ROOT/supabase/local_auth_stub.sql" >/dev/null
$P -d rw -f "$ROOT/supabase/schema.sql" 2>&1 | grep -v NOTICE || true
$P -d rw -f "$ROOT/supabase/schema.sql" 2>&1 | grep -v NOTICE || true
echo "  schema applied twice (idempotent)"
$P -d rw -c "grant all on all tables in schema public to anon, authenticated;" >/dev/null
OUT=$($P -d rw -tA -f "$ROOT/supabase/rls_test.sql" 2>&1)
echo "$OUT" | grep -E "OK:|must be 0|sees|created" | sed 's/^psql:[^:]*:[0-9]*: NOTICE:  //; s/^/  /'
FAILS=$(echo "$OUT" | grep -cE "RLS FAILED|FAIL:|ERROR" || true)
if [ "$FAILS" -gt 0 ]; then echo "  ✗ $FAILS failure(s)"; echo "$OUT" | grep -E "RLS FAILED|FAIL:|ERROR"; exit 1; fi
MUSTZERO=$(echo "$OUT" | grep -E "must be 0" | grep -vcE " 0 " || true)
if [ "$MUSTZERO" -gt 0 ]; then echo "  ✗ a 'must be 0' line was not 0"; exit 1; fi
echo "  ✓ supabase/schema.sql: every RLS assertion holds"

# FABLE-020: shared world, Tower, co-op (applied twice for idempotency).
if [ -f "$ROOT/supabase/world_tower_coop.sql" ]; then
  $P -d rw -f "$ROOT/supabase/world_tower_coop.sql" 2>&1 | grep -v NOTICE || true
  $P -d rw -f "$ROOT/supabase/world_tower_coop.sql" 2>&1 | grep -v NOTICE || true
  OUT2=$($P -d rw -tA -f "$ROOT/supabase/world_tower_coop_test.sql" 2>&1) || { echo "$OUT2" | tail -5; echo "  ✗ world/tower/coop checks failed"; exit 1; }
  echo "$OUT2" | grep -E "OK:" | sed 's/^psql:[^:]*:[0-9]*: NOTICE:  //; s/^/  /'
  echo "$OUT2" | grep -q "ALL FABLE-020 SUPABASE CHECKS PASSED" || { echo "  ✗ world/tower/coop checks did not finish"; exit 1; }
  echo "  ✓ supabase/world_tower_coop.sql: every world/tower/co-op assertion holds"
fi
