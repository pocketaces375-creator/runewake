// FABLE-061 — the part of verify-pack-purchase that decides things. No Deno, no globals: every
// outside call comes in through `deps`, so tools/test_verify_pack_purchase.ts can run it under Node
// with a fake Google and a fake Supabase.
//
//   phone ── POST {product_id, purchase_token}  (Authorization: Bearer <player's Supabase JWT>)
//   1. who is this?                          Supabase Auth
//   2. did Google take the money for it?     Play Developer API purchases.products.get
//   3. was it bought from THIS account?      obfuscatedExternalAccountId == the player's id
//   4. roll + record the packs (once)        grant_purchase() in packs.sql, service role
//   5. let it be bought again                Play Developer API ...:consume
//   ◄── {ok, already, grants:[{id, cards[3], rarities[3]}], consumed}

export type Grant = { id: string; cards: string[]; rarities: string[] };

export type GooglePurchase = {
  purchaseState?: number;          // 0 purchased, 1 canceled, 2 pending
  consumptionState?: number;       // 0 not yet consumed, 1 consumed
  purchaseType?: number;           // 0 = test (license tester), absent for real money
  orderId?: string;
  obfuscatedExternalAccountId?: string;
};

export type Deps = {
  products: Record<string, number>;                                            // known product id → packs
  getUser(jwt: string): Promise<{ id: string } | null>;
  googleGet(productId: string, token: string): Promise<{ status: number; body: GooglePurchase | null }>;
  googleConsume(productId: string, token: string): Promise<boolean>;
  grant(args: { user: string; token: string; product: string; order: string | null; isTest: boolean }):
    Promise<{ ok: true; already: boolean; grants: Grant[] } | { ok: false; error: string }>;
  log?(msg: string): void;
};

export type Reply = { status: number; body: Record<string, unknown> };

const reply = (status: number, body: Record<string, unknown>): Reply => ({ status, body });

export async function handle(method: string, authHeader: string | null, rawBody: string, deps: Deps): Promise<Reply> {
  const log = deps.log ?? (() => {});
  if (method !== "POST") return reply(405, { ok: false, error: "POST only" });

  const jwt = (authHeader ?? "").replace(/^Bearer\s+/i, "").trim();
  if (!jwt) return reply(401, { ok: false, error: "sign in first" });
  const user = await deps.getUser(jwt);
  if (!user) return reply(401, { ok: false, error: "sign in first" });

  let body: { product_id?: unknown; purchase_token?: unknown };
  try { body = JSON.parse(rawBody || "{}"); } catch { return reply(400, { ok: false, error: "bad json" }); }
  const product = typeof body.product_id === "string" ? body.product_id : "";
  const token = typeof body.purchase_token === "string" ? body.purchase_token : "";
  if (!(product in deps.products)) return reply(400, { ok: false, error: "unknown product" });
  if (token.length < 10 || token.length > 4096) return reply(400, { ok: false, error: "bad purchase token" });

  // 2. Google is the only source of truth for "was this paid for"
  const g = await deps.googleGet(product, token);
  if (g.status === 404 || g.status === 410 || g.status === 400) return reply(400, { ok: false, error: "Google does not know this purchase" });
  if (g.status !== 200 || !g.body) {
    log(`google get failed: ${g.status}`);
    return reply(502, { ok: false, error: "could not reach Google Play — try again", retry: true });
  }
  const p = g.body;
  if (p.purchaseState === 2) return reply(202, { ok: false, pending: true, error: "payment pending — your packs arrive when it completes" });
  if (p.purchaseState !== 0) return reply(409, { ok: false, error: "this purchase was canceled" });

  // 3. a token bought on one account cannot be redeemed on another
  const bound = (p.obfuscatedExternalAccountId ?? "").trim();
  if (bound && bound !== user.id) {
    log(`account mismatch: bought as ${bound}, redeemed by ${user.id}`);
    return reply(403, { ok: false, error: "this purchase belongs to another account" });
  }

  // 4. roll and record — idempotent per token
  const granted = await deps.grant({ user: user.id, token, product, order: p.orderId ?? null, isTest: p.purchaseType === 0 });
  if (!granted.ok) {
    const e = granted.error;
    if (/another account/i.test(e)) return reply(403, { ok: false, error: "this purchase belongs to another account" });
    if (/not ready/i.test(e)) return reply(503, { ok: false, error: "packs are not available yet", retry: false });
    log(`grant failed: ${e}`);
    return reply(500, { ok: false, error: "could not record the purchase — try again", retry: true });
  }

  // 5. consume so the product can be bought again (a consumed token is already done)
  let consumed = p.consumptionState === 1;
  if (!consumed) consumed = await deps.googleConsume(product, token);
  if (!consumed) log(`consume failed for ${product} — the phone will consume it`);

  return reply(200, { ok: true, already: granted.already, grants: granted.grants, consumed });
}

// ── Google service-account OAuth (WebCrypto: same code in Deno and Node) ─────

const b64url = (bytes: Uint8Array): string => {
  let s = "";
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
};
const enc = (o: unknown) => b64url(new TextEncoder().encode(JSON.stringify(o)));

export async function signServiceAccountJwt(clientEmail: string, privateKeyPem: string, nowSec: number): Promise<string> {
  const pem = privateKeyPem.replace(/\\n/g, "\n").replace(/-----[^-]+-----/g, "").replace(/\s+/g, "");
  const der = Uint8Array.from(atob(pem), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("pkcs8", der, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"]);
  const unsigned = enc({ alg: "RS256", typ: "JWT" }) + "." + enc({
    iss: clientEmail,
    scope: "https://www.googleapis.com/auth/androidpublisher",
    aud: "https://oauth2.googleapis.com/token",
    iat: nowSec,
    exp: nowSec + 3600,
  });
  const sig = new Uint8Array(await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, new TextEncoder().encode(unsigned)));
  return unsigned + "." + b64url(sig);
}

export function googleClient(serviceAccountJson: string, packageName: string, fetchFn: typeof fetch) {
  const sa = JSON.parse(serviceAccountJson) as { client_email: string; private_key: string };
  let cached: { token: string; exp: number } | null = null;
  const base = `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${encodeURIComponent(packageName)}/purchases/products`;

  async function accessToken(): Promise<string> {
    const now = Math.floor(Date.now() / 1000);
    if (cached && cached.exp > now + 60) return cached.token;
    const assertion = await signServiceAccountJwt(sa.client_email, sa.private_key, now);
    const r = await fetchFn("https://oauth2.googleapis.com/token", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: "grant_type=" + encodeURIComponent("urn:ietf:params:oauth:grant-type:jwt-bearer") + "&assertion=" + assertion,
    });
    if (!r.ok) throw new Error(`google oauth ${r.status}: ${await r.text()}`);
    const j = await r.json() as { access_token: string; expires_in?: number };
    cached = { token: j.access_token, exp: now + (j.expires_in ?? 3600) };
    return cached.token;
  }

  return {
    async get(productId: string, token: string) {
      const r = await fetchFn(`${base}/${encodeURIComponent(productId)}/tokens/${encodeURIComponent(token)}`, {
        headers: { Authorization: `Bearer ${await accessToken()}` },
      });
      return { status: r.status, body: r.ok ? await r.json() as GooglePurchase : null };
    },
    async consume(productId: string, token: string) {
      const r = await fetchFn(`${base}/${encodeURIComponent(productId)}/tokens/${encodeURIComponent(token)}:consume`, {
        method: "POST",
        headers: { Authorization: `Bearer ${await accessToken()}` },
      });
      return r.ok;
    },
  };
}
