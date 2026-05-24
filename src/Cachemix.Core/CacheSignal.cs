using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// The internal carrier written to the telemetry channel by the cache
/// decorators. A <see langword="readonly"/> struct, so publishing a lookup
/// event allocates nothing on the heap.
/// </summary>
internal readonly struct CacheSignal
{
    /// <summary>Creates a signal for a cache operation.</summary>
    /// <param name="cacheName">The logical name of the originating cache.</param>
    /// <param name="cacheKind">The kind of the originating cache.</param>
    /// <param name="kind">The category of operation.</param>
    /// <param name="key">The affected cache key, formatted as text.</param>
    /// <param name="timestampUtc">When the operation occurred, in UTC.</param>
    public CacheSignal(
        string cacheName,
        CacheKind cacheKind,
        CacheEventKind kind,
        string key,
        DateTimeOffset timestampUtc)
    {
        CacheName = cacheName;
        CacheKind = cacheKind;
        Kind = kind;
        Key = key;
        TimestampUtc = timestampUtc;
    }

    /// <summary>The logical name of the originating cache.</summary>
    public string CacheName { get; }

    /// <summary>The kind of the originating cache.</summary>
    public CacheKind CacheKind { get; }

    /// <summary>The category of operation.</summary>
    public CacheEventKind Kind { get; }

    /// <summary>The affected cache key, formatted as text.</summary>
    public string Key { get; }

    /// <summary>When the operation occurred, in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>The full descriptor, supplied for sets and for observed distributed hits.</summary>
    public CacheEntryDescriptor? Descriptor { get; init; }

    /// <summary>The eviction reason, supplied for removals and evictions.</summary>
    public CacheEvictionReason? EvictionReason { get; init; }

    /// <summary>The value-factory duration in milliseconds, when a factory ran.</summary>
    public double? DurationMs { get; init; }

    /// <summary>The affected value size in bytes, when known.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Projects this signal to the public <see cref="CacheEvent"/> for the event feed.</summary>
    /// <returns>A <see cref="CacheEvent"/> carrying this signal's public fields.</returns>
    public CacheEvent ToEvent() => new(CacheName, CacheKind, Kind, Key, TimestampUtc)
    {
        DurationMs = DurationMs,
        SizeBytes = SizeBytes,
        EvictionReason = EvictionReason,
    };
}
