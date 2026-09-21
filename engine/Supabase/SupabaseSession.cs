using System;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Supabase;

/// <summary>
/// A signed-in Supabase user, as the client remembers it between launches.
/// Persisted verbatim to user://supabase_session.json by the client.
///
/// FABLE-018. A player is always one of these — the first launch signs in
/// ANONYMOUSLY (no email, no password, no prompt) and gets a real
/// auth.users row and a JWT. Linking an email later keeps the same user id,
/// so nothing about the save has to move; it just becomes recoverable.
/// </summary>
public class SupabaseSession
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>auth.users.id — the key of every row that belongs to this player.</summary>
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Null until an email has been linked and confirmed.</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("is_anonymous")]
    public bool IsAnonymous { get; set; } = true;

    /// <summary>Unix seconds. Access tokens live one hour by default.</summary>
    [JsonPropertyName("expires_at")]
    public long ExpiresAtUnix { get; set; }

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(UserId);

    /// <summary>
    /// True when the access token is expired or about to be. Refresh early —
    /// a request that leaves the phone with 20 seconds of validity can arrive
    /// with none.
    /// </summary>
    public bool NeedsRefresh(DateTimeOffset? now = null, int marginSeconds = 60)
    {
        var t = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        return ExpiresAtUnix <= t + marginSeconds;
    }

    /// <summary>"Guest 3F2A" or the linked email — what the account panel shows.</summary>
    public string DisplayLabel()
    {
        if (!string.IsNullOrEmpty(Email)) return Email!;
        if (UserId.Length >= 4) return "Guest " + UserId.Substring(0, 4).ToUpperInvariant();
        return "Guest";
    }
}
