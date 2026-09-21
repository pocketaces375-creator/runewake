using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Xunit;

namespace Runewake.Tests.Supabase;

/// <summary>
/// FABLE-018 — accounts and cloud save. Mock HttpMessageHandler, no network.
/// This file is generated from the same assertions Fable ran under a bare
/// console harness (93/93 green) — same cases, same expectations, under xunit.
/// </summary>
public class AccountsTests
{
    private static readonly SupabaseConfig Cfg = new() { Url = "https://x.supabase.co", AnonKey = "anon-key" };

    private static void AssertTrue(bool cond, string what) => Assert.True(cond, what);
    private static void AssertEq<A>(A got, A want, string what) => Assert.True(EqualityComparer<A>.Default.Equals(got, want), $"{what} (got {got}, want {want})");

    /// <summary>A scripted Supabase: route → (status, body). Records every request.</summary>
    private sealed class FakeSupabase : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        public readonly List<string> Bodies = new();
        public Func<HttpRequestMessage, string, (int, string)> Route = (_, __) => (404, "{}");
        public bool Offline = false;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("no route to host");
            var body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
            Requests.Add(req); Bodies.Add(body);
            var (status, text) = Route(req, body);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static string SessionJson(string uid, bool anon, string? email = null, long expiresAt = 9999999999) =>
        $"{{\"access_token\":\"jwt-{uid}\",\"refresh_token\":\"rt-{uid}\",\"expires_in\":3600,\"expires_at\":{expiresAt},\"user\":{{\"id\":\"{uid}\",\"email\":{(email == null ? "null" : "\"" + email + "\"")},\"is_anonymous\":{anon.ToString().ToLower()}}}}}";

    [Fact]
    public async Task SupabaseSession_labels_and_expiry()
    {
        await Task.CompletedTask;        {
            var s = new SupabaseSession { UserId = "3f2a9b..." , AccessToken = "x", ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30 };
            AssertEq(s.DisplayLabel(), "Guest 3F2A", "guest label from user id");
            s.Email = "adam@example.com"; AssertEq(s.DisplayLabel(), "adam@example.com", "label prefers email");
            AssertTrue(s.NeedsRefresh(), "30s left counts as needs-refresh (60s margin)");
            s.ExpiresAtUnix += 3600; AssertTrue(!s.NeedsRefresh(), "an hour left does not");
        }
    }

    [Fact]
    public async Task SupabaseAuth_SignInAnonymously()
    {
        await Task.CompletedTask;        {
            var fake = new FakeSupabase { Route = (r, b) => (200, SessionJson("u-anon-1", true)) };
            var auth = new SupabaseAuth(Cfg, new HttpClient(fake));
            var res = await auth.SignInAnonymously();
            AssertTrue(res.Ok, "anonymous sign-in succeeds");
            AssertEq(res.Session!.UserId, "u-anon-1", "user id parsed");
            AssertTrue(res.Session.IsAnonymous, "flagged anonymous");
            AssertEq(res.Session.RefreshToken, "rt-u-anon-1", "refresh token kept");
            var req = fake.Requests[0];
            AssertEq(req.RequestUri!.PathAndQuery, "/auth/v1/signup", "hits /auth/v1/signup");
            AssertEq(fake.Bodies[0], "{}", "empty body = anonymous");
            AssertEq(req.Headers.GetValues("apikey").First(), "anon-key", "apikey header");
            AssertEq(req.Headers.GetValues("Authorization").First(), "Bearer anon-key", "bearer is anon key before sign-in");
        }
        {
            var fake = new FakeSupabase { Route = (r, b) => (422, "{\"code\":422,\"error_code\":\"anonymous_provider_disabled\",\"msg\":\"Anonymous sign-ins are disabled\"}") };
            var res = await new SupabaseAuth(Cfg, new HttpClient(fake)).SignInAnonymously();
            AssertTrue(!res.Ok, "dashboard toggle off → failure, not crash");
            AssertEq(res.Error, "Anonymous sign-ins are disabled in the Supabase dashboard", "…and the error names the actual fix");
        }
        {
            var fake = new FakeSupabase { Offline = true };
            var res = await new SupabaseAuth(Cfg, new HttpClient(fake)).SignInAnonymously();
            AssertTrue(!res.Ok && res.Error == "No connection" && res.Status == 0, "offline → 'No connection', status 0");
        }
        {
            var res = await new SupabaseAuth(new SupabaseConfig(), new HttpClient(new FakeSupabase())).SignInAnonymously();
            AssertTrue(!res.Ok && res.Error == "Accounts not configured", "unconfigured → no request, clean failure");
        }
    }

    [Fact]
    public async Task SupabaseAuth_Refresh()
    {
        await Task.CompletedTask;        {
            var old = new SupabaseSession { UserId = "u1", AccessToken = "stale", RefreshToken = "rt-old", Email = "kept@example.com" };
            var fake = new FakeSupabase { Route = (r, b) => (200, "{\"access_token\":\"fresh\",\"refresh_token\":\"\",\"expires_in\":3600,\"user\":{\"id\":\"u1\",\"is_anonymous\":false}}") };
            var res = await new SupabaseAuth(Cfg, new HttpClient(fake)).Refresh(old);
            AssertTrue(res.Ok, "refresh ok");
            AssertEq(fake.Requests[0].RequestUri!.PathAndQuery, "/auth/v1/token?grant_type=refresh_token", "refresh endpoint");
            AssertTrue(fake.Bodies[0].Contains("\"refresh_token\":\"rt-old\""), "sends the old refresh token");
            AssertEq(res.Session!.AccessToken, "fresh", "new access token");
            AssertEq(res.Session.RefreshToken, "rt-old", "empty refresh in reply → keeps old one (rotation off)");
            AssertEq(res.Session.Email, "kept@example.com", "email carried over when reply omits it");
            AssertTrue(res.Session.ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3000, "expires_at computed from expires_in when absent");
        }
    }

    [Fact]
    public async Task Link_email_guest_recoverable()
    {
        await Task.CompletedTask;        {
            var guest = new SupabaseSession { UserId = "u-g", AccessToken = "jwt-g", RefreshToken = "rt-g", IsAnonymous = true };
            var fake = new FakeSupabase();
            fake.Route = (r, b) =>
            {
                if (r.Method == HttpMethod.Put && r.RequestUri!.PathAndQuery == "/auth/v1/user") return (200, "{\"id\":\"u-g\",\"new_email\":\"a@b.co\"}");
                if (r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/auth/v1/verify") return (200, SessionJson("u-g", false, "a@b.co"));
                return (404, "{}");
            };
            var auth = new SupabaseAuth(Cfg, new HttpClient(fake));
            var r1 = await auth.LinkEmail(guest, "  a@b.co ");
            AssertTrue(r1.Ok, "link request accepted");
            AssertEq(fake.Requests[0].Headers.GetValues("Authorization").First(), "Bearer jwt-g", "PUT /user carries the USER's jwt, not the anon key");
            AssertTrue(fake.Bodies[0].Contains("\"email\":\"a@b.co\""), "email trimmed and sent");
            var r2 = await auth.ConfirmLinkEmail(guest, "a@b.co", "123456");
            AssertTrue(r2.Ok, "confirm ok");
            AssertTrue(fake.Bodies[1].Contains("\"type\":\"email_change\"") && fake.Bodies[1].Contains("\"token\":\"123456\""), "verify type=email_change with the code");
            AssertEq(r2.Session!.UserId, "u-g", "SAME user id after linking — the save does not move");
            AssertTrue(!r2.Session.IsAnonymous && r2.Session.Email == "a@b.co", "no longer anonymous, email set");

            var bad = await auth.LinkEmail(guest, "not-an-email");
            AssertTrue(!bad.Ok && fake.Requests.Count == 2, "bad email rejected locally, no request sent");
            var shortCode = await auth.ConfirmLinkEmail(guest, "a@b.co", "12");
            AssertTrue(!shortCode.Ok && fake.Requests.Count == 2, "short code rejected locally");
        }
        {
            var fake = new FakeSupabase { Route = (r, b) => (403, "{\"code\":403,\"error_code\":\"otp_expired\",\"msg\":\"Token has expired or is invalid\"}") };
            var res = await new SupabaseAuth(Cfg, new HttpClient(fake)).ConfirmLinkEmail(new SupabaseSession { UserId = "u", AccessToken = "j" }, "a@b.co", "000000");
            AssertEq(res.Error, "Wrong or expired code", "expired otp → player-readable");
        }
    }

    [Fact]
    public async Task Sign_in_on_another_phone()
    {
        await Task.CompletedTask;        {
            var fake = new FakeSupabase();
            fake.Route = (r, b) =>
            {
                if (r.RequestUri!.PathAndQuery == "/auth/v1/otp") return (200, "{}");
                if (r.RequestUri!.PathAndQuery == "/auth/v1/verify") return (200, SessionJson("u-orig", false, "a@b.co"));
                return (404, "{}");
            };
            var auth = new SupabaseAuth(Cfg, new HttpClient(fake));
            var r1 = await auth.SendSignInCode("a@b.co");
            AssertTrue(r1.Ok, "code requested");
            AssertTrue(fake.Bodies[0].Contains("\"create_user\":false"), "create_user=false — a typo cannot mint an empty account");
            var r2 = await auth.ConfirmSignInCode("a@b.co", "654321");
            AssertTrue(r2.Ok && r2.Session!.UserId == "u-orig", "verify type=email returns the ORIGINAL account's session");
            AssertTrue(fake.Bodies[1].Contains("\"type\":\"email\""), "verify type=email");
        }
        {
            var fake = new FakeSupabase { Route = (r, b) => (422, "{\"msg\":\"Signups not allowed for otp\"}") };
            var res = await new SupabaseAuth(Cfg, new HttpClient(fake)).SendSignInCode("nobody@b.co");
            AssertEq(res.Error, "No account with that email", "unknown email → says so");
        }
    }

    [Fact]
    public async Task ProgressionSnapshot_round_trip()
    {
        await Task.CompletedTask;        {
            var s = new ProgressionState { Shards = 120, DigCharges = 3, RuneDust = 55, GlobalDiscoveryIndex = 7, HasCompletedTutorial = true, DelverLevel = 4, DelverXp = 230, SavedRunePageJson = "{\"slots\":[1]}", ShopRotationDay = 9, ArenaWins = 2, ArenaLosses = 1 };
            s.ClearedNodes.Add("r1_n3"); s.ClearedNodes.Add("r1_n1");
            s.Collection["emb_c_x"] = 2; s.Collection["tid_r_y"] = 1;
            s.Fragments["ember"] = 4;
            s.OwnedRuneIds.Add("rune_a"); s.UnlockedTools.Add("brush");
            s.DiscoveredRelics.Add(new LostRelicInstance { RelicInstanceId = "rel-1", CardId = "emb_c_x", AcquirerName = "Adam", AcquiredAt = "2026-09-21", Site = "beach", DiscoveryIndex = 7, EngravingStyle = "ornate" });
            s.DeckCardIds.AddRange(new[] { "a", "b" });
            s.SavedDecks["Burn"] = new List<string> { "a", "a", "c" };
            s.Tutorial = new TutorialState { CurrentStep = (TutorialStep)2, IsComplete = false };
            s.SeenCardIds.Add("emb_c_x");

            var json = ProgressionSnapshot.FromState(s).ToJson();
            var back = ProgressionSnapshot.FromJson(json)!;
            var s2 = new ProgressionState();
            s2.ClearedNodes.Add("garbage-that-must-be-cleared");
            back.ApplyTo(s2);

            AssertEq(s2.Shards, 120, "shards"); AssertEq(s2.DigCharges, 3, "dig charges"); AssertEq(s2.RuneDust, 55, "rune dust");
            AssertTrue(s2.ClearedNodes.SetEquals(s.ClearedNodes) && s2.ClearedNodes.Count == 2, "cleared nodes replaced, garbage gone");
            AssertTrue(s2.Collection.Count == 2 && s2.Collection["emb_c_x"] == 2, "collection counts");
            AssertEq(s2.Fragments["ember"], 4, "fragments");
            AssertTrue(s2.OwnedRuneIds.Contains("rune_a") && s2.UnlockedTools.Contains("brush"), "runes + tools");
            AssertTrue(s2.DiscoveredRelics.Count == 1 && s2.DiscoveredRelics[0].RelicInstanceId == "rel-1" && s2.DiscoveredRelics[0].EngravingStyle == "ornate" && s2.DiscoveredRelics[0].DiscoveryIndex == 7, "relic fields intact");
            AssertTrue(s2.DeckCardIds.SequenceEqual(new[] { "a", "b" }), "deck order kept");
            AssertTrue(s2.SavedDecks["Burn"].SequenceEqual(new[] { "a", "a", "c" }), "saved decks incl. duplicates");
            AssertEq(s2.GlobalDiscoveryIndex, 7, "discovery index"); AssertTrue(s2.HasCompletedTutorial, "tutorial done");
            AssertEq(s2.DelverLevel, 4, "level"); AssertEq(s2.DelverXp, 230, "xp");
            AssertEq(s2.SavedRunePageJson, "{\"slots\":[1]}", "rune page json");
            AssertTrue(s2.Tutorial != null && (int)s2.Tutorial.CurrentStep == 2 && !s2.Tutorial.IsComplete, "tutorial state");
            AssertEq(s2.ShopRotationDay, 9, "shop day"); AssertEq(s2.ArenaWins, 2, "arena w"); AssertEq(s2.ArenaLosses, 1, "arena l");
            AssertTrue(s2.SeenCardIds.Contains("emb_c_x"), "seen cards");
            AssertTrue(ProgressionSnapshot.FromState(s2).ToJson() == json, "second round trip is byte-identical (deterministic ordering)");

            // Field-by-field completeness guard: every settable/public prop of ProgressionState must appear in the snapshot type.
            var stateProps = typeof(ProgressionState).GetProperties().Select(p => p.Name).Where(n => n != "Version").ToHashSet();
            var snapProps = typeof(ProgressionSnapshot).GetProperties().Select(p => p.Name).ToHashSet();
            snapProps.Add("Tutorial"); // represented as TutorialStep + TutorialComplete
            var missing = stateProps.Where(n => !snapProps.Contains(n)).ToList();
            AssertTrue(missing.Count == 0, "every ProgressionState property is carried by the snapshot" + (missing.Count > 0 ? " — MISSING: " + string.Join(",", missing) : ""));
        }
        {
            AssertTrue(ProgressionSnapshot.FromJson("{\"v\":1,\"shards\":5,\"collection\":null}") is { } p && p.Collection.Count == 0 && p.Shards == 5, "null collections are null-proofed");
            AssertTrue(ProgressionSnapshot.FromJson("{\"v\":99}") == null, "a newer-version blob is refused, not guessed at");
            AssertTrue(ProgressionSnapshot.FromJson("not json") == null, "garbage → null");
            AssertTrue(ProgressionSnapshot.FromJson("{\"v\":1}")!.IsEmptyProgress, "fresh blob is empty progress");
            var tut = new ProgressionState { Tutorial = null };
            var snapNoTut = ProgressionSnapshot.FromJson(ProgressionSnapshot.FromState(tut).ToJson())!;
            var applied = new ProgressionState { Tutorial = new TutorialState() }; snapNoTut.ApplyTo(applied);
            AssertTrue(applied.Tutorial == null, "null tutorial survives the round trip as null");
        }
    }

    [Fact]
    public async Task CloudSaveBundle_round_trip()
    {
        await Task.CompletedTask;        {
            var b = new CloudSaveBundle { ProfilesJson = "{\"Profiles\":[{\"Slot\":0}]}", DecksJson = "[]" };
            b.Slots["0"] = ProgressionSnapshot.FromState(new ProgressionState { Shards = 9 });
            b.Slots["2"] = ProgressionSnapshot.FromState(new ProgressionState());
            var back = CloudSaveBundle.FromJson(b.ToJson())!;
            AssertTrue(back.Slots.Count == 2 && back.Slots["0"].Shards == 9, "slots keyed by index string");
            AssertEq(back.ProfilesJson, "{\"Profiles\":[{\"Slot\":0}]}", "opaque profiles json preserved");
            AssertTrue(!back.IsEmptyProgress, "bundle with shards is not empty");
            AssertTrue(new CloudSaveBundle().IsEmptyProgress, "empty bundle is empty");
        }
    }

    [Fact]
    public async Task CloudSaveSync_Fetch_Push()
    {
        await Task.CompletedTask;        {
            var sess = new SupabaseSession { UserId = "u1", AccessToken = "jwt-u1" };
            var fake = new FakeSupabase { Route = (r, b) => (200, "[]") };
            var f = await new CloudSaveSync(Cfg, new HttpClient(fake)).Fetch(sess);
            AssertTrue(f.Ok && f.Save == null, "no row → Ok with null save (not an error)");
            AssertTrue(fake.Requests[0].RequestUri!.PathAndQuery.StartsWith("/rest/v1/player_saves?user_id=eq.u1"), "filters by user id");
            AssertEq(fake.Requests[0].Headers.GetValues("Authorization").First(), "Bearer jwt-u1", "user jwt on REST");
        }
        {
            var sess = new SupabaseSession { UserId = "u1", AccessToken = "jwt-u1" };
            var bundleJson = new CloudSaveBundle { Slots = { ["0"] = ProgressionSnapshot.FromState(new ProgressionState { Shards = 77 }) } }.ToJson();
            var fake = new FakeSupabase { Route = (r, b) => (200, $"[{{\"user_id\":\"u1\",\"save_version\":1,\"save_json\":{bundleJson},\"device_id\":\"phone-A\",\"app_version\":\"0.9\",\"updated_at\":\"2026-09-21T10:00:00.123456+00:00\"}}]") };
            var f = await new CloudSaveSync(Cfg, new HttpClient(fake)).Fetch(sess);
            AssertTrue(f.Ok && f.Save != null, "row parsed");
            AssertEq(f.Save!.Bundle.Slots["0"].Shards, 77, "jsonb column → bundle");
            AssertEq(f.Save.DeviceId, "phone-A", "device id");
            AssertEq(f.Save.UpdatedAt, DateTimeOffset.Parse("2026-09-21T10:00:00.123456+00:00"), "updated_at parsed with micros + tz");
        }
        {
            var sess = new SupabaseSession { UserId = "u1", AccessToken = "jwt-u1" };
            var fake = new FakeSupabase { Route = (r, b) => (201, "[{\"user_id\":\"u1\",\"save_version\":1,\"save_json\":{},\"updated_at\":\"2026-09-21T11:00:00+00:00\"}]") };
            var bundle = new CloudSaveBundle { Slots = { ["0"] = ProgressionSnapshot.FromState(new ProgressionState { Shards = 5 }) } };
            var p = await new CloudSaveSync(Cfg, new HttpClient(fake)).Push(sess, bundle, "phone-B", "0.9.1");
            AssertTrue(p.Ok, "push ok");
            AssertEq(p.UpdatedAt, DateTimeOffset.Parse("2026-09-21T11:00:00+00:00"), "server stamp returned");
            var req = fake.Requests[0];
            AssertEq(req.Method, HttpMethod.Post, "POST");
            AssertTrue(req.Headers.GetValues("Prefer").First().Contains("resolution=merge-duplicates"), "upsert header");
            AssertTrue(fake.Bodies[0].Contains("\"user_id\":\"u1\"") && fake.Bodies[0].Contains("\"device_id\":\"phone-B\"") && fake.Bodies[0].Contains("\"shards\":5"), "payload has user, device, and the save as nested json (not a string)");
            AssertTrue(!fake.Bodies[0].Contains("\\\"shards\\\""), "save_json is real json, not an escaped string");
        }
        {
            var fake = new FakeSupabase { Route = (r, b) => (401, "{\"message\":\"JWT expired\"}") };
            var p = await new CloudSaveSync(Cfg, new HttpClient(fake)).Push(new SupabaseSession { UserId = "u", AccessToken = "j" }, new CloudSaveBundle(), "d", "v");
            AssertTrue(!p.Ok && p.Status == 401, "401 surfaces as failure with status");
        }
    }

    [Fact]
    public async Task CloudSaveSync_Decide_the_merge_rule()
    {
        await Task.CompletedTask;        {
            var t0 = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
            CloudSaveSync.CloudSave At(DateTimeOffset t) => new() { UpdatedAt = t };
            AssertEq(CloudSaveSync.Decide(null, null, false, true), CloudSaveSync.Decision.NoOp, "no cloud, nothing local → NoOp");
            AssertEq(CloudSaveSync.Decide(null, null, true, false), CloudSaveSync.Decision.PushLocal, "no cloud, local play → PushLocal");
            AssertEq(CloudSaveSync.Decide(At(t0), null, false, true), CloudSaveSync.Decision.PullCloud, "reinstall (never synced, empty) → PullCloud");
            AssertEq(CloudSaveSync.Decide(At(t0), null, false, false), CloudSaveSync.Decision.PullCloud, "never synced, local not dirty → PullCloud");
            AssertEq(CloudSaveSync.Decide(At(t0), null, true, false), CloudSaveSync.Decision.Conflict, "never synced but played here → Conflict (ask)");
            AssertEq(CloudSaveSync.Decide(At(t0), t0, false, false), CloudSaveSync.Decision.NoOp, "in sync → NoOp");
            AssertEq(CloudSaveSync.Decide(At(t0), t0, true, false), CloudSaveSync.Decision.PushLocalNewer, "only local moved → push");
            AssertEq(CloudSaveSync.Decide(At(t0.AddMinutes(5)), t0, false, false), CloudSaveSync.Decision.PullCloud, "only cloud moved → pull");
            AssertEq(CloudSaveSync.Decide(At(t0.AddMinutes(5)), t0, true, false), CloudSaveSync.Decision.Conflict, "both moved → Conflict");
            AssertEq(CloudSaveSync.Decide(At(t0.AddMilliseconds(400)), t0, true, false), CloudSaveSync.Decision.PushLocalNewer, "sub-second stamp jitter is not 'cloud moved'");
        }
    }
}
