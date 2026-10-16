#!/usr/bin/env bash
# tools/supabase_packs_check.sh — FABLE-061
# Apply schema.sql + packs.sql + pack_catalog.sql to a throwaway local Postgres (twice, for idempotency),
# then run packs_test.sql and assert every check. Needs postgresql-16 installed; no network.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PGBIN="${PGBIN:-/usr/lib/postgresql/16/bin}"
PORT="${PGPORT_CHECK:-5498}"
D="$(mktemp -d /tmp/rwpg.XXXXXX)"
trap 'su postgres -c "$PGBIN/pg_ctl -D $D/data stop -m immediate" >/dev/null 2>&1 || true; rm -rf "$D"' EXIT
chown postgres:postgres "$D" 2>/dev/null || true
su postgres -c "$PGBIN/initdb -D $D/data -A trust -U postgres" >/dev/null
su postgres -c "$PGBIN/pg_ctl -D $D/data -l $D/log -o '-p $PORT -k /tmp -c listen_addresses=' start" >/dev/null
sleep 1
P="psql -h /tmp -p $PORT -U postgres -v ON_ERROR_STOP=1 -q"
$P -c "create database rw;" >/dev/null
$P -d rw -f "$ROOT/supabase/local_auth_stub.sql" >/dev/null
$P -d rw -c "do \$\$ begin if not exists (select 1 from pg_roles where rolname='service_role') then create role service_role nologin; end if; end \$\$;" >/dev/null
for f in schema.sql packs.sql pack_catalog.sql packs.sql pack_catalog.sql; do
  $P -d rw -f "$ROOT/supabase/$f" 2>&1 | grep -v NOTICE || true
done
[ -f "$ROOT/supabase/card_discoveries_seed.sql" ] && $P -d rw -f "$ROOT/supabase/card_discoveries_seed.sql" >/dev/null
echo "  packs.sql + pack_catalog.sql applied twice (idempotent), seed applied"
OUT=$($P -d rw -tA -f "$ROOT/supabase/packs_test.sql" 2>&1) || { echo "$OUT" | tail -5; echo "  ✗ packs_test.sql failed"; exit 1; }
echo "$OUT" | grep -E "^OK:" | sed 's/^/  /'
if echo "$OUT" | grep -qE "FAIL|ERROR"; then echo "  ✗ failures above"; exit 1; fi
echo "$OUT" | grep -q "ALL PACK CHECKS PASSED" || { echo "  ✗ did not reach the end"; exit 1; }
echo "  ✓ supabase/packs.sql: every pack check holds"
