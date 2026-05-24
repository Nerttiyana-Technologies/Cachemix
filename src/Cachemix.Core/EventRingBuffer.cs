using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// A fixed-capacity circular buffer of recent <see cref="CacheEvent"/>s. When
/// full, the oldest event is overwritten, so memory use is strictly bounded.
/// </summary>
internal sealed class EventRingBuffer
{
    private readonly CacheEvent[] _buffer;
    private readonly object _lock = new();
    private int _next;
    private int _count;

    /// <summary>Creates a ring buffer holding at most <paramref name="capacity"/> events.</summary>
    /// <param name="capacity">The maximum number of retained events.</param>
    public EventRingBuffer(int capacity)
    {
        _buffer = new CacheEvent[capacity < 1 ? 1 : capacity];
    }

    /// <summary>Adds an event, overwriting the oldest if the buffer is full.</summary>
    /// <param name="cacheEvent">The event to add.</param>
    public void Add(in CacheEvent cacheEvent)
    {
        lock (_lock)
        {
            _buffer[_next] = cacheEvent;
            _next = (_next + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count++;
            }
        }
    }

    /// <summary>Returns up to <paramref name="max"/> of the most recent events, newest first.</summary>
    /// <param name="max">The maximum number of events to return.</param>
    /// <returns>The most recent events, ordered newest to oldest.</returns>
    public IReadOnlyList<CacheEvent> Snapshot(int max)
    {
        lock (_lock)
        {
            int take = Math.Min(max, _count);
            var result = new CacheEvent[take];
            for (int i = 0; i < take; i++)
            {
                int index = (_next - 1 - i + _buffer.Length) % _buffer.Length;
                result[i] = _buffer[index];
            }

            return result;
        }
    }
}
