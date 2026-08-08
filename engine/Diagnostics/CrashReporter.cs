using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Runewake.Engine.Diagnostics;

/// <summary>
/// Static crash reporter — writes crash reports to disk as JSON files.
/// Thread-safe. Never throws. When not configured (no output dir), writes
/// are silently dropped.
///
/// Max 50 crash files retained. Oldest are evicted when the limit is reached.
/// </summary>
public static class CrashReporter
{
    private static string? _outputDir;
    private static string? _accountId;
    private static readonly object _lock = new();
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private const int MaxFiles = 50;

    /// <summary>
    /// Set the output directory for crash report files.
    /// Must be an absolute path (resolve from Godot's user:// before calling).
    /// </summary>
    public static void SetOutputDir(string dir)
    {
        lock (_lock) { _outputDir = dir; }
    }

    /// <summary>
    /// Set the account ID to stamp on all subsequent crash reports.
    /// </summary>
    public static void SetAccountId(string? id)
    {
        lock (_lock) { _accountId = id; }
    }

    /// <summary>
    /// Record an exception as a crash report on disk.
    /// Thread-safe. Never throws — if anything goes wrong, the error is caught
    /// and silently discarded. The game must never fail to launch because of
    /// crash reporting.
    /// </summary>
    public static void RecordException(
        Exception ex,
        string phase,
        Dictionary<string, string>? context = null)
    {
        try
        {
            string? dir;
            string? accountId;
            lock (_lock)
            {
                dir = _outputDir;
                accountId = _accountId;
            }

            if (string.IsNullOrEmpty(dir))
                return; // not configured — silently drop

            // Ensure directory exists
            Directory.CreateDirectory(dir);

            // Enforce max file count
            EnforceMaxFiles(dir);

            var report = new CrashReport
            {
                AccountId = accountId,
                OccurredAt = DateTime.UtcNow,
                AppVersion = "1.0.0",
                Platform = GetPlatformName(),
                ExceptionType = ex.GetType().FullName ?? "Unknown",
                Message = ex.Message,
                StackTrace = ex.ToString(),
                GamePhase = phase,
                Context = context ?? new Dictionary<string, string>()
            };

            string path = Path.Combine(dir, $"{report.ReportId}.json");
            string json = JsonSerializer.Serialize(report, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Never throw — crash reporting must never prevent the game from launching.
        }
    }

    /// <summary>
    /// Load all unsent crash reports (.json files) from the output directory.
    /// Returns empty list if directory is empty or unreadable.
    /// </summary>
    public static List<CrashReport> LoadUnsentReports()
    {
        try
        {
            string? dir;
            lock (_lock) { dir = _outputDir; }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return new List<CrashReport>();

            var reports = new List<CrashReport>();
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var report = JsonSerializer.Deserialize<CrashReport>(json, JsonOpts);
                    if (report != null)
                        reports.Add(report);
                }
                catch
                {
                    // Skip unreadable files
                }
            }
            return reports;
        }
        catch
        {
            return new List<CrashReport>();
        }
    }

    /// <summary>
    /// Mark a report as sent by renaming the .json file to .sent.
    /// Does nothing if the file no longer exists.
    /// </summary>
    public static void MarkSent(string reportId)
    {
        try
        {
            string? dir;
            lock (_lock) { dir = _outputDir; }
            if (string.IsNullOrEmpty(dir)) return;

            string jsonPath = Path.Combine(dir, $"{reportId}.json");
            string sentPath = Path.Combine(dir, $"{reportId}.sent");
            if (File.Exists(jsonPath))
                File.Move(jsonPath, sentPath, overwrite: true);
        }
        catch
        {
            // Best-effort
        }
    }

    // ——— Private helpers ———

    private static void EnforceMaxFiles(string dir)
    {
        try
        {
            var files = Directory.GetFiles(dir, "*.json")
                .OrderBy(f => File.GetCreationTimeUtc(f))
                .ToList();

            while (files.Count >= MaxFiles)
            {
                try { File.Delete(files[0]); }
                catch { /* best-effort */ }
                files.RemoveAt(0);
            }
        }
        catch
        {
            // Best-effort — if we can't enumerate, skip cleanup
        }
    }

    private static string GetPlatformName()
    {
        if (System.OperatingSystem.IsAndroid()) return "Android";
        if (System.OperatingSystem.IsIOS()) return "iOS";
        if (System.OperatingSystem.IsWindows()) return "Windows";
        if (System.OperatingSystem.IsMacOS()) return "macOS";
        if (System.OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }
}