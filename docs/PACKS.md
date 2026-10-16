# Card packs (FABLE-061)

Paid packs, sold through Google Play.

## The rules (Trikzos)

| | |
|---|---|
| A pack | 3 cards: 1 Common, 1 Uncommon, 1 Rare. 10% of the time the Rare is a Relic instead. No card twice in a pack. |
| Prices | 1 pack **$0.25** (`rw_pack_1`), 5 packs **$0.99** (`rw_pack_5`) |
| Which cards | Only older cards. A card enters packs **90 days after it is first discovered on the map** (by anyone). |
| Never in packs | Cards first discovered in the **Tower** (where content is pushed and the rarest cards come from), Lost Relics, tokens, and any card marked `"pack": false` in its JSON. |

The odds are shown in the shop before purchase. Google Play requires this for randomized items.

## How a purchase becomes cards

```
phone ── Google Play purchase sheet ──► purchase token
phone ── token ──► edge function verify-pack-purchase   (supabase/functions/verify-pack-purchase)
          ├─ asks Google: was this token paid, and bought from THIS account?
          ├─ grant_purchase(): rolls the packs ON THE SERVER and records them (supabase/packs.sql)
          └─ consumes the token, so the product can be bought again
phone ◄── the rolled packs → stored as unopened packs in the save → mark_packs_applied
```

- A token is granted once, to one account. Sending it again returns the same packs, and nothing new is rolled.
- If packs are paid for but never reach the phone (crash, no signal, reinstall), they are picked up the next time the shop opens (`my_pending_packs`).
- A phone cannot give itself packs. Rolling and granting run on the server, with service-role access only.
- Purchases go to the **active campaign**. Each campaign slot has its own collection.
- A debug build without the billing plugin offers **free preview packs**, so the opening can be tried. A release build without the plugin says packs come with the Play Store version.

Discovery: when a player beats a map place, every card they win is reported as `map`. Cards won in the Tower are reported as `tower`. Only the first discovery of each card counts.

## One-time setup

### 1. Supabase (dashboard)

1. Open the SQL editor and run, in order:
   - `supabase/packs.sql`
   - `supabase/pack_catalog.sql`
   - optionally, `supabase/card_discoveries_seed.sql`.

   The seed counts every card already in the game as discovered on the day it was added. With the seed, packs open on **Nov 5, 2026** with 60 cards (21 Common, 19 Uncommon, 15 Rare, 5 Relic). Drop 1 joins on Jan 1 and Drop 3 on Jan 9. Without the seed, the 90 days start the day players first find each card after packs ship.
2. Go to **Edge Functions**, then **Deploy a new function**, and name it `verify-pack-purchase`. Paste in `index.ts` and `core.ts`.
3. Go to **Edge Functions**, then **Secrets**, and add:
   - `GOOGLE_SERVICE_ACCOUNT_JSON`: the whole JSON key from step 2.4.
   - `ANDROID_PACKAGE_NAME`: `com.runewake.buriedage`

Whenever cards are added or a rarity changes, run `python3 tools/gen_pack_catalog.py` and run the new `supabase/pack_catalog.sql` again.

### 2. Google Play Console

1. Create the app with package name `com.runewake.buriedage`. Upload a signed **AAB** to **Internal testing**. In-app products cannot be created until a build with the billing library is uploaded.
2. Go to **Monetize**, then **In-app products**, and create two one-time products:
   - `rw_pack_1` at $0.25
   - `rw_pack_5` at $0.99

   Activate both.
3. Go to **Settings**, then **License testing**, and add the testers' Gmail addresses. Their purchases are free and recorded as `is_test`.
4. Set up the service account:
   1. In Google Cloud, create a service account and a JSON key.
   2. In Play Console, go to **Users and permissions**, invite the service account's email, and grant **View financial data** and **Manage orders and subscriptions**.
   3. Put the key into the Supabase secret from step 1.3.
5. In the content rating questionnaire, declare **in-app purchases** and **randomized items** (packs).

### 3. The mini PC build

- `bash tools/install_billing_plugin.sh` installs the GodotGooglePlayBilling plugin (3.3.0, Billing Library 9) into `client/addons` and enables it.
- `bash tools/install_billing_plugin.sh --disable` turns it off again.

## Known gaps before Play Store release

- **Target API level.** Google Play requires new apps to target **API 36** (since Aug 31, 2026). The export targets 34, which is the Godot 4.3 template default. Reaching 36 most likely means moving to a newer Godot, so it is its own task.
- **AAB.** Play takes AAB, not APK. The export preset still writes an APK (`gradle_build/export_format=0`).
- **Refunds.** Refunded or charged-back purchases are not taken back automatically yet. The fix is Google's Voided Purchases API; the `pack_purchases` table has what is needed.

## Tests

- `bash tools/supabase_packs_check.sh`: the pool, discovery, rolling odds, idempotent grants and RLS, on a throwaway Postgres.
- `node tools/test_verify_pack_purchase.ts`: the edge function's decisions, plus Google OAuth with a real RSA key.
- `tests/State/PacksTests.cs`: storing, opening and persisting packs, and the server conversation.
