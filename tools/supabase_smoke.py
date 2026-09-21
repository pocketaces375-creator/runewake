#!/usr/bin/env python3
"""
tools/supabase_smoke.py — FABLE-018: prove the REAL Supabase project does what
the game needs, end to end, before a single APK carries its keys.

Talks to the same endpoints the client does, with the same headers, in the
same order, and asserts the same things the RLS test asserted locally — on
the live project. Standard library only; no supabase package.

    python3 tools/supabase_smoke.py                  # reads client/supabase_config.json
    python3 tools/supabase_smoke.py --config path.json
    python3 tools/supabase_smoke.py --keep           # do not delete the test users' rows

What it checks, in order:
  1. anonymous sign-in works              (dashboard toggle: Anonymous sign-ins)
  2. the profile row was auto-created     (trigger + SECURITY DEFINER)
  3. push a save, read it back            (RLS insert/select own)
  4. server stamps updated_at             (trigger ignores the client's value)
  5. upsert overwrites, doesn't duplicate (Prefer: resolution=merge-duplicates)
  6. a SECOND anonymous user sees nothing (RLS isolation — the whole point)
  7. bare anon key sees nothing           (no JWT → no rows)
  8. a crash report can be filed and cannot be read back
  9. refresh token works                  (session survives the hour)
 10. relic ledger insert + read own only

Exit 0 = every check passed. Exit 1 = the first failing check is named, with
the HTTP status and body, so the fix is obvious. Nothing here needs the
service-role key, on purpose: if it passes with the anon key, the app will.
"""
import argparse, json, sys, time, urllib.request, urllib.error, uuid, os

def load_config(path):
    with open(path) as f:
        cfg = json.load(f)
    url, key = cfg.get("url", "").rstrip("/"), cfg.get("anon_key", "")
    if not url or not key or "YOUR-PROJECT" in url or key.startswith("eyJ...your"):
        sys.exit(f"config at {path} is the example placeholder — fill in url and anon_key first")
    return url, key

