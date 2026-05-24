namespace Cachemix.Abstractions;

/// <summary>
/// An immutable, point-in-time description of a single cache entry, as rendered
/// by the dashboard. Produced from the live registry; safe to serialize.
/// </summary>
public sealed record CacheEntrySnapshot
{
    /// <summary>The cache key, formatted as text.</summary>
    public required string Key { get; init; }

    /// <summary>The logical name of the cache holding the entry.</summary>
    public required string CacheName { get; init; }

    /// <summary>The kind of cache holding the entry.</summary>
    public required CacheKind CacheKind { get; init; }

    /// <summary>The CLR type name of the cached value, when known.</summary>
    public string? ValueTypeName { get; init; }

    /// <summary>The estimated size of the entry in bytes, when known.</summary>
    public long? EstimatedSizeBytes { get; init; }

    /// <summary>When the entry was first observed, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>When the entry was last read, in UTC, when known.</summary>
    public DateTimeOffset? LastAccessedUtc { get; init; }

    /// <summary>The absolute expiration instant, in UTC, when one is set.</summary>
    public DateTimeOffset? AbsoluteExpirationUtc { get; init; }

    /// <summary>The sliding expiration window, when one is set.</summary>
    public TimeSpan? SlidingExpiration { get; init; }

    /// <summary>The number of hits recorded against the entry.</summary>
    public long HitCount { get; init; }

    /// <summary>Tags associated with the entry. Populated for HybridCache entries.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The entry's cache priority, when known.</summary>
    public string? Priority { get; init; }

    /// <summary>
    /// <see langword="true"/> when the entry's expiration was inferred (for example,
    /// from a Redis key TTL) rather than directly observed through interception.
    /// </summary>
    public bool ExpirationInferred { get; init; }
}
