using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// The mutable, live record of a single tracked cache entry. Held in the
/// registry and projected to an immutable <see cref="CacheEntrySnapshot"/> for
/// the dashboard. Hit counting and last-access tracking are thread-safe.
/// </summary>
internal sealed class CacheEntryDescriptor
{
    private long _hitCount;
    private long _lastAccessedTicksUtc;

    /// <summary>Creates a descriptor for the entry stored under <paramref name="key"/>.</summary>
    /// <param name="key">The cache key, formatted as text.</param>
    /// <param name="cacheName">The logical name of the owning cache.</param>
    /// <param name="cacheKind">The kind of the owning cache.</param>
    public CacheEntryDescriptor(string key, string cacheName, CacheKind cacheKind)
    {
        Key = key;
        CacheName = cacheName;
        CacheKind = cacheKind;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The cache key, formatted as text.</summary>
    public string Key { get; }

    /// <summary>The logical name of the owning cache.</summary>
    public string CacheName { get; }

    /// <summary>The kind of the owning cache.</summary>
    public CacheKind CacheKind { get; }

    /// <summary>When the entry was first observed, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>The CLR type name of the cached value, when known.</summary>
    public string? ValueTypeName { get; set; }

    /// <summary>The estimated size of the entry in bytes, when known.</summary>
    public long? EstimatedSizeBytes { get; set; }

    /// <summary>The absolute expiration instant, in UTC, when one is set.</summary>
    public DateTimeOffset? AbsoluteExpirationUtc { get; set; }

    /// <summary>The sliding expiration window, when one is set.</summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>The entry's cache priority, when known.</summary>
    public string? Priority { get; set; }

    /// <summary>Tags associated with the entry. Populated for HybridCache entries.</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>Whether the entry's expiration was inferred rather than directly observed.</summary>
    public bool ExpirationInferred { get; set; }

    /// <summary>The number of hits recorded against this entry.</summary>
    public long HitCount => Interlocked.Read(ref _hitCount);

    /// <summary>When the entry was last read, in UTC, or <see langword="null"/> if never read.</summary>
    public DateTimeOffset? LastAccessedUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastAccessedTicksUtc);
            return ticks == 0L ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Records a hit against this entry and updates its last-access time.</summary>
    public void RecordHit()
    {
        Interlocked.Increment(ref _hitCount);
        Interlocked.Exchange(ref _lastAccessedTicksUtc, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>Produces an immutable snapshot of this entry for the dashboard.</summary>
    /// <returns>A <see cref="CacheEntrySnapshot"/> describing the entry.</returns>
    public CacheEntrySnapshot ToSnapshot() => new()
    {
        Key = Key,
        CacheName = CacheName,
        CacheKind = CacheKind,
        ValueTypeName = ValueTypeName,
        EstimatedSizeBytes = EstimatedSizeBytes,
        CreatedAtUtc = CreatedAtUtc,
        LastAccessedUtc = LastAccessedUtc,
        AbsoluteExpirationUtc = AbsoluteExpirationUtc,
        SlidingExpiration = SlidingExpiration,
        HitCount = HitCount,
        Tags = Tags,
        Priority = Priority,
        ExpirationInferred = ExpirationInferred,
    };
}
