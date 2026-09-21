using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Runewake.Engine.Supabase;

/// <summary>
/// The player_saves table over PostgREST. FABLE-018. One row per user, the
/// whole <see cref="CloudSaveBundle"/> as jsonb, last-write-wins on the
/// SERVER's updated_at (a trigger stamps it; whatever the client sends is
/// ignored).
///
/// The merge decision lives in <see cref="Decide"/>, which is pure and
/// tested; Fetch and Push are plumbing.
/// </summary>
public class CloudSaveSync
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public CloudSaveSync(SupabaseConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public bool IsConfigured => _config.IsConfigured;

    /// <summary>What the server holds for this user.</summary>
    public sealed class CloudSave
    {
        public CloudSaveBundle Bundle { get; init; } = new();
        public DateTimeOffset UpdatedAt { get; init; }
        public string? DeviceId { get; init; }
        public string? AppVersion { get; init; }
        public int SaveVersion { get; init; }
    }

    public sealed class FetchResult
    {
        /// <summary>The request completed and was understood (even if there is no row).</summary>
        public bool Ok { get; init; }
        /// <summary>Null when Ok and the user simply has no cloud save yet.</summary>
        public CloudSave? Save { get; init; }
        public string Error { get; init; } = string.Empty;
        public int Status { get; init; }
    }

    private sealed class Row
    {
        [JsonPropertyName("user_id")] public string? UserId { get; set; }
        [JsonPropertyName("save_version")] public int SaveVersion { get; set; }
        [JsonPropertyName("save_json")] public JsonElement SaveJson { get; set; }
        [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
        [JsonPropertyName("app_version")] public string? AppVersion { get; set; }
        [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    }

    // ── fetch ─────────────────────────────────────────────────────────────

    public async Task<FetchResult> Fetch(SupabaseSession session)
    {
        if (!IsConfigured) return new FetchResult { Error = "Not configured" };
        if (!session.IsValid) return new FetchResult { Error = "Not signed in" };

        try
        {
            var url = $"{_config.Url.TrimEnd('/')}/rest/v1/player_saves?user_id=eq.{session.UserId}&select=user_id,save_version,save_json,device_id,app_version,updated_at";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(req, session);

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            int status = (int)resp.StatusCode;
            if (status < 200 || status >= 300)
                return new FetchResult { Error = SupabaseAuth.Describe(text, status), Status = status };

            var rows = JsonSerializer.Deserialize<List<Row>>(text, JsonOpts) ?? new List<Row>();
            if (rows.Count == 0) return new FetchResult { Ok = true, Save = null, Status = status };

            var row = rows[0];
            var bundle = CloudSaveBundle.FromJson(row.SaveJson.GetRawText());
            if (bundle == null)
                return new FetchResult { Error = "Cloud save is unreadable (newer game version?)", Status = status };

            return new FetchResult
            {
                Ok = true,
                Status = status,
                Save = new CloudSave
                {
                    Bundle = bundle,
                    UpdatedAt = row.UpdatedAt,
                    DeviceId = row.DeviceId,
                    AppVersion = row.AppVersion,
                    SaveVersion = row.SaveVersion,
                },
            };
        }
        catch (Exception ex)
        {
            return new FetchResult { Error = "No connection (" + ex.GetType().Name + ")" };
        }
    }

    // ── push ──────────────────────────────────────────────────────────────

    public sealed class PushResult
    {
        public bool Ok { get; init; }
        /// <summary>The server's stamp for this write; store it as the local "last synced" mark.</summary>
        public DateTimeOffset? UpdatedAt { get; init; }
        public string Error { get; init; } = string.Empty;
        public int Status { get; init; }
    }

    public async Task<PushResult> Push(SupabaseSession session, CloudSaveBundle bundle, string deviceId, string appVersion)
    {
        if (!IsConfigured) return new PushResult { Error = "Not configured" };
        if (!session.IsValid) return new PushResult { Error = "Not signed in" };

        try
        {
            var payload = new
            {
                user_id = session.UserId,
                save_version = CloudSaveBundle.CurrentVersion,
                save_json = JsonSerializer.Deserialize<JsonElement>(bundle.ToJson()),
                device_id = deviceId,
                app_version = appVersion,
            };
            var body = JsonSerializer.Serialize(payload, JsonOpts);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.Url.TrimEnd('/')}/rest/v1/player_saves")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            ApplyHeaders(req, session);
            // Upsert on the primary key, and hand back the row so we learn the
            // server's updated_at without a second round trip.
            req.Headers.Add("Prefer", "resolution=merge-duplicates,return=representation");

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            int status = (int)resp.StatusCode;
            if (status < 200 || status >= 300)
                return new PushResult { Error = SupabaseAuth.Describe(text, status), Status = status };

            DateTimeOffset? stamp = null;
            try
            {
                var rows = JsonSerializer.Deserialize<List<Row>>(text, JsonOpts);
                if (rows != null && rows.Count > 0) stamp = rows[0].UpdatedAt;
            }
            catch { /* representation missing is not a failure */ }

            return new PushResult { Ok = true, UpdatedAt = stamp, Status = status };
        }
        catch (Exception ex)
        {
            return new PushResult { Error = "No connection (" + ex.GetType().Name + ")" };
        }
    }

    private void ApplyHeaders(HttpRequestMessage req, SupabaseSession session)
    {
        req.Headers.Add("apikey", _config.AnonKey);
        req.Headers.Add("Authorization", "Bearer " + session.AccessToken);
        req.Headers.Add("Accept", "application/json");
    }

    // ── the merge rule ────────────────────────────────────────────────────

    public enum Decision
    {
        /// <summary>Nothing in the cloud; upload what we have.</summary>
        PushLocal,
        /// <summary>Cloud is newer than our last sync; take it.</summary>
        PullCloud,
        /// <summary>Cloud is what we last synced (or older); our local changes win.</summary>
        PushLocalNewer,
        /// <summary>Both moved since we last synced. Do not guess — ask the player.</summary>
        Conflict,
        /// <summary>Identical; nothing to do.</summary>
        NoOp,
    }

    /// <summary>
    /// Pure. Given what the server holds, when we last synced with it, and
    /// whether anything changed locally since then, decide what to do.
    ///
    ///   lastSyncedAt   the server updated_at from our last successful pull or
    ///                  push, or null if this install has never synced.
    ///   localDirty     has the game saved since lastSyncedAt?
    ///
    /// Last-write-wins is only safe when one side moved. When both moved, the
    /// honest answer is Conflict and the client shows two cards: "this phone,
    /// Level 7, 3 relics" vs "cloud, Level 5, 4 relics — from another phone,
    /// yesterday". A phone game that silently discards a day of play is worse
    /// than one that asks.
    /// </summary>
    public static Decision Decide(CloudSave? cloud, DateTimeOffset? lastSyncedAt, bool localDirty, bool localIsEmpty)
    {
        if (cloud == null)
            return localIsEmpty ? Decision.NoOp : Decision.PushLocal;

        // Never synced on this install: it's either a reinstall (adopt the
        // cloud) or a brand-new phone with play already on it (conflict).
        if (lastSyncedAt == null)
            return localIsEmpty || !localDirty ? Decision.PullCloud : Decision.Conflict;

        bool cloudMoved = cloud.UpdatedAt > lastSyncedAt.Value.AddSeconds(1);   // 1s: clock/precision slack

        if (cloudMoved && localDirty) return Decision.Conflict;
        if (cloudMoved) return Decision.PullCloud;
        if (localDirty) return Decision.PushLocalNewer;
        return Decision.NoOp;
    }
}
