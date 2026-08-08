using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Supabase;
using Runewake.Engine.Telemetry;

namespace Runewake.Client;

/// <summary>
/// Telemetry service — Godot Node that records events to a ring buffer
/// and flushes them to Supabase when configured.
/// Offline-first: no-op when Supabase is not configured.
/// Fire-and-forget: never blocks the main thread.
/// </summary>
public partial class TelemetryService : Node
{
    private readonly TelemetryBuffer _buffer = new(500);
    private SupabaseConfig? _config;
    private string? _accountId;
    private RelicLedgerSync? _sync;

    /// <summary>Number of events currently buffered.</summary>
    public int BufferedCount => _buffer.Count;

    /// <summary>
    /// Initialize with Supabase config and optional account ID.
    /// Call once from Main.cs after SyncManager init.
    /// </summary>
    public void Initialize(SupabaseConfig config, string? accountId)
    {
        _config = config;
        _accountId = accountId;
        if (config.IsConfigured)
            _sync = new RelicLedgerSync(config);
    }

    /// <summary>
    /// Record a telemetry event. Stamped with account ID and UTC time.
    /// Thread-safe — can be called from any context.
    /// </summary>
    public void Record(string eventName, Dictionary<string, string>? props = null)
    {
        var evt = new TelemetryEvent(eventName, props)
        {
            AccountId = _accountId
        };
        _buffer.Record(evt);
    }

    /// <summary>
    /// Flush all buffered events to Supabase in a single POST batch.
    /// No-op when not configured. Network errors silently swallowed.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_config == null || !_config.IsConfigured)
            return;

        var events = _buffer.Drain();
        if (events.Count == 0)
            return;

        try
        {
            var rows = new List<object>(events.Count);
            foreach (var e in events)
            {
                rows.Add(new
                {
                    event_name = e.EventName,
                    account_id = e.AccountId,
                    occurred_at = e.OccurredAt.ToString("O"),
                    props = e.Props
                });
            }

            var body = System.Text.Json.JsonSerializer.Serialize(rows);
            var url = $"{_config!.Url}/rest/v1/telemetry_events";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("apikey", _config.AnonKey);
            req.Headers.Add("Authorization", $"Bearer {_config.AnonKey}");
            req.Headers.Add("Prefer", "resolution=merge-duplicates");
            req.Headers.Add("Accept", "application/json");

            var response = await http.SendAsync(req).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            GD.Print($"[Telemetry] Flushed {events.Count} events.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Telemetry] Flush failed: {ex.Message}");
        }
    }

    // ——— Convenience recorders ———

    public void RecordDuelStart(string encounterId, int playerDeckSize)
    {
        Record("duel_start", new Dictionary<string, string>
        {
            ["encounter_id"] = encounterId,
            ["deck_size"] = playerDeckSize.ToString()
        });
    }

    public void RecordDuelEnd(string encounterId, bool won, int turnsPlayed)
    {
        Record("duel_end", new Dictionary<string, string>
        {
            ["encounter_id"] = encounterId,
            ["won"] = won ? "1" : "0",
            ["turns"] = turnsPlayed.ToString()
        });
    }

    public void RecordNodeCleared(string nodeId)
    {
        Record("node_cleared", new Dictionary<string, string>
        {
            ["node_id"] = nodeId
        });
    }

    public void RecordRelicMinted(string cardId)
    {
        Record("relic_minted", new Dictionary<string, string>
        {
            ["card_id"] = cardId
        });
    }

    public void RecordTutorialStepReached(string stepName)
    {
        Record("tutorial_step", new Dictionary<string, string>
        {
            ["step"] = stepName
        });
    }
}