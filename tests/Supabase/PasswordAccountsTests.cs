using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Xunit;

namespace Runewake.Tests.Supabase;

/// <summary>
/// FABLE-ACCOUNTS-1 — email + password accounts and the "what happens to this
/// phone's progress when you sign in" rule. Scripted HttpMessageHandler, no network.
/// </summary>
public class PasswordAccountsTests
{
    private static readonly SupabaseConfig Cfg = new() { Url = "https://x.supabase.co", AnonKey = "anon-key" };

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        public readonly List<string> Bodies = new();
        public Func<HttpRequestMessage, string, (int, string)> Route = (_, __) => (404, "{}");
        public bool Offline;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("no route to host");
            var body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
            Requests.Add(req); Bodies.Add(body);
            var (status, text) = Route(req, body);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private static string SessionJson(string uid, string email) =>
        $"{{\"access_token\":\"jwt-{uid}\",\"refresh_token\":\"rt-{uid}\",\"expires_in\":3600,\"expires_at\":9999999999,\"user\":{{\"id\":\"{uid}\",\"email\":\"{email}\",\"is_anonymous\":false}}}}";

    private static SupabaseAuth Auth(Fake f) => new(Cfg, new HttpClient(f));

    [Fact]
    public async Task SignUp_with_confirm_email_off_returns_a_session()
    {
        var f = new Fake { Route = (r, b) => (200, SessionJson("u-new", "a@b.co")) };
        var res = await Auth(f).SignUpWithPassword(" a@b.co ", "hunter22");
        Assert.True(res.Ok);
        Assert.False(res.NeedsConfirmation);
        Assert.Equal("u-new", res.Session!.UserId);
        Assert.False(res.Session.IsAnonymous);
        Assert.Equal("a@b.co", res.Session.Email);
        Assert.Equal("/auth/v1/signup", f.Requests[0].RequestUri!.PathAndQuery);
        Assert.Contains("\"email\":\"a@b.co\"", f.Bodies[0]);
        Assert.Contains("\"password\":\"hunter22\"", f.Bodies[0]);
        Assert.Equal("Bearer anon-key", f.Requests[0].Headers.GetValues("Authorization").First());
    }

    [Fact]
    public async Task SignUp_with_confirm_email_on_asks_for_confirmation()
    {
        // Supabase answers a confirm-email sign-up with the bare user object, no tokens.
        var f = new Fake { Route = (r, b) => (200, "{\"id\":\"u-new\",\"email\":\"a@b.co\",\"confirmation_sent_at\":\"2026-10-04T00:00:00Z\",\"identities\":[{\"provider\":\"email\"}]}") };
        var res = await Auth(f).SignUpWithPassword("a@b.co", "hunter22");
        Assert.True(res.Ok);
        Assert.True(res.NeedsConfirmation);
        Assert.Null(res.Session);
    }

    [Fact]
    public async Task SignUp_for_an_email_that_already_has_an_account_says_so()
    {
        // With Confirm email on, an existing email comes back as a user with NO identities.
        var f = new Fake { Route = (r, b) => (200, "{\"id\":\"u-fake\",\"email\":\"a@b.co\",\"identities\":[]}") };
        var res = await Auth(f).SignUpWithPassword("a@b.co", "hunter22");
        Assert.False(res.Ok);
        Assert.Equal("That email already has an account — sign in instead", res.Error);

        // With Confirm email off, Supabase says it outright.
        var f2 = new Fake { Route = (r, b) => (422, "{\"code\":422,\"error_code\":\"user_already_exists\",\"msg\":\"User already registered\"}") };
        var res2 = await Auth(f2).SignUpWithPassword("a@b.co", "hunter22");
        Assert.Equal("That email already has an account — sign in instead", res2.Error);
    }

    [Fact]
    public async Task SignUp_checks_locally_before_sending()
    {
        var f = new Fake();
        Assert.Equal("That doesn't look like an email address", (await Auth(f).SignUpWithPassword("nope", "hunter22")).Error);
        Assert.Equal("Password must be at least 6 characters", (await Auth(f).SignUpWithPassword("a@b.co", "123")).Error);
        Assert.Empty(f.Requests);
        var off = new SupabaseAuth(new SupabaseConfig(), new HttpClient(f));
        Assert.Equal("Accounts not configured", (await off.SignUpWithPassword("a@b.co", "hunter22")).Error);
        Assert.Empty(f.Requests);
    }

    [Fact]
    public async Task SignUp_errors_are_readable()
    {
        async Task<string> Err(int status, string body) =>
            (await Auth(new Fake { Route = (r, b) => (status, body) }).SignUpWithPassword("a@b.co", "hunter22")).Error;
        Assert.Equal("New sign-ups are turned off in the Supabase dashboard", await Err(422, "{\"msg\":\"Signups not allowed for this instance\"}"));
        Assert.Equal("Email sign-in is turned off in the Supabase dashboard", await Err(422, "{\"error_code\":\"email_provider_disabled\",\"msg\":\"Email signups are disabled\"}"));
        // FABLE-ACCOUNTS-2: the email limit is per HOUR, and saying "wait a minute" sent people round in circles.
        Assert.StartsWith("The game can only send a few emails an hour", await Err(429, "{\"code\":429,\"error_code\":\"over_email_send_rate_limit\",\"msg\":\"email rate limit exceeded\"}"));
        Assert.Equal("Too many attempts — wait a minute", await Err(429, "{\"msg\":\"Request rate limit reached\"}"));
        Assert.Equal("Password should contain at least one character of each", await Err(422, "{\"error_code\":\"weak_password\",\"msg\":\"Password should contain at least one character of each\"}"));
        var offline = await Auth(new Fake { Offline = true }).SignUpWithPassword("a@b.co", "hunter22");
        Assert.True(offline.Error.StartsWith("No connection") && offline.Status == 0);
    }

