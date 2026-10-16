// FABLE-061 — tests the edge function's core under Node (node tools/test_verify_pack_purchase.ts).
// A fake Google (with a real RSA service-account key, so the OAuth signature is really checked) and a
// fake Supabase. Exit code 0 = every check passed.
import { generateKeyPairSync, createVerify } from "node:crypto";
import { googleClient, handle, type Deps, type Grant } from "../supabase/functions/verify-pack-purchase/core.ts";

let fails = 0;
const ok = (cond: boolean, what: string) => { console.log(`${cond ? "OK" : "FAIL"}: ${what}`); if (!cond) fails++; };

const USER = "a0000000-0000-0000-0000-00000000000a";
const OTHER = "b0000000-0000-0000-0000-00000000000b";
const TOKEN = "tok-abcdefghijklmnop";

function fakeDeps(over: Partial<Deps> = {}, purchase: Record<string, unknown> = { purchaseState: 0, consumptionState: 0, orderId: "GPA.1" }) {
  const calls: string[] = [];
  const store = new Map<string, { user: string; grants: Grant[] }>();
  const deps: Deps = {
    products: { rw_pack_1: 1, rw_pack_5: 5 },
    async getUser(jwt) { return jwt === "good-jwt" ? { id: USER } : jwt === "other-jwt" ? { id: OTHER } : null; },
    async googleGet(p, t) { calls.push(`get ${p} ${t}`); return { status: 200, body: purchase }; },
    async googleConsume(p, t) { calls.push(`consume ${p} ${t}`); return true; },
    async grant(a) {
      calls.push(`grant ${a.user} ${a.product} test=${a.isTest}`);
      const have = store.get(a.token);
      if (have && have.user !== a.user) return { ok: false, error: "this purchase belongs to another account" };
      if (have) return { ok: true, already: true, grants: have.grants };
      const grants = Array.from({ length: deps.products[a.product] }, (_, i) => ({ id: `g${i}`, cards: ["c", "u", "r"], rarities: ["COMMON", "UNCOMMON", "RARE"] }));
      store.set(a.token, { user: a.user, grants });
      return { ok: true, already: false, grants };
    },
    ...over,
  };
  return { deps, calls };
}
const body = (product = "rw_pack_5", token = TOKEN) => JSON.stringify({ product_id: product, purchase_token: token });

// ── the decisions ───────────────────────────────────────────────────────────
{
  const { deps, calls } = fakeDeps();
  const r = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(r.status === 200 && (r.body.grants as Grant[]).length === 5 && r.body.consumed === true && r.body.already === false,
    "a paid 5-pack returns 5 packs and is consumed");
  ok(calls.join("|") === `get rw_pack_5 ${TOKEN}|grant ${USER} rw_pack_5 test=false|consume rw_pack_5 ${TOKEN}`, "Google first, then grant, then consume");
  const again = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(again.status === 200 && again.body.already === true && (again.body.grants as Grant[]).length === 5, "sending the same purchase again returns the same packs");
}
{
  const { deps } = fakeDeps();
  await handle("POST", "Bearer good-jwt", body(), deps);
  const r = await handle("POST", "Bearer other-jwt", body(), deps);
  ok(r.status === 403, "a second account cannot redeem the same purchase");
}
{
  const { deps, calls } = fakeDeps({}, { purchaseState: 0, consumptionState: 0, obfuscatedExternalAccountId: OTHER });
  const r = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(r.status === 403 && !calls.some((c) => c.startsWith("grant")), "a purchase made from another account is refused before anything is granted");
}
{
  const { deps, calls } = fakeDeps({}, { purchaseState: 2 });
  const r = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(r.status === 202 && r.body.pending === true && !calls.some((c) => c.startsWith("grant")), "a pending payment grants nothing yet");
}
{
  const { deps, calls } = fakeDeps({}, { purchaseState: 1 });
  const r = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(r.status === 409 && !calls.some((c) => c.startsWith("grant")), "a canceled purchase grants nothing");
}
{
  const { deps, calls } = fakeDeps({}, { purchaseState: 0, consumptionState: 1, purchaseType: 0 });
  const r = await handle("POST", "Bearer good-jwt", body("rw_pack_1"), deps);
  ok(r.status === 200 && r.body.consumed === true && !calls.some((c) => c.startsWith("consume")), "an already-consumed token is not consumed twice");
  ok(calls.some((c) => c.endsWith("test=true")), "a license-tester purchase is recorded as a test");
}
{
  const { deps } = fakeDeps({ googleGet: async () => ({ status: 404, body: null }) });
  ok((await handle("POST", "Bearer good-jwt", body(), deps)).status === 400, "a token Google never issued is refused");
}
{
  const { deps } = fakeDeps({ googleGet: async () => ({ status: 503, body: null }) });
  const r = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(r.status === 502 && r.body.retry === true, "Google down: try again later, nothing granted");
}
{
  const { deps, calls } = fakeDeps({ grant: async () => ({ ok: false, error: "pack pool is not ready (needs …)" }) });
  const r = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(r.status === 503 && !calls.some((c) => c.startsWith("consume")), "an empty pool grants nothing and does NOT consume (Google refunds it)");
}
{
  const { deps } = fakeDeps({ googleConsume: async () => false });
  const r = await handle("POST", "Bearer good-jwt", body(), deps);
  ok(r.status === 200 && r.body.consumed === false, "a failed consume still delivers the packs and tells the phone to consume");
}
{
  const { deps } = fakeDeps();
  ok((await handle("POST", null, body(), deps)).status === 401, "no sign-in: refused");
  ok((await handle("POST", "Bearer nope", body(), deps)).status === 401, "a bad sign-in: refused");
  ok((await handle("POST", "Bearer good-jwt", body("rw_pack_999"), deps)).status === 400, "an unknown product: refused");
  ok((await handle("POST", "Bearer good-jwt", "{not json", deps)).status === 400, "junk: refused");
  ok((await handle("GET", "Bearer good-jwt", body(), deps)).status === 405, "GET: refused");
}

