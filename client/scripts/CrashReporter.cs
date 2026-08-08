using System;
using System.Collections.Generic;
using Godot;
using Runewake.Engine.Diagnostics;

namespace Runewake.Client;

/// <summary>
/// Godot Node singleton (autoload) that handles unhandled exceptions.
///
/// On <see cref="AppDomain.CurrentDomain.UnhandledException"/>: builds a
/// JSON crash report via <see cref="CrashReportBuilder"/> and writes it to
/// <c>user://crash_reports/&lt;timestamp&gt;_crash.json</c>.
///
/// On startup (call <see cref="UploadPendingReports"/>), POSTs any unsent
/// reports to Supabase using Godot's <see cref="HttpRequest"/> node (works
/// in exported builds on Android/iOS). Each file gets its own one-shot
/// HttpRequest child; on 2xx the local file is deleted.
///
/// The crash handler itself never throws.
/// </summary>
public partial class CrashReporter : Node
{
    private static CrashReporter? _instance;
    private static bool _hookInstalled;

    private const string CrashDir = "user://crash_reports";

    // ——— Singleton lifecycle ———

    public override void _Ready()
    {
        _instance = this;
        InstallGlobalHook();
        GD.Print("[CrashReporter] Autoload ready.");
    }

    /// <summary>
    /// Install the global AppDomain unhandled-exception hook.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    private static void InstallGlobalHook()
    {
        if (_hookInstalled)
            return;
        _hookInstalled = true;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        GD.Print("[CrashReporter] Global hook installed.");
    }

    // ——— Crash handler ———

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var ex = args.ExceptionObject as Exception;
        if (ex == null)
            return;

        string appVersion = "dev";
        string platform = "unknown";
        string godotVersion = "unknown";

        try { appVersion = ProjectSettings.GetSetting("application/config/version", "dev").AsString(); }
        catch { }

        try { platform = OS.GetName(); }
        catch { }

        try
        {
            var vi = Godot.Engine.GetVersionInfo();
            if (vi.TryGetValue("string", out var v))
                godotVersion = v.AsString();
        }
        catch { }

        var report = CrashReportBuilder.BuildReport(ex, appVersion, platform, godotVersion);
        string json = CrashReportBuilder.SerializeReport(report);
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string fileName = $"{timestamp}_crash.json";

        string? absPath = null;
        try
        {
            string dir = ProjectSettings.GlobalizePath(CrashDir);
            string filePath = System.IO.Path.Combine(dir, fileName);
            CrashReportBuilder.WriteReportFile(filePath, json);
            absPath = filePath;
        }
        catch
        {
            // Best-effort path resolution
        }

        GD.PrintErr($"[CrashReporter] FATAL: {ex.GetType().Name}: {ex.Message}");
        if (absPath != null)
            GD.PrintErr($"[CrashReporter] Report written to {absPath}");
    }

    // ——— Pending report upload ———

    /// <summary>
    /// Upload all pending crash report JSON files to Supabase.
    /// Call once from Main._Ready() after save loading.
    /// Spawns one-shot HttpRequest children — fires and forgets.
    /// On 2xx response, the local file is deleted.
    /// Reports with errors are left on disk for the next session.
    /// </summary>
    public static void UploadPendingReports(string supabaseUrl, string anonKey)
    {
        if (_instance == null)
        {
            GD.PrintErr("[CrashReporter] Cannot upload — autoload not yet ready.");
            return;
        }

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(anonKey))
        {
            GD.Print("[CrashReporter] UploadPendingReports: no credentials configured.");
            return;
        }

        string? dir = null;
        try
        {
            dir = ProjectSettings.GlobalizePath(CrashDir);
        }
        catch
        {
            return;
        }

        var files = CrashReportBuilder.ListPendingReports(dir);
        if (files.Count == 0)
        {
            GD.Print("[CrashReporter] No pending reports to upload.");
            return;
        }

        GD.Print($"[CrashReporter] Uploading {files.Count} pending crash report(s)...");

        foreach (var filePath in files)
        {
            string json;
            try
            {
                json = System.IO.File.ReadAllText(filePath);
                if (string.IsNullOrEmpty(json))
                    continue;
            }
            catch
            {
                continue;
            }

            var http = new HttpRequest();
            http.UseThreads = true;
            http.Timeout = 10;

            string url = $"{supabaseUrl.TrimEnd('/')}/rest/v1/crash_reports";
            string[] headers = new[]
            {
                $"apikey: {anonKey}",
                $"Authorization: Bearer {anonKey}",
                "Content-Type: application/json",
                "Accept: application/json"
            };

            // Capture filePath in closure for the response handler
            string capturedPath = filePath;

            http.RequestCompleted += (long result, long responseCode, string[] responseHeaders, byte[] body) =>
            {
                if (responseCode == 200 || responseCode == 201)
                {
                    CrashReportBuilder.DeleteReportFile(capturedPath);
                    GD.Print($"[CrashReporter] Uploaded and removed: {capturedPath}");
                }
                else
                {
                    GD.PrintErr($"[CrashReporter] Upload failed (HTTP {responseCode}): {capturedPath}");
                }

                // Cleanup the temporary node
                if (_instance != null && http.GetParent() != null)
                    _instance.RemoveChild(http);
                http.QueueFree();
            };

            _instance.AddChild(http);
            Error err = http.Request(url, headers, Godot.HttpClient.Method.Post, json);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[CrashReporter] Request error {err} for {filePath}");
                _instance.RemoveChild(http);
                http.QueueFree();
            }
        }
    }
}