    [Fact]
    public async Task SignIn_with_password()
    {
        var f = new Fake { Route = (r, b) => (200, SessionJson("u-1", "a@b.co")) };
        var res = await Auth(f).SignInWithPassword(" a@b.co", "hunter22");
        Assert.True(res.Ok);
        Assert.Equal("u-1", res.Session!.UserId);
        Assert.Equal("/auth/v1/token?grant_type=password", f.Requests[0].RequestUri!.PathAndQuery);
        Assert.Contains("\"email\":\"a@b.co\"", f.Bodies[0]);

        var wrong = await Auth(new Fake { Route = (r, b) => (400, "{\"error\":\"invalid_grant\",\"error_description\":\"Invalid login credentials\"}") }).SignInWithPassword("a@b.co", "nope123");
        Assert.Equal("Wrong email or password", wrong.Error);
        var unconfirmed = await Auth(new Fake { Route = (r, b) => (400, "{\"error_code\":\"email_not_confirmed\",\"msg\":\"Email not confirmed\"}") }).SignInWithPassword("a@b.co", "hunter22");
        Assert.Equal("Confirm your email first — tap the link we sent you", unconfirmed.Error);

        var empty = new Fake();
        Assert.Equal("Enter your password", (await Auth(empty).SignInWithPassword("a@b.co", "")).Error);
        Assert.Empty(empty.Requests);
    }

    [Fact]
    public async Task Resend_confirmation_and_set_password()
    {
        var f = new Fake { Route = (r, b) => (200, "{}") };
        var resend = await Auth(f).ResendConfirmation("a@b.co");
        Assert.True(resend.Ok);
        Assert.Equal("/auth/v1/resend", f.Requests[0].RequestUri!.PathAndQuery);
        Assert.Contains("\"type\":\"signup\"", f.Bodies[0]);

        var me = new SupabaseSession { UserId = "u-1", AccessToken = "jwt-u-1", Email = "a@b.co", IsAnonymous = false };
        var f2 = new Fake { Route = (r, b) => (200, "{\"id\":\"u-1\"}") };
        var set = await Auth(f2).SetPassword(me, "newpass1");
        Assert.True(set.Ok);
        Assert.Equal(HttpMethod.Put, f2.Requests[0].Method);
        Assert.Equal("/auth/v1/user", f2.Requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("Bearer jwt-u-1", f2.Requests[0].Headers.GetValues("Authorization").First());
        Assert.Contains("\"password\":\"newpass1\"", f2.Bodies[0]);

        Assert.Equal("Password must be at least 6 characters", (await Auth(f2).SetPassword(me, "x")).Error);
        Assert.Equal("Not signed in", (await Auth(f2).SetPassword(new SupabaseSession(), "newpass1")).Error);
        var same = await Auth(new Fake { Route = (r, b) => (422, "{\"error_code\":\"same_password\",\"msg\":\"New password should be different from the old password.\"}") }).SetPassword(me, "newpass1");
        Assert.Equal("That's already your password", same.Error);
    }

    // ── what happens to this phone's progress on sign-in ─────────────────

    private static CloudSaveBundle Bundle(Action<ProgressionState>? play = null)
    {
        var st = new ProgressionState();
        st.AddCard("vrd_c_root_warden", 2);          // every fresh install owns cards
        play?.Invoke(st);
        var b = new CloudSaveBundle();
        b.Slots["0"] = ProgressionSnapshot.FromState(st);
        return b;
    }

    private static CloudSaveSync.CloudSave Cloud(CloudSaveBundle b) =>
        new() { Bundle = CloudSaveBundle.FromJson(b.ToJson())!, UpdatedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void Fresh_install_with_a_full_collection_has_not_played()
    {
        Assert.False(Bundle().HasPlayed);
        Assert.False(Bundle().IsEmptyProgress);   // why HasPlayed exists: the collection alone says nothing
        Assert.True(Bundle(s => s.ClearedNodes.Add("r1_n1")).HasPlayed);
        Assert.True(Bundle(s => s.DelverXp = 10).HasPlayed);
        Assert.True(Bundle(s => s.ArenaWins = 1).HasPlayed);
    }

    [Fact]
    public void DecideOnSignIn_the_rules()
    {
        var fresh = Bundle();
        var played = Bundle(s => { s.ClearedNodes.Add("r1_n1"); s.DelverXp = 40; });
        var playedMore = Bundle(s => { s.ClearedNodes.Add("r1_n1"); s.ClearedNodes.Add("r1_n2"); s.DelverXp = 90; });

        // New account (no cloud row): this phone's progress becomes the account's.
        Assert.Equal(CloudSaveSync.SignInDecision.PushLocal, CloudSaveSync.DecideOnSignIn(null, played));
        Assert.Equal(CloudSaveSync.SignInDecision.PushLocal, CloudSaveSync.DecideOnSignIn(null, fresh));
        // Reinstall / new phone: load the account, no question.
        Assert.Equal(CloudSaveSync.SignInDecision.PullCloud, CloudSaveSync.DecideOnSignIn(Cloud(played), fresh));
        // Same progress both sides: nothing to ask.
        Assert.Equal(CloudSaveSync.SignInDecision.AlreadySame, CloudSaveSync.DecideOnSignIn(Cloud(played), played));
        // An account that never played, a phone that did: keep the play.
        Assert.Equal(CloudSaveSync.SignInDecision.PushLocal, CloudSaveSync.DecideOnSignIn(Cloud(fresh), played));
        // Both have real play: ask.
        Assert.Equal(CloudSaveSync.SignInDecision.Ask, CloudSaveSync.DecideOnSignIn(Cloud(played), playedMore));
    }
}
