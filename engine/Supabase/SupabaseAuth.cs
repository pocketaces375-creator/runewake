using System;
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

        TokenResponse? tok;
        try { tok = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts); }
        catch { return AuthResult.Fail("Unreadable reply from server", status); }

        if (tok == null || string.IsNullOrEmpty(tok.AccessToken) || tok.User == null || string.IsNullOrEmpty(tok.User.Id))
            return AuthResult.Fail("Server reply had no session", status);

        var session = new SupabaseSession
        {
            AccessToken = tok.AccessToken!,
            RefreshToken = string.IsNullOrEmpty(tok.RefreshToken) ? (carryOver?.RefreshToken ?? string.Empty) : tok.RefreshToken!,
            UserId = tok.User.Id!,
            Email = string.IsNullOrEmpty(tok.User.Email) ? carryOver?.Email : tok.User.Email,
            IsAnonymous = tok.User.IsAnonymous,
            ExpiresAtUnix = tok.ExpiresAt ?? (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, tok.ExpiresIn)),
        };
        return AuthResult.Success(session);
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
        if (lower.Contains("signups not allowed") || lower.Contains("user not found"))
            return "No account with that email";
        if (lower.Contains("rate limit") || status == 429)
            return "Too many attempts — wait a minute";
        if (lower.Contains("already registered") || lower.Contains("already been registered"))
            return "That email is already linked to another account";
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
