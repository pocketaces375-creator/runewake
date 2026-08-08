using System;
using System.Collections.Generic;

namespace Runewake.Engine.Telemetry;

/// <summary>
/// A bounded, thread-safe ring buffer for telemetry events.
/// Stores up to <see cref="Capacity"/> events. Oldest events are silently
/// evicted when the buffer is full. All public methods are thread-safe.
/// </summary>
public class TelemetryBuffer
{
    private readonly int _capacity;
    private readonly TelemetryEvent[] _buffer;
    private int _head;
    private int _count;
    private readonly object _lock = new();

    /// <summary>Current number of events in the buffer.</summary>
    public int Count
    {
        get { lock (_lock) return _count; }
    }

    /// <summary>Maximum number of events the buffer can hold.</summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Create a ring buffer with the given capacity.
    /// </summary>
    public TelemetryBuffer(int capacity = 500)
    {
        _capacity = Math.Max(1, capacity);
        _buffer = new TelemetryEvent[_capacity];
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Add an event to the buffer.
    /// If the buffer is full, the oldest event is evicted.
    /// Thread-safe.
    /// </summary>
    public void Record(TelemetryEvent e)
    {
        lock (_lock)
        {
            _buffer[_head] = e;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity)
                _count++;
        }
    }

    /// <summary>
    /// Returns all buffered events (in FIFO order) and clears the buffer.
    /// Thread-safe.
    /// </summary>
    public List<TelemetryEvent> Drain()
    {
        lock (_lock)
        {
            var result = new List<TelemetryEvent>(_count);

            if (_count == 0)
                return result;

            // Reconstruct FIFO order: oldest first
            int start = _head - _count;
            if (start < 0)
                start += _capacity;

            for (int i = 0; i < _count; i++)
            {
                int idx = (start + i) % _capacity;
                if (_buffer[idx] != null)
                    result.Add(_buffer[idx]);
            }

            _head = 0;
            _count = 0;
            return result;
        }
    }
}