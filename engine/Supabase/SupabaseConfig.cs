using System.Text.Json.Serialization;

namespace Runewake.Engine.Supabase;

/// <summary>
/// Configuration for the Supabase REST API connection.
/// Loaded from user://supabase_config.json at startup.
/// Fallback values may be baked in at compile time via build-time constants.
/// This file must never be committed with real keys.
/// </summary>
public class SupabaseConfig
{
    /// <summary>Supabase project URL (e.g. "https://xxxxx.supabase.co").</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Supabase anon/public API key (safe for client-side use).</summary>
    [JsonPropertyName("anon_key")]
    public string AnonKey { get; set; } = string.Empty;

    /// <summary>
    /// True when both URL and anon key are non-empty, meaning sync is possible.
    /// When false, all sync operations return immediately (offline-first no-op).
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(AnonKey);
}