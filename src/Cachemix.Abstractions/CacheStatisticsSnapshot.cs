namespace Cachemix.Abstractions;

/// <summary>Aggregate counters for a single cache, captured at a point in time.</summary>
public sealed record CacheStatisticsSnapshot
{
    /// <summary>The logical name of the cache.</summary>
    public required string CacheName { get; init; }

    /// <summary>The kind of cache.</summary>
    public required CacheKind CacheKind { get; init; }

    /// <summary>The number of live entries currently tracked.</summary>
    public long EntryCount { get; init; }

    /// <summary>Total hits recorded since process start.</summary>
    public long TotalHits { get; init; }

    /// <summary>Total misses recorded since process start.</summary>
    public long TotalMisses { get; init; }

    /// <summary>Total sets recorded since process start.</summary>
    public long TotalSets { get; init; }

    /// <summary>Total evictions recorded since process start.</summary>
    public long TotalEvictions { get; init; }

    /// <summary>
    /// The number of likely cache stampedes detected — the same key missing
    /// repeatedly within a short window before being repopulated.
    /// </summary>
    public long StampedeIncidents { get; init; }

    /// <summary>The estimated total size of tracked entries in bytes, when known.</summary>
    public long? EstimatedSizeBytes { get; init; }

    /// <summary>
    /// The ratio of hits to total lookups (<see cref="TotalHits"/> + <see cref="TotalMisses"/>),
    /// in the range 0 to 1. Returns 0 when no lookups have been recorded.
    /// </summary>
    public double HitRatio
    {
        get
        {
            long lookups = TotalHits + TotalMisses;
            return lookups == 0L ? 0d : (double)TotalHits / lookups;
        }
    }
}
