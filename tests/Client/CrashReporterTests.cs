using System;
using System.Collections.Generic;
using System.IO;
using Runewake.Engine.Diagnostics;
using Xunit;

namespace Runewake.Tests.Client;

public class CrashReporterTests
{
    [Fact]
    public void BuildReport_ContainsRequiredFields()
    {
        var ex = new InvalidOperationException("test error");
        var report = CrashReportBuilder.BuildReport(ex, "1.0.0", "Android", "4.3");

        Assert.True(report.ContainsKey("timestamp"));
        Assert.True(report.ContainsKey("app_version"));
        Assert.True(report.ContainsKey("platform"));
        Assert.True(report.ContainsKey("exception_type"));
        Assert.True(report.ContainsKey("message"));
        Assert.True(report.ContainsKey("stack_trace"));
        Assert.True(report.ContainsKey("godot_version"));

        Assert.Equal("1.0.0", report["app_version"]);
        Assert.Equal("Android", report["platform"]);
        Assert.Equal("System.InvalidOperationException", report["exception_type"]);
        Assert.Equal("test error", report["message"]);
        Assert.Contains("test error", (string)report["stack_trace"]!);

        // Assert no null or empty string values
        foreach (var kvp in report)
        {
            Assert.False(string.IsNullOrEmpty(kvp.Value?.ToString()),
                $"Field '{kvp.Key}' was null or empty");
        }
    }

    [Fact]
    public void StackTrace_IsTruncatedAt4000Chars()
    {
        // Build a 10000-char stack trace
        string longTrace = new string('A', 10000);
        string truncated = CrashReportBuilder.TruncateStackTrace(longTrace, 4000);

        // 4000 chars + "\n... (truncated)" = 4016 chars
        Assert.Equal(4016, truncated.Length);
        Assert.StartsWith(new string('A', 4000), truncated);
        Assert.EndsWith("... (truncated)", truncated);
    }

    [Fact]
    public void WriteReport_SilentlySwallowsOnIOException()
    {
        // Pass an invalid path (root on non-Windows), should not throw
        CrashReportBuilder.WriteReportFile("/invalid/path/that/does/not/exist/crash.json", "{}");
        // If we reach here, the exception was silently swallowed
    }

    [Fact]
    public void PendingReports_EmptyDirectoryReturnsEmptyList()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rw_crash_empty_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var files = CrashReportBuilder.ListPendingReports(dir);
            Assert.Empty(files);
        }
        finally
        {
            try { Directory.Delete(dir); } catch { }
        }
    }

    [Fact]
    public void PendingReports_FilesInDirReturnedAsList()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rw_crash_files_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "report_a.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "report_b.json"), "{}");

            var files = CrashReportBuilder.ListPendingReports(dir);
            Assert.Equal(2, files.Count);
            Assert.Contains(files, f => f.EndsWith("report_a.json"));
            Assert.Contains(files, f => f.EndsWith("report_b.json"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SerializedReport_IsValidJson_ContainsAllKeys()
    {
        var ex = new ArgumentNullException("param");
        var report = CrashReportBuilder.BuildReport(ex, "0.1.0", "Windows", "4.3");
        string json = CrashReportBuilder.SerializeReport(report);

        Assert.Contains("\"timestamp\"", json);
        Assert.Contains("\"app_version\": \"0.1.0\"", json);
        Assert.Contains("\"platform\": \"Windows\"", json);
        Assert.Contains("\"exception_type\": \"System.ArgumentNullException\"", json);
        Assert.Contains("\"stack_trace\"", json);

        // Verify it's valid JSON by deserializing back
        var back = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        Assert.NotNull(back);
        Assert.Equal("0.1.0", back!["app_version"]?.ToString());
    }
}