using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Runewake.Engine.Diagnostics;

/// <summary>
/// Pure C# static helpers for building crash reports.
/// No Godot dependencies — testable from the xUnit project.
/// </summary>
public static class CrashReportBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Build a crash report dictionary from an exception and metadata.
    /// Keys: timestamp, app_version, platform, exception_type, message,
    /// stack_trace, godot_version.
    /// </summary>
    public static Dictionary<string, object?> BuildReport(
        Exception exception,
        string appVersion,
        string platform,
        string godotVersion)
    {
        return new Dictionary<string, object?>
        {
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
            ["app_version"] = appVersion,
            ["platform"] = platform,
            ["exception_type"] = exception.GetType().FullName,
            ["message"] = exception.Message,
            ["stack_trace"] = TruncateStackTrace(exception.ToString(), 4000),
            ["godot_version"] = godotVersion
        };
    }

    /// <summary>
    /// Truncate a stack trace string to at most <paramref name="maxLen"/> characters.
    /// If truncated, appends "\n... (truncated)" to the end.
    /// </summary>
    public static string TruncateStackTrace(string stackTrace, int maxLen = 4000)
    {
        if (string.IsNullOrEmpty(stackTrace) || stackTrace.Length <= maxLen)
            return stackTrace ?? string.Empty;

        return stackTrace[..maxLen] + "\n... (truncated)";
    }

    /// <summary>
    /// Serialize a report dictionary to a JSON string.
    /// </summary>
    public static string SerializeReport(Dictionary<string, object?> report)
    {
        return JsonSerializer.Serialize(report, JsonOpts);
    }

    /// <summary>
    /// List all .json files in a directory. Returns paths sorted by name.
    /// Returns empty list if the directory doesn't exist or is unreadable.
    /// </summary>
    public static List<string> ListPendingReports(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return new List<string>();

            return Directory.GetFiles(directory, "*.json")
                .OrderBy(f => f)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Write a crash report JSON string to a file.
    /// Silently swallows any IO exceptions (never throws).
    /// </summary>
    public static void WriteReportFile(string filePath, string json)
    {
        try
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Never throw from crash handler
        }
    }

    /// <summary>
    /// Delete a file. Silently swallows errors.
    /// </summary>
    public static void DeleteReportFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // Best-effort
        }
    }
}