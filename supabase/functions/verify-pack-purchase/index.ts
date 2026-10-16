// FABLE-061 — Supabase Edge Function: turn a Google Play purchase into packs.
// Deploy: Supabase dashboard → Edge Functions → "Deploy a new function" → name it verify-pack-purchase,
// paste this file AND core.ts (same folder). Or with the CLI: supabase functions deploy verify-pack-purchase
//
// Secrets (dashboard → Edge Functions → Secrets):
//   GOOGLE_SERVICE_ACCOUNT_JSON  the whole JSON key of the Play Console service account
//   ANDROID_PACKAGE_NAME         com.runewake.buriedage
// Supabase provides SUPABASE_URL, SUPABASE_ANON_KEY and SUPABASE_SERVICE_ROLE_KEY itself.
//
// All decisions are in core.ts (tested by tools/test_verify_pack_purchase.ts).
import { googleClient, handle, type Grant } from "./core.ts";

const URL_ = Deno.env.get("SUPABASE_URL")!;
const ANON = Deno.env.get("SUPABASE_ANON_KEY")!;
const SERVICE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const google = googleClient(Deno.env.get("GOOGLE_SERVICE_ACCOUNT_JSON") ?? "{}", Deno.env.get("ANDROID_PACKAGE_NAME") ?? "com.runewake.buriedage", fetch);

Deno.serve(async (req) => {
  const out = await handle(req.method, req.headers.get("Authorization"), await req.text(), {
    products: { rw_pack_1: 1, rw_pack_5: 5 },
    async getUser(jwt) {
      const r = await fetch(`${URL_}/auth/v1/user`, { headers: { apikey: ANON, Authorization: `Bearer ${jwt}` } });
      if (!r.ok) return null;
      const u = await r.json() as { id?: string };
      return u.id ? { id: u.id } : null;
    },
    googleGet: (p, t) => google.get(p, t),
    googleConsume: (p, t) => google.consume(p, t),
    async grant(a) {
      const r = await fetch(`${URL_}/rest/v1/rpc/grant_purchase`, {
        method: "POST",
        headers: { apikey: SERVICE, Authorization: `Bearer ${SERVICE}`, "Content-Type": "application/json" },
        body: JSON.stringify({ p_user: a.user, p_token: a.token, p_product: a.product, p_order: a.order, p_is_test: a.isTest }),
      });
      const text = await r.text();
      if (!r.ok) return { ok: false as const, error: text };
      const j = JSON.parse(text) as { already: boolean; grants: Grant[] };
      return { ok: true as const, already: j.already, grants: j.grants ?? [] };
    },
    log: (m) => console.log(`[verify-pack-purchase] ${m}`),
  });
  return new Response(JSON.stringify(out.body), { status: out.status, headers: { "Content-Type": "application/json" } });
});
