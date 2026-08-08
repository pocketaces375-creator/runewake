using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Runewake.Engine.Telemetry;
using Xunit;

namespace Runewake.Tests.Telemetry;

public class TelemetryBufferTests
{
    [Fact]
    public void Record_SingleEvent_CountIsOne()
    {
        var buf = new TelemetryBuffer(100);
        buf.Record(new TelemetryEvent("test_event"));
        Assert.Equal(1, buf.Count);
    }

    [Fact]
    public void Record_OverCapacity_OldestEvicted()
    {
        var buf = new TelemetryBuffer(50);
        for (int i = 0; i < 51; i++)
            buf.Record(new TelemetryEvent($"evt_{i}"));

        Assert.Equal(50, buf.Count);

        var drained = buf.Drain();
        Assert.Equal(50, drained.Count);
        // First event should be evt_1 (evt_0 was evicted)
        Assert.Equal("evt_1", drained[0].EventName);
        Assert.Equal("evt_50", drained[^1].EventName);
    }

    [Fact]
    public void Drain_ReturnsAllEvents_ClearsBuffer()
    {
        var buf = new TelemetryBuffer(100);
        buf.Record(new TelemetryEvent("a"));
        buf.Record(new TelemetryEvent("b"));
        buf.Record(new TelemetryEvent("c"));

        var first = buf.Drain();
        Assert.Equal(3, first.Count);
        Assert.Equal("a", first[0].EventName);
        Assert.Equal("c", first[2].EventName);

        // Buffer should be empty after drain
        Assert.Equal(0, buf.Count);
        var second = buf.Drain();
        Assert.Empty(second);
    }

    [Fact]
    public void Drain_EmptyBuffer_ReturnsEmptyList()
    {
        var buf = new TelemetryBuffer(100);
        var result = buf.Drain();
        Assert.Empty(result);
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Record_ThreadSafe_NoConcurrentExceptions()
    {
        var buf = new TelemetryBuffer(200);
        var tasks = new List<Task>();
        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    buf.Record(new TelemetryEvent($"t{t}_e{i}"));
            }));
        }

        Task.WaitAll(tasks.ToArray());

        Assert.Equal(200, buf.Count);
        // Drain should succeed without exception
        var drained = buf.Drain();
        Assert.Equal(200, drained.Count);
    }

    [Fact]
    public void TelemetryEvent_OccurredAt_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var evt = new TelemetryEvent("test");
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(evt.OccurredAt, before, after);
        Assert.Equal("test", evt.EventName);
        Assert.Empty(evt.Props);
    }

    [Fact]
    public void TelemetryEvent_WithProps_SetsValues()
    {
        var props = new Dictionary<string, string> { ["key"] = "value" };
        var evt = new TelemetryEvent("custom", props);

        Assert.Equal("custom", evt.EventName);
        Assert.Equal("value", evt.Props["key"]);
    }
}