class Client:
    def __init__(self, url, key):
        self.url, self.key = url, key
    def call(self, method, path, body=None, jwt=None, prefer=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(self.url + path, data=data, method=method)
        req.add_header("apikey", self.key)
        req.add_header("Authorization", "Bearer " + (jwt or self.key))
        req.add_header("Content-Type", "application/json")
        req.add_header("Accept", "application/json")
        if prefer: req.add_header("Prefer", prefer)
        try:
            with urllib.request.urlopen(req, timeout=20) as r:
                text = r.read().decode()
                return r.status, (json.loads(text) if text else None)
        except urllib.error.HTTPError as e:
            text = e.read().decode()
            try: return e.code, json.loads(text)
            except Exception: return e.code, text
        except Exception as e:
            return 0, str(e)

PASSED = 0
def check(cond, what, detail=""):
    global PASSED
    if cond:
        PASSED += 1
        print(f"  ✅ {what}")
    else:
        print(f"  ❌ {what}")
        if detail: print(f"     {detail}")
        print(f"\n{PASSED} passed before this. Fix the above and rerun.")
        sys.exit(1)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--config", default=os.path.join(os.path.dirname(__file__), "..", "client", "supabase_config.json"))
    ap.add_argument("--keep", action="store_true", help="leave the test users' rows in place")
    a = ap.parse_args()
    url, key = load_config(a.config)
    c = Client(url, key)
    print(f"═══ Supabase smoke — {url} ═══")

    # 1. anonymous sign-in
    st, body = c.call("POST", "/auth/v1/signup", {})
    check(st == 200 and isinstance(body, dict) and body.get("access_token"),
          "1. anonymous sign-in returns a session",
          f"status {st}: {body}\n     → Dashboard: Authentication → Providers → Anonymous sign-ins must be ON")
    alice_jwt, alice_rt, alice_id = body["access_token"], body["refresh_token"], body["user"]["id"]
    check(body["user"].get("is_anonymous") is True, "   user is flagged anonymous")

    # 2. profile auto-created by trigger
    st, rows = c.call("GET", f"/rest/v1/profiles?id=eq.{alice_id}&select=id,created_at", jwt=alice_jwt)
    check(st == 200 and isinstance(rows, list) and len(rows) == 1,
          "2. profiles row was auto-created for the new user",
          f"status {st}: {rows}\n     → schema.sql not applied, or the on_auth_user_created trigger is missing")

    # 3. push a save, read it back
    save = {"v": 1, "slots": {"0": {"v": 1, "shards": 30, "delver_level": 2, "cleared_nodes": ["r1_n1"]}}}
    st, rows = c.call("POST", "/rest/v1/player_saves",
                      {"user_id": alice_id, "save_version": 1, "save_json": save, "device_id": "smoke-A", "app_version": "smoke", "updated_at": "2001-01-01T00:00:00Z"},
                      jwt=alice_jwt, prefer="resolution=merge-duplicates,return=representation")
    check(st in (200, 201) and isinstance(rows, list) and len(rows) == 1,
          "3. push save (upsert) accepted", f"status {st}: {rows}")
    check(rows[0]["save_json"]["slots"]["0"]["shards"] == 30, "   save_json stored as real jsonb, read back intact")

    # 4. server stamped updated_at (we sent 2001)
    check(not str(rows[0]["updated_at"]).startswith("2001"),
          "4. server stamped updated_at (ignored the client's 2001 value)", f"updated_at = {rows[0]['updated_at']}")
    first_stamp = rows[0]["updated_at"]

    # 5. upsert overwrites, no duplicate row
    time.sleep(1.1)
    save["slots"]["0"]["shards"] = 31
    st, rows = c.call("POST", "/rest/v1/player_saves",
                      {"user_id": alice_id, "save_version": 1, "save_json": save, "device_id": "smoke-A", "app_version": "smoke"},
                      jwt=alice_jwt, prefer="resolution=merge-duplicates,return=representation")
    check(st in (200, 201) and rows[0]["save_json"]["slots"]["0"]["shards"] == 31, "5. second push overwrote the row", f"status {st}: {rows}")
    check(rows[0]["updated_at"] > first_stamp, "   updated_at advanced")
    st, rows = c.call("GET", f"/rest/v1/player_saves?select=user_id", jwt=alice_jwt)
    check(st == 200 and len(rows) == 1, "   exactly one row for this user (no duplicates)", f"{rows}")

    # 6. second user sees nothing
    st, body = c.call("POST", "/auth/v1/signup", {})
    check(st == 200 and body.get("access_token"), "6. second anonymous user created")
    bob_jwt, bob_id = body["access_token"], body["user"]["id"]
    st, rows = c.call("GET", "/rest/v1/player_saves?select=user_id,save_json", jwt=bob_jwt)
    check(st == 200 and rows == [], "   Bob cannot see Alice's save (RLS)", f"status {st}: {rows}\n     → RLS is OFF or the select policy is wrong. STOP. Do not ship.")
    st, rows = c.call("POST", "/rest/v1/player_saves",
                      {"user_id": alice_id, "save_version": 1, "save_json": {"v": 1, "slots": {}}},
                      jwt=bob_jwt, prefer="resolution=merge-duplicates")
    check(st in (401, 403), "   Bob cannot overwrite Alice's save (RLS)", f"status {st}: {rows}\n     → insert/update policy missing. STOP. Do not ship.")
    st, rows = c.call("GET", "/rest/v1/profiles?select=id", jwt=bob_jwt)
    check(st == 200 and len(rows) == 1 and rows[0]["id"] == bob_id, "   Bob sees only his own profile")

    # 7. bare anon key (no user)
    st, rows = c.call("GET", "/rest/v1/player_saves?select=user_id")
    check(st == 200 and rows == [], "7. bare anon key sees no saves", f"status {st}: {rows}")
    st, rows = c.call("GET", "/rest/v1/profiles?select=id")
    check(st == 200 and rows == [], "   bare anon key sees no profiles", f"status {st}: {rows}")

    # 8. crash report: write yes, read no
    st, rows = c.call("POST", "/rest/v1/crash_reports", {"app_version": "smoke", "platform": "smoke", "message": "smoke test — ignore"}, prefer="return=minimal")
    check(st in (200, 201), "8. bare anon key can file a crash report", f"status {st}: {rows}")
    st, rows = c.call("GET", "/rest/v1/crash_reports?select=id&limit=1")
    check(st == 200 and rows == [], "   …and cannot read them back", f"status {st}: {rows}")

    # 9. refresh
    st, body = c.call("POST", "/auth/v1/token?grant_type=refresh_token", {"refresh_token": alice_rt})
    check(st == 200 and body.get("access_token") and body["user"]["id"] == alice_id, "9. refresh token yields a new session for the same user", f"status {st}: {body}")
    alice_jwt = body["access_token"]

    # 10. relic ledger
    rid = "smoke-" + uuid.uuid4().hex[:8]
    st, rows = c.call("POST", "/rest/v1/relic_instances",
                      [{"relic_instance_id": rid, "user_id": alice_id, "card_id": "emb_c_smoke", "discovery_index": 1}],
                      jwt=alice_jwt, prefer="resolution=merge-duplicates,return=representation")
    check(st in (200, 201), "10. relic insert (same shape RelicLedgerSync posts)", f"status {st}: {rows}")
    st, rows = c.call("GET", f"/rest/v1/relic_instances?user_id=eq.{alice_id}&select=relic_instance_id", jwt=alice_jwt)
    check(st == 200 and any(r["relic_instance_id"] == rid for r in rows), "    Alice reads her relic back")
    st, rows = c.call("GET", f"/rest/v1/relic_instances?user_id=eq.{alice_id}&select=relic_instance_id", jwt=bob_jwt)
    check(st == 200 and rows == [], "    Bob cannot read Alice's relics", f"{rows}")

    # cleanup (rows only; auth users are cleaned by Supabase's own anon-user reaper or by hand)
    if not a.keep:
        c.call("DELETE", f"/rest/v1/relic_instances?relic_instance_id=eq.{rid}", jwt=alice_jwt)
        print("  (test relic removed; the two anonymous users remain — delete from Authentication → Users if you like)")

    print(f"\n═══ {PASSED} checks passed. The project is ready for the APK. ═══")
    print(f"    test users: alice={alice_id}  bob={bob_id}")

if __name__ == "__main__":
    main()
