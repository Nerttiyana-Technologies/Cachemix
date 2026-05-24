using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cachemix.AspNetCore;

/// <summary>
/// A background service that pushes a fresh <see cref="DashboardSnapshot"/> to
/// every connected dashboard client on a fixed interval.
/// </summary>
internal sealed class DashboardBroadcaster : BackgroundService
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(2);

    private readonly IHubContext<CachemixHub> _hub;
    private readonly DashboardSnapshotProvider _snapshots;
    private readonly ILogger<DashboardBroadcaster> _logger;

    public DashboardBroadcaster(
        IHubContext<CachemixHub> hub,
        DashboardSnapshotProvider snapshots,
        ILogger<DashboardBroadcaster> logger)
    {
        _hub = hub;
        _snapshots = snapshots;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BroadcastInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    DashboardSnapshot snapshot = _snapshots.GetSnapshot();
                    await _hub.Clients.All
                        .SendAsync(CachemixHub.ReceiveSnapshotMethod, snapshot, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    DashboardLog.BroadcastFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown.
        }
    }
}
