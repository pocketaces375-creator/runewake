using System;
using System.Collections.Generic;

namespace Runewake.Engine.Telemetry;

/// <summary>
/// A single telemetry event record.
/// No PII. Stamped with AccountId and time by the service.
/// </summary>
public class TelemetryEvent
{
    /// <summary>Event type name (e.g. "duel_start", "duel_end", "relic_minted").</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Optional account UUID (set by TelemetryService).</summary>
    public string? AccountId { get; set; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Arbitrary key/value properties for the event.</summary>
    public Dictionary<string, string> Props { get; set; } = new();

    public TelemetryEvent() { }

    public TelemetryEvent(string eventName, Dictionary<string, string>? props = null)
    {
        EventName = eventName;
        OccurredAt = DateTime.UtcNow;
        if (props != null)
            Props = props;
    }
}