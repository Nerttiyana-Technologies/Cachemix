using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// The read-only view of everything Cachemix has observed. Resolve this from
/// dependency injection to query cache telemetry — for example, from the
/// dashboard or a custom health check.
/// </summary>
public interface ICacheTelemetry
{
    /// <summary>Returns aggregate statistics for every observed cache.</summary>
    /// <returns>One <see cref="CacheStatisticsSnapshot"/> per cache.</returns>
    IReadOnlyList<CacheStatisticsSnapshot> GetStatistics();

    /// <summary>Returns tracked entries, optionally filtered to a single cache.</summary>
    /// <param name="cacheName">The cache to filter to, or <see langword="null"/> for all caches.</param>
    /// <returns>The matching entry snapshots.</returns>
    IReadOnlyList<CacheEntrySnapshot> GetEntries(string? cacheName = null);

    /// <summary>Returns the most recent cache events, newest first.</summary>
    /// <param name="maxEvents">The maximum number of events to return.</param>
    /// <returns>The recent events.</returns>
    IReadOnlyList<CacheEvent> GetRecentEvents(int maxEvents = 200);

    /// <summary>Computes a health report across every observed cache.</summary>
    /// <returns>The current <see cref="CacheHealthReport"/>.</returns>
    CacheHealthReport GetHealthReport();
}
