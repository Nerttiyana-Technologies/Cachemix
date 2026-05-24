using Cachemix.Core;
using Microsoft.Extensions.Options;

namespace Cachemix.AspNetCore;

/// <summary>Builds a <see cref="DashboardSnapshot"/> from the telemetry store.</summary>
internal sealed class DashboardSnapshotProvider
{
    private const int RecentEventCount = 200;

    private readonly ICacheTelemetry _telemetry;
    private readonly CachemixOptions _options;
    private readonly bool _multiInstanceEnabled;

    public DashboardSnapshotProvider(
        ICacheTelemetry telemetry,
        IOptions<CachemixOptions> options,
        IEnumerable<DashboardPeer> peers)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _multiInstanceEnabled = (peers ?? throw new ArgumentNullException(nameof(peers))).Any();
    }

    /// <summary>Captures the current dashboard snapshot.</summary>
    /// <returns>A fresh <see cref="DashboardSnapshot"/>.</returns>
    public DashboardSnapshot GetSnapshot() => new()
    {
        Caches = _telemetry.GetStatistics(),
        Entries = _telemetry.GetEntries(),
        RecentEvents = _telemetry.GetRecentEvents(RecentEventCount),
        Health = _telemetry.GetHealthReport(),
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        DestructiveActionsAllowed = _options.AllowDestructiveActions,
        InstanceName = _options.InstanceName,
        MultiInstanceEnabled = _multiInstanceEnabled,
    };
}
