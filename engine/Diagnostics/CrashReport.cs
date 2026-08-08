using System;
using System.Collections.Generic;

namespace Runewake.Engine.Diagnostics;

/// <summary>
/// A single crash report record.
/// Serialized to JSON and written to disk by <see cref="CrashReporter"/>.
/// </summary>
public class CrashReport
{
    /// <summary>Unique identifier for this report (UUID).</summary>
    public string ReportId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>UTC timestamp when the exception occurred.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional account UUID (set by CrashReportingService).</summary>
    public string? AccountId { get; set; }

    /// <summary>Game version string.</summary>
    public string AppVersion { get; set; } = "1.0.0";

    /// <summary>Platform string (e.g. "Android", "iOS", "editor").</summary>
    public string Platform { get; set; } = "";

    /// <summary>Full type name of the exception (e.g. "System.NullReferenceException").</summary>
    public string ExceptionType { get; set; } = "";

    /// <summary>Exception message.</summary>
    public string Message { get; set; } = "";

    /// <summary>Full stack trace string.</summary>
    public string StackTrace { get; set; } = "";

    /// <summary>Game phase when the crash occurred (e.g. "map", "duel", "dig", "tutorial").</summary>
    public string GamePhase { get; set; } = "";

    /// <summary>Additional context key/value pairs (e.g. encounter ID, node ID).</summary>
    public Dictionary<string, string> Context { get; set; } = new();
}