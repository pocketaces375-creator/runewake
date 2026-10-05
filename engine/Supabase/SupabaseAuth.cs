using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Runewake.Engine.Supabase;

/// <summary>
/// Supabase Auth (GoTrue) over plain HttpClient — no SDK, same as
/// RelicLedgerSync. FABLE-018.
///
/// Four flows, and they are the whole account system:
///
///   SignInAnonymously   first launch. POST /auth/v1/signup with an empty
///                       body. Requires "Anonymous sign-ins" enabled in the
///                       dashboard. Returns a full session.
///
///   Refresh             every launch after that, and whenever the access
///                       token is within a minute of expiring.
///
///   LinkEmail /         upgrade a guest to a recoverable account WITHOUT
///   ConfirmLinkEmail    changing the user id. PUT /auth/v1/user {email}
///                       sends a code; POST /auth/v1/verify {type:
///                       "email_change"} confirms it.
///
///   SendSignInCode /    sign in on ANOTHER phone. POST /auth/v1/otp sends a
///   ConfirmSignInCode   code to a known email; verify {type:"email"} returns
///                       that user's session. The old guest session on the
///                       new phone is then discarded by the caller.
///
/// Every method returns a result rather than throwing: the game must run
/// identically with no network, and an auth failure is a label on the
/// account panel, never a crash.
/// </summary>
public class SupabaseAuth
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SupabaseAuth(SupabaseConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public bool IsConfigured => _config.IsConfigured;

    // ── result type ───────────────────────────────────────────────────────

    public sealed class AuthResult
    {
        public bool Ok { get; init; }
        public SupabaseSession? Session { get; init; }
        /// <summary>Short, player-readable. "No connection", "Wrong code", …</summary>
        public string Error { get; init; } = string.Empty;
        /// <summary>HTTP status, or 0 when the request never completed.</summary>
        public int Status { get; init; }
        /// <summary>
        /// FABLE-ACCOUNTS-1: sign-up accepted, but the project wants the email
        /// confirmed first (dashboard: Confirm email ON). No session yet — the
        /// player taps the link in the email, then signs in with the password.
        /// </summary>
        public bool NeedsConfirmation { get; init; }

        public static AuthResult Success(SupabaseSession s) => new() { Ok = true, Session = s, Status = 200 };
        public static AuthResult Fail(string error, int status = 0) => new() { Ok = false, Error = error, Status = status };
    }

    // ── wire shapes ───────────────────────────────────────────────────────

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public long ExpiresIn { get; set; }
        [JsonPropertyName("expires_at")] public long? ExpiresAt { get; set; }
        [JsonPropertyName("user")] public UserInfo? User { get; set; }
    }

    private sealed class UserInfo
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("new_email")] public string? NewEmail { get; set; }
        [JsonPropertyName("is_anonymous")] public bool IsAnonymous { get; set; }
        [JsonPropertyName("identities")] public List<JsonElement>? Identities { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
        [JsonPropertyName("msg")] public string? Msg { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("error_code")] public string? ErrorCode { get; set; }
    }

    // ── flows ─────────────────────────────────────────────────────────────

    /// <summary>First launch: become a real user with no ceremony.</summary>
    public Task<AuthResult> SignInAnonymously()
        => PostForSession("/auth/v1/signup", "{}", bearer: null);

    /// <summary>Trade a refresh token for a fresh access token. Keeps the user id.</summary>
    public Task<AuthResult> Refresh(SupabaseSession session)
    {
        if (string.IsNullOrEmpty(session.RefreshToken))
            return Task.FromResult(AuthResult.Fail("No refresh token"));
        var body = JsonSerializer.Serialize(new { refresh_token = session.RefreshToken }, JsonOpts);
        return PostForSession("/auth/v1/token?grant_type=refresh_token", body, bearer: null, carryOver: session);
    }

    /// <summary>
    /// Ask Supabase to email a 6-digit code to <paramref name="email"/> for
    /// THIS user. The user id does not change; on confirmation the account
    /// stops being anonymous. Nothing is saved until ConfirmLinkEmail.
    /// </summary>
    public async Task<AuthResult> LinkEmail(SupabaseSession session, string email)
    {
        email = (email ?? string.Empty).Trim();
        if (!LooksLikeEmail(email)) return AuthResult.Fail("That doesn't look like an email address");
        if (!session.IsValid) return AuthResult.Fail("Not signed in");

        var body = JsonSerializer.Serialize(new { email }, JsonOpts);
        var (status, json) = await Send(HttpMethod.Put, "/auth/v1/user", body, session.AccessToken).ConfigureAwait(false);
        if (status == 0) return AuthResult.Fail(NoConnection(json));
        if (status < 200 || status >= 300) return AuthResult.Fail(Describe(json, status), status);
        // Supabase answers with the user object, new_email pending. No session change yet.
        return AuthResult.Success(session);
    }

    /// <summary>
    /// Confirm the code from LinkEmail. On success the returned session is the
    /// same user, now with an email and is_anonymous=false.
    /// </summary>
    public Task<AuthResult> ConfirmLinkEmail(SupabaseSession session, string email, string code)
    {
        code = (code ?? string.Empty).Trim();
        if (code.Length < 6) return Task.FromResult(AuthResult.Fail("Enter the 6-digit code"));
        var body = JsonSerializer.Serialize(new { type = "email_change", email = email.Trim(), token = code }, JsonOpts);
        return PostForSession("/auth/v1/verify", body, bearer: session.AccessToken, carryOver: session);
    }

    /// <summary>
    /// Sign in on another phone: send a code to an email that already belongs
    /// to an account. create_user=false so a typo cannot silently mint a new,
    /// empty account and make the player think their progress is gone.
    /// </summary>
    public async Task<AuthResult> SendSignInCode(string email)
    {
        email = (email ?? string.Empty).Trim();
        if (!LooksLikeEmail(email)) return AuthResult.Fail("That doesn't look like an email address");
        var body = JsonSerializer.Serialize(new { email, create_user = false }, JsonOpts);
        var (status, json) = await Send(HttpMethod.Post, "/auth/v1/otp", body, null).ConfigureAwait(false);
        if (status == 0) return AuthResult.Fail(NoConnection(json));
        if (status < 200 || status >= 300) return AuthResult.Fail(Describe(json, status), status);
        return new AuthResult { Ok = true, Status = status };
    }

    /// <summary>Confirm a sign-in code. The result is THAT account's session.</summary>
    public Task<AuthResult> ConfirmSignInCode(string email, string code)
    {
        code = (code ?? string.Empty).Trim();
        if (code.Length < 6) return Task.FromResult(AuthResult.Fail("Enter the 6-digit code"));
        var body = JsonSerializer.Serialize(new { type = "email", email = email.Trim(), token = code }, JsonOpts);
        return PostForSession("/auth/v1/verify", body, bearer: null);
    }

    // ── FABLE-ACCOUNTS-1: email + password ───────────────────────────────

    /// <summary>Supabase's default minimum. The server has the final say.</summary>
    public const int MinPasswordLength = 6;

    /// <summary>
    /// Create an account with an email and a password. POST /auth/v1/signup.
    ///
    /// Two good outcomes, depending on the dashboard's "Confirm email":
    ///   OFF → a session straight away (Ok, Session set).
    ///   ON  → no session; Supabase emails a confirmation link
    ///         (Ok, NeedsConfirmation). The player taps it, then signs in.
    ///
    /// With Confirm email ON, Supabase does not say "already registered" (it
    /// would let anyone test which emails have accounts); it returns a user
    /// with an EMPTY identities list instead. That case is reported here, so
    /// nobody waits for an email that is never coming.
    /// </summary>
    public async Task<AuthResult> SignUpWithPassword(string email, string password)
    {
        email = (email ?? string.Empty).Trim();
        password ??= string.Empty;
        if (!LooksLikeEmail(email)) return AuthResult.Fail("That doesn't look like an email address");
        if (password.Length < MinPasswordLength) return AuthResult.Fail($"Password must be at least {MinPasswordLength} characters");
        if (!IsConfigured) return AuthResult.Fail("Accounts not configured");

        var body = JsonSerializer.Serialize(new { email, password }, JsonOpts);
        var (status, json) = await Send(HttpMethod.Post, "/auth/v1/signup", body, null).ConfigureAwait(false);
        if (status == 0) return AuthResult.Fail(NoConnection(json));
        if (status < 200 || status >= 300) return AuthResult.Fail(Describe(json, status), status);

        var withSession = ParseSession(json, null);
        if (withSession != null) return AuthResult.Success(withSession);

        UserInfo? user = null;
        try { user = JsonSerializer.Deserialize<UserInfo>(json, JsonOpts); } catch { /* fall through */ }
        if (user == null || string.IsNullOrEmpty(user.Id))
            return AuthResult.Fail("Unreadable reply from server", status);
        if (user.Identities != null && user.Identities.Count == 0)
            return AuthResult.Fail("That email already has an account — sign in instead", status);
        return new AuthResult { Ok = true, NeedsConfirmation = true, Status = status };
    }

    /// <summary>Sign in with email + password. POST /auth/v1/token?grant_type=password.</summary>
    public Task<AuthResult> SignInWithPassword(string email, string password)
    {
        email = (email ?? string.Empty).Trim();
        if (!LooksLikeEmail(email)) return Task.FromResult(AuthResult.Fail("That doesn't look like an email address"));
        if (string.IsNullOrEmpty(password)) return Task.FromResult(AuthResult.Fail("Enter your password"));
        var body = JsonSerializer.Serialize(new { email, password }, JsonOpts);
        return PostForSession("/auth/v1/token?grant_type=password", body, bearer: null);
    }

    /// <summary>Send the sign-up confirmation email again. POST /auth/v1/resend.</summary>
    public async Task<AuthResult> ResendConfirmation(string email)
    {
        email = (email ?? string.Empty).Trim();
        if (!LooksLikeEmail(email)) return AuthResult.Fail("That doesn't look like an email address");
        if (!IsConfigured) return AuthResult.Fail("Accounts not configured");
        var body = JsonSerializer.Serialize(new { type = "signup", email }, JsonOpts);
        var (status, json) = await Send(HttpMethod.Post, "/auth/v1/resend", body, null).ConfigureAwait(false);
        if (status == 0) return AuthResult.Fail(NoConnection(json));
        if (status < 200 || status >= 300) return AuthResult.Fail(Describe(json, status), status);
        return new AuthResult { Ok = true, Status = status };
    }

    /// <summary>
    /// Set a new password for the signed-in user. PUT /auth/v1/user. Used after
    /// "Forgot password?": the player signs in with an emailed code, then
    /// chooses a new password here.
    /// </summary>
    public async Task<AuthResult> SetPassword(SupabaseSession session, string password)
    {
        password ??= string.Empty;
        if (password.Length < MinPasswordLength) return AuthResult.Fail($"Password must be at least {MinPasswordLength} characters");
        if (!session.IsValid) return AuthResult.Fail("Not signed in");
        if (!IsConfigured) return AuthResult.Fail("Accounts not configured");
        var body = JsonSerializer.Serialize(new { password }, JsonOpts);
        var (status, json) = await Send(HttpMethod.Put, "/auth/v1/user", body, session.AccessToken).ConfigureAwait(false);
        if (status == 0) return AuthResult.Fail(NoConnection(json));
        if (status < 200 || status >= 300) return AuthResult.Fail(Describe(json, status), status);
        return AuthResult.Success(session);
    }

    /// <summary>Best-effort server-side logout. Local session is the caller's to delete.</summary>
    public async Task SignOut(SupabaseSession session)
    {
        if (!session.IsValid) return;
        await Send(HttpMethod.Post, "/auth/v1/logout", "{}", session.AccessToken).ConfigureAwait(false);
    }

    // ── plumbing ──────────────────────────────────────────────────────────

    private async Task<AuthResult> PostForSession(string path, string body, string? bearer, SupabaseSession? carryOver = null)
    {
        if (!IsConfigured) return AuthResult.Fail("Accounts not configured");

        var (status, json) = await Send(HttpMethod.Post, path, body, bearer).ConfigureAwait(false);
        if (status == 0) return AuthResult.Fail(NoConnection(json));
        if (status < 200 || status >= 300) return AuthResult.Fail(Describe(json, status), status);

        try { JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts); }
        catch { return AuthResult.Fail("Unreadable reply from server", status); }
        var session = ParseSession(json, carryOver);
        if (session == null)
            return AuthResult.Fail("Server reply had no session", status);
        return AuthResult.Success(session);
    }

    /// <summary>A session from a GoTrue token reply, or null when the reply carries none.</summary>
    private static SupabaseSession? ParseSession(string json, SupabaseSession? carryOver)
    {
        TokenResponse? tok;
        try { tok = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts); }
        catch { return null; }
        if (tok == null || string.IsNullOrEmpty(tok.AccessToken) || tok.User == null || string.IsNullOrEmpty(tok.User.Id))
            return null;
        return new SupabaseSession
        {
            AccessToken = tok.AccessToken!,
            RefreshToken = string.IsNullOrEmpty(tok.RefreshToken) ? (carryOver?.RefreshToken ?? string.Empty) : tok.RefreshToken!,
            UserId = tok.User.Id!,
            Email = string.IsNullOrEmpty(tok.User.Email) ? carryOver?.Email : tok.User.Email,
            IsAnonymous = tok.User.IsAnonymous,
            ExpiresAtUnix = tok.ExpiresAt ?? (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, tok.ExpiresIn)),
        };
    }

    /// <summary>Returns (status, body). status 0 means the request never got an answer.</summary>
    private async Task<(int, string)> Send(HttpMethod method, string path, string body, string? bearer)
    {
        try
        {
            var req = new HttpRequestMessage(method, _config.Url.TrimEnd('/') + path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", _config.AnonKey);
            // Auth endpoints take the anon key as bearer when there is no user yet.
            req.Headers.Add("Authorization", "Bearer " + (bearer ?? _config.AnonKey));
            req.Headers.Add("Accept", "application/json");

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ((int)resp.StatusCode, text);
        }
        catch (Exception ex)
        {
            // FABLE-019: status 0 = never got an answer. Carry WHY in the body,
            // so the player-facing "No connection" can say what actually
            // failed. The first APK with accounts said only "No connection";
            // the cause (no INTERNET permission in the manifest) was
            // invisible from the phone. The innermost exception is the one
            // that names the real fault (SocketException, AuthenticationException…).
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            return (0, inner.GetType().Name + ": " + inner.Message);
        }
    }

    /// <summary>"No connection" plus the underlying reason, when there is one.</summary>
    internal static string NoConnection(string detail)
        => string.IsNullOrEmpty(detail) ? "No connection" : "No connection — " + detail;

    /// <summary>Turn a GoTrue error body into something a player can read.</summary>
    internal static string Describe(string json, int status)
    {
        string? raw = null;
        try
        {
            var e = JsonSerializer.Deserialize<ErrorResponse>(json, JsonOpts);
            raw = e?.Msg ?? e?.Message ?? e?.ErrorDescription ?? e?.Error;
        }
        catch { /* not json */ }

        var lower = (raw ?? string.Empty).ToLowerInvariant();
        if (lower.Contains("anonymous sign-ins are disabled") || lower.Contains("anonymous_provider_disabled"))
            return "Anonymous sign-ins are disabled in the Supabase dashboard";
        if (lower.Contains("otp") && (lower.Contains("expired") || lower.Contains("invalid")))
            return "Wrong or expired code";
        if (lower.Contains("token has expired") || lower.Contains("invalid token"))
            return "Wrong or expired code";
        if (lower.Contains("invalid login credentials"))
            return "Wrong email or password";
        if (lower.Contains("email not confirmed"))
            return "Confirm your email first — tap the link we sent you";
        if (lower.Contains("email logins are disabled") || lower.Contains("email signups are disabled") || lower.Contains("email_provider_disabled"))
            return "Email sign-in is turned off in the Supabase dashboard";
        if ((lower.Contains("signups not allowed") && lower.Contains("otp")) || lower.Contains("user not found"))
            return "No account with that email";
        if (lower.Contains("signups not allowed") || lower.Contains("signup_disabled"))
            return "New sign-ups are turned off in the Supabase dashboard";
        if (lower.Contains("same_password") || lower.Contains("should be different from the old"))
            return "That's already your password";
        if (lower.Contains("email rate limit") || lower.Contains("over_email_send_rate_limit"))
            return "The game can only send a few emails an hour, and that limit was just reached. Try again in an hour — or sign in if you already confirmed";
        if (lower.Contains("rate limit") || status == 429)
            return "Too many attempts — wait a minute";
        if (lower.Contains("already registered") || lower.Contains("already been registered"))
            return "That email already has an account — sign in instead";
        if (!string.IsNullOrEmpty(raw)) return raw!;
        return status switch
        {
            401 => "Session expired — restart the game",
            403 => "Not allowed",
            >= 500 => "Server error — try again later",
            _ => $"Request failed ({status})",
        };
    }

    private static bool LooksLikeEmail(string s)
    {
        int at = s.IndexOf('@');
        return at > 0 && at < s.Length - 3 && s.IndexOf('.', at) > at + 1 && !s.Contains(' ');
    }
}
