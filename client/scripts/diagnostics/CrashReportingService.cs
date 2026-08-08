using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Diagnostics;
using Runewake.Engine.Supabase;

namespace Runewake.Client;

/// <summary>
/// Crash reporting service — Godot Node that initializes the crash reporter
/// and optionally uploads reports to Supabase.
///
/// Local-first: always writes crash reports to disk. Upload is best-effort
/// when configured. Never throws — crash reporting must never prevent the
/// game from launching.
/// </summary>
public partial class CrashReportingService : Node
{
    private SupabaseConfig? _config;
    private bool _initialized;

    /// <summary>
    /// Initialize the crash reporter. Resolves user://crashes/ to an absolute
    /// path and sets the output directory. Call once from Main.cs.
    /// </summary>
    public void Initialize(SupabaseConfig config, string? accountId)
    {
        try
        {
            string dir = ProjectSettings.GlobalizePath("user://crashes/");
            System.IO.Directory.CreateDirectory(dir);
            CrashReporter.SetOutputDir(dir);
            CrashReporter.SetAccountId(accountId);
            _config = config;
            _initialized = true;
            GD.Print($"[CrashService] Initialized, dir={dir}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CrashService] Init failed: {ex.Message}");
            // Never throw — crash reporting must not prevent launch.
        }
    }

    /// <summary>
    /// Record an exception and attempt to flush unsent reports.
    /// Fire-and-forget — does not block the caller.
    /// </summary>
    public void HandleException(Exception ex, string phase,
        Dictionary<string, string>? context = null)
    {
        CrashReporter.RecordException(ex, phase, context);
        _ = FlushAsync(); // fire and forget
    }

    /// <summary>
    /// Upload all unsent crash reports to Supabase.
    /// No-op when not configured. Silently swallows errors.
    /// </summary>
    public async Task FlushAsync()
    {
        if (!_initialized || _config == null || !_config.IsConfigured)
            return;

        try
        {
            var reports = CrashReporter.LoadUnsentReports();
            if (reports.Count == 0)
                return;

            // Batch in groups of 20
            for (int i = 0; i < reports.Count; i += 20)
            {
                var batch = reports.GetRange(i, Math.Min(20, reports.Count - i));
                var rows = new List<object>(batch.Count);
                foreach (var r in batch)
                {
                    rows.Add(new
                    {
                        report_id = r.ReportId,
                        account_id = r.AccountId,
                        occurred_at = r.OccurredAt.ToString("O"),
                        app_version = r.AppVersion,
                        platform = r.Platform,
                        exception_type = r.ExceptionType,
                        message = r.Message,
                        stack_trace = r.StackTrace,
                        game_phase = r.GamePhase,
                        context = r.Context
                    });
                }

                var body = System.Text.Json.JsonSerializer.Serialize(rows);
                var url = $"{_config.Url}/rest/v1/crash_reports";
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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

                // Mark each report as sent
                foreach (var r in batch)
                    CrashReporter.MarkSent(r.ReportId);

                GD.Print($"[CrashService] Flushed {batch.Count} crash reports.");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CrashService] Flush failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Override to catch Godot-level crash notifications and app exit.
    /// On crash or close, synchronously flush reports (blocking is acceptable
    /// here since the process is about to exit).
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationCrash || what == NotificationWMCloseRequest)
        {
            GD.Print("[CrashService] Process exit — flushing crash reports...");
            try
            {
                FlushAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort on exit
            }
        }
    }
}