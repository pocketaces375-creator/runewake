using System;
using System.IO;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Lightweight unhandled-exception reporter for exported builds.
///
/// Hooks <see cref="AppDomain.CurrentDomain.UnhandledException"/> before any
/// scene code runs and writes a per-crash file to user://crashes/ containing:
///   - UTC ISO-8601 timestamp
///   - Exception type and message
///   - Full stack trace
///   - OS name (via <see cref="OS.GetName"/>)
///   - App version (via ProjectSettings "application/config/version")
///
/// Each crash gets its own file so repeated crashes don't overwrite prior logs.
/// </summary>
public static class CrashReporter
{
    private static readonly string CrashDir = "user://crashes";
    private static bool _installed;

    /// <summary>
    /// Install the global exception handler. Safe to call multiple times —
    /// only the first call installs the handler.
    /// </summary>
    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        GD.Print("[CrashReporter] Installed.");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var ex = args.ExceptionObject as Exception;
        if (ex == null)
            return;

        string? osName = null;
        string? appVersion = null;

        try { osName = OS.GetName(); }
        catch { /* best-effort */ }

        try { appVersion = ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString(); }
        catch { /* best-effort */ }

        string timestamp = DateTime.UtcNow.ToString("O");
        string fileTimestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string fileName = $"crash_{fileTimestamp}.log";

        var lines = new[]
        {
            $"timestamp: {timestamp}",
            $"os: {osName ?? "unknown"}",
            $"app_version: {appVersion ?? "0.0.0"}",
            $"terminating: {args.IsTerminating}",
            $"exception_type: {ex.GetType().FullName}",
            $"message: {ex.Message}",
            string.Empty,
            "stack_trace:",
            ex.ToString(),
            string.Empty,
            "--- end ---",
        };

        string text = string.Join(System.Environment.NewLine, lines);

        // Always print to the editor/output log
        GD.PrintErr($"[CrashReporter] FATAL: {ex.GetType().Name}: {ex.Message}");

        try
        {
            string dir = ProjectSettings.GlobalizePath(CrashDir);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, text);
            GD.PrintErr($"[CrashReporter] Crash log written to {path}");
        }
        catch (Exception inner)
        {
            GD.PrintErr($"[CrashReporter] Failed to write crash log: {inner.Message}");
        }
    }
}