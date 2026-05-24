using Cachemix.Abstractions;

namespace Cachemix.AspNetCore;

/// <summary>
/// The complete dashboard payload sent to connected clients: per-cache
/// statistics, tracked entries, recent events, and the health report.
/// </summary>
public sealed record DashboardSnapshot
{
    /// <summary>Aggregate statistics for every observed cache.</summary>
    public required IReadOnlyList<CacheStatisticsSnapshot> Caches { get; init; }

    /// <summary>Every tracked cache entry.</summary>
    public required IReadOnlyList<CacheEntrySnapshot> Entries { get; init; }

    /// <summary>The most recent cache events, newest first.</summary>
    public required IReadOnlyList<CacheEvent> RecentEvents { get; init; }

    /// <summary>The current cache health report.</summary>
    public required CacheHealthReport Health { get; init; }

    /// <summary>When the snapshot was produced, in UTC.</summary>
    public DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Whether the dashboard is permitted to perform destructive actions such as eviction.</summary>
    public bool DestructiveActionsAllowed { get; init; }

    /// <summary>The name of the instance that produced this snapshot.</summary>
    public string InstanceName { get; init; } = string.Empty;

    /// <summary>Whether one or more peer instances are configured for the Multi-Instance Diff.</summary>
    public bool MultiInstanceEnabled { get; init; }
}
