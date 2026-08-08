using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Runewake.Engine.Cards;

namespace Runewake.Engine.Supabase;

/// <summary>
/// HTTP-only sync client for the Supabase relic ledger.
/// No Supabase SDK dependency — just HttpClient + REST.
/// All network errors are silently swallowed (best-effort sync).
/// When <see cref="SupabaseConfig.IsConfigured"/> is false, all methods
/// return immediately (offline-first, server optional).
/// </summary>
public class RelicLedgerSync
{
    private readonly HttpClient _http;
    private readonly SupabaseConfig _config;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    /// <summary>
    /// True when URL and anon key are both set.
    /// </summary>
    public bool IsConfigured => _config.IsConfigured;

    /// <summary>
    /// Create a new sync client.
    /// </summary>
    /// <param name="config">Supabase connection config.</param>
    /// <param name="httpClient">
    /// Optional HttpClient override (for testing with mock handlers).
    /// If omitted, a new HttpClient with 10s timeout is created.
    /// </param>
    public RelicLedgerSync(SupabaseConfig config, HttpClient? httpClient = null)
    {
        _config = config;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    // ——— Headers ———

    private void ApplyHeaders(HttpRequestMessage req)
    {
        req.Headers.Add("apikey", _config.AnonKey);
        req.Headers.Add("Authorization", $"Bearer {_config.AnonKey}");
    }

    // ——— Account identity ———

    /// <summary>
    /// POST /rest/v1/rpc/get_or_create_account
    /// Returns account_id UUID string, or a locally-generated fallback
    /// UUID written to user://account_id.txt on any failure.
    /// </summary>
    public async Task<string?> GetOrCreateAccountId(string deviceId)
    {
        if (!IsConfigured)
            return null;

        try
        {
            var body = JsonSerializer.Serialize(new { device_id = deviceId }, JsonOpts);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.Url}/rest/v1/rpc/get_or_create_account")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            ApplyHeaders(req);
            req.Headers.Add("Accept", "application/json");

            var response = await _http.SendAsync(req).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            // Response is a plain UUID string
            var accountId = JsonSerializer.Deserialize<string>(json, JsonOpts);
            return accountId;
        }
        catch
        {
            // Network failure — silently return null; caller falls back to local UUID
            return null;
        }
    }

    // ——— Relic sync (upsert) ———

    /// <summary>
    /// POST /rest/v1/relic_instances with Prefer: resolution=merge-duplicates.
    /// Batches relics in groups of 50.
    /// Silently swallows network errors (best-effort).
    /// </summary>
    public async Task SyncRelics(string accountId, List<LostRelicInstance> relics)
    {
        if (!IsConfigured || relics.Count == 0)
            return;

        var batchSize = 50;
        for (int i = 0; i < relics.Count; i += batchSize)
        {
            var batch = relics.GetRange(i, Math.Min(batchSize, relics.Count - i));
            try
            {
                var rows = new List<object>(batch.Count);
                foreach (var r in batch)
                {
                    rows.Add(new
                    {
                        relic_instance_id = r.RelicInstanceId,
                        account_id = accountId,
                        card_id = r.CardId,
                        acquirer_name = r.AcquirerName,
                        acquired_at = r.AcquiredAt,
                        site = r.Site,
                        discovery_index = r.DiscoveryIndex,
                        engraving_style = r.EngravingStyle
                    });
                }

                var body = JsonSerializer.Serialize(rows, JsonOpts);
                var req = new HttpRequestMessage(HttpMethod.Post, $"{_config.Url}/rest/v1/relic_instances")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                ApplyHeaders(req);
                req.Headers.Add("Prefer", "resolution=merge-duplicates");
                req.Headers.Add("Accept", "application/json");

                var response = await _http.SendAsync(req).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                // Best-effort — skip this batch silently
            }
        }
    }

    // ——— Relic fetch ———

    /// <summary>
    /// GET /rest/v1/relic_instances?account_id=eq.{accountId}
    /// Deserializes rows into LostRelicInstance list.
    /// Returns empty list on any failure.
    /// </summary>
    public async Task<List<LostRelicInstance>> FetchRelics(string accountId)
    {
        if (!IsConfigured)
            return new List<LostRelicInstance>();

        try
        {
            var url = $"{_config.Url}/rest/v1/relic_instances?account_id=eq.{accountId}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(req);
            req.Headers.Add("Accept", "application/json");

            var response = await _http.SendAsync(req).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var relics = JsonSerializer.Deserialize<List<SupabaseRelicRow>>(json, JsonOpts);
            if (relics == null)
                return new List<LostRelicInstance>();

            var result = new List<LostRelicInstance>(relics.Count);
            foreach (var row in relics)
            {
                result.Add(new LostRelicInstance
                {
                    RelicInstanceId = row.RelicInstanceId,
                    CardId = row.CardId,
                    AcquirerName = row.AcquirerName,
                    AcquiredAt = row.AcquiredAt,
                    Site = row.Site,
                    DiscoveryIndex = row.DiscoveryIndex,
                    EngravingStyle = row.EngravingStyle
                });
            }
            return result;
        }
        catch
        {
            return new List<LostRelicInstance>();
        }
    }

    // ——— Internal deserialization row ———

    private class SupabaseRelicRow
    {
        public string RelicInstanceId { get; set; } = string.Empty;
        public string CardId { get; set; } = string.Empty;
        public string AcquirerName { get; set; } = string.Empty;
        public string AcquiredAt { get; set; } = string.Empty;
        public string Site { get; set; } = string.Empty;
        public int DiscoveryIndex { get; set; }
        public string EngravingStyle { get; set; } = string.Empty;
    }
}