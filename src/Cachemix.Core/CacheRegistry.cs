using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// The live, per-cache registry of tracked entries and aggregate counters.
/// All members are thread-safe.
/// </summary>
internal sealed class CacheRegistry
{
    private const long StampedeWindowTicks = TimeSpan.TicksPerSecond;

    private readonly ConcurrentDictionary<string, CacheEntryDescriptor> _entries =
        new(StringComparer.Ordinal);

    // Tracks the last miss time per key, used to detect concurrent recomputation.
    private readonly ConcurrentDictionary<string, long> _recentMissTicks =
        new(StringComparer.Ordinal);

    private readonly int _maxEntries;

    private long _hits;
    private long _misses;
    private long _sets;
    private long _evictions;
    private long _stampedeIncidents;

    /// <summary>Creates a registry for one cache.</summary>
    /// <param name="cacheName">The logical name of the cache.</param>
    /// <param name="cacheKind">The kind of cache.</param>
    /// <param name="maxEntries">The maximum number of entries to track.</param>
    public CacheRegistry(string cacheName, CacheKind cacheKind, int maxEntries)
    {
        CacheName = cacheName;
        CacheKind = cacheKind;
        _maxEntries = maxEntries < 1 ? 1 : maxEntries;
    }

    /// <summary>The logical name of the cache.</summary>
    public string CacheName { get; }

    /// <summary>The kind of cache.</summary>
    public CacheKind CacheKind { get; }

    /// <summary>Records a hit and updates the entry's access tracking if it is known.</summary>
    /// <param name="key">The hit key.</param>
    public void ApplyHit(string key)
    {
        Interlocked.Increment(ref _hits);
        if (_entries.TryGetValue(key, out CacheEntryDescriptor? descriptor))
        {
            descriptor.RecordHit();
        }
    }

    /// <summary>Records a miss, detecting a likely stampede on the same key.</summary>
    /// <param name="key">The missed key.</param>
    public void ApplyMiss(string key)
    {
        Interlocked.Increment(ref _misses);

        long now = DateTime.UtcNow.Ticks;
        if (_recentMissTicks.TryGetValue(key, out long previousMiss)
            && (now - previousMiss) < StampedeWindowTicks)
        {
            // The same key missed again before being repopulated — a likely
            // concurrent recomputation (cache stampede).
            Interlocked.Increment(ref _stampedeIncidents);
        }

        _recentMissTicks[key] = now;

        if (_recentMissTicks.Count > _maxEntries)
        {
            PruneRecentMisses(now);
        }
    }

    /// <summary>Records a created or replaced entry.</summary>
    /// <param name="descriptor">The descriptor of the entry.</param>
    public void ApplySet(CacheEntryDescriptor descriptor)
    {
        Interlocked.Increment(ref _sets);
        _entries[descriptor.Key] = descriptor;

        // The key is populated now; subsequent misses are fresh, not a stampede.
        _recentMissTicks.TryRemove(descriptor.Key, out _);

        EnforceCapacity();
    }

    /// <summary>Registers an observed entry only if its key is not already tracked.</summary>
    /// <param name="descriptor">The descriptor of the observed entry.</param>
    public void ObserveIfAbsent(CacheEntryDescriptor descriptor)
    {
        if (_entries.TryAdd(descriptor.Key, descriptor))
        {
            EnforceCapacity();
        }
    }

    /// <summary>Records the departure of an entry.</summary>
    /// <param name="key">The departed key.</param>
    /// <param name="eviction"><see langword="true"/> for an eviction, <see langword="false"/> for an explicit remove.</param>
    public void ApplyRemoval(string key, bool eviction)
    {
        if (eviction)
        {
            Interlocked.Increment(ref _evictions);
        }

        _entries.TryRemove(key, out _);
    }

    /// <summary>Attempts to retrieve the descriptor for <paramref name="key"/>.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="descriptor">The descriptor, when found.</param>
    /// <returns><see langword="true"/> when the key is tracked.</returns>
    public bool TryGetDescriptor(string key, [MaybeNullWhen(false)] out CacheEntryDescriptor descriptor)
        => _entries.TryGetValue(key, out descriptor);

    /// <summary>Produces immutable snapshots of every tracked entry.</summary>
    /// <returns>The entry snapshots.</returns>
    public IReadOnlyList<CacheEntrySnapshot> SnapshotEntries()
    {
        var list = new List<CacheEntrySnapshot>(_entries.Count);
        foreach (CacheEntryDescriptor descriptor in _entries.Values)
        {
            list.Add(descriptor.ToSnapshot());
        }

        return list;
    }

    /// <summary>Produces an aggregate statistics snapshot for this cache.</summary>
    /// <returns>The statistics snapshot.</returns>
    public CacheStatisticsSnapshot SnapshotStatistics() => new()
    {
        CacheName = CacheName,
        CacheKind = CacheKind,
        EntryCount = _entries.Count,
        TotalHits = Interlocked.Read(ref _hits),
        TotalMisses = Interlocked.Read(ref _misses),
        TotalSets = Interlocked.Read(ref _sets),
        TotalEvictions = Interlocked.Read(ref _evictions),
        StampedeIncidents = Interlocked.Read(ref _stampedeIncidents),
        EstimatedSizeBytes = EstimateTotalSize(),
    };

    private long? EstimateTotalSize()
    {
        long total = 0L;
        bool any = false;
        foreach (CacheEntryDescriptor descriptor in _entries.Values)
        {
            if (descriptor.EstimatedSizeBytes is { } size)
            {
                total += size;
                any = true;
            }
        }

        return any ? total : null;
    }

    private void PruneRecentMisses(long nowTicks)
    {
        long cutoff = nowTicks - StampedeWindowTicks;
        foreach (KeyValuePair<string, long> pair in _recentMissTicks)
        {
            if (pair.Value < cutoff)
            {
                _recentMissTicks.TryRemove(pair.Key, out _);
            }
        }
    }

    private void EnforceCapacity()
    {
        if (_entries.Count <= _maxEntries)
        {
            return;
        }

        int toRemove = _entries.Count - _maxEntries;
        foreach (KeyValuePair<string, CacheEntryDescriptor> pair in _entries
            .OrderBy(e => e.Value.LastAccessedUtc ?? e.Value.CreatedAtUtc)
            .Take(toRemove))
        {
            _entries.TryRemove(pair.Key, out _);
        }
    }
}