// ── the Google client: OAuth with a real key, the right URLs ────────────────
{
  const { privateKey, publicKey } = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const pem = privateKey.export({ type: "pkcs8", format: "pem" }).toString();
  const sa = JSON.stringify({ client_email: "packs@runewake.iam.gserviceaccount.com", private_key: pem });
  const seen: string[] = [];
  let tokenCalls = 0;
  const fakeFetch = (async (url: string | URL, init?: RequestInit) => {
    const u = String(url);
    seen.push(`${init?.method ?? "GET"} ${u}`);
    if (u === "https://oauth2.googleapis.com/token") {
      tokenCalls++;
      const assertion = new URLSearchParams(String(init?.body)).get("assertion")!;
      const [h, p, s] = assertion.split(".");
      const v = createVerify("RSA-SHA256"); v.update(`${h}.${p}`);
      const valid = v.verify(publicKey, Buffer.from(s.replace(/-/g, "+").replace(/_/g, "/"), "base64"));
      const claims = JSON.parse(Buffer.from(p, "base64").toString());
      ok(valid, "the OAuth assertion is signed with the service account's key");
      ok(claims.scope === "https://www.googleapis.com/auth/androidpublisher" && claims.iss.startsWith("packs@"), "it asks for the androidpublisher scope as the service account");
      return new Response(JSON.stringify({ access_token: "at-1", expires_in: 3600 }), { status: 200 });
    }
    const auth = (init?.headers as Record<string, string>)?.Authorization;
    if (auth !== "Bearer at-1") return new Response("no", { status: 401 });
    if (u.endsWith(":consume")) return new Response("", { status: 200 });
    return new Response(JSON.stringify({ purchaseState: 0, consumptionState: 0, orderId: "GPA.9" }), { status: 200 });
  }) as typeof fetch;
  const g = googleClient(sa, "com.runewake.buriedage", fakeFetch);
  const got = await g.get("rw_pack_1", "tok/with+chars");
  ok(got.status === 200 && got.body?.orderId === "GPA.9", "purchases.products.get works with the access token");
  ok(seen.some((s) => s === "GET https://androidpublisher.googleapis.com/androidpublisher/v3/applications/com.runewake.buriedage/purchases/products/rw_pack_1/tokens/tok%2Fwith%2Bchars"),
    "the purchase URL is right and the token is escaped");
  ok(await g.consume("rw_pack_1", "tok"), "consume works");
  ok(tokenCalls === 1, "the access token is reused, not fetched per call");
}

console.log(fails === 0 ? "ALL EDGE FUNCTION CHECKS PASSED" : `${fails} FAILED`);
process.exit(fails === 0 ? 0 : 1);
