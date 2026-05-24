namespace Cachemix.Abstractions;

/// <summary>
/// An immutable record of a single cache operation, written to the telemetry
/// pipeline on the hot path. Declared as a <see langword="readonly"/> struct so
/// that recording an event allocates nothing on the heap.
/// </summary>
/// <param name="CacheName">The logical name of the originating cache.</param>
/// <param name="CacheKind">The kind of the originating cache.</param>
/// <param name="Kind">The category of operation.</param>
/// <param name="Key">The affected cache key, formatted as text.</param>
/// <param name="TimestampUtc">When the operation occurred, in UTC.</param>
public readonly record struct CacheEvent(
    string CacheName,
    CacheKind CacheKind,
    CacheEventKind Kind,
    string Key,
    DateTimeOffset TimestampUtc)
{
    /// <summary>
    /// For a miss or set that ran a value factory, how long the factory took, in
    /// milliseconds; otherwise <see langword="null"/>.
    /// </summary>
    public double? DurationMs { get; init; }

    /// <summary>The size of the affected value in bytes, when known.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// For an <see cref="CacheEventKind.Eviction"/>, why the entry was evicted;
    /// otherwise <see langword="null"/>.
    /// </summary>
    public CacheEvictionReason? EvictionReason { get; init; }
}
