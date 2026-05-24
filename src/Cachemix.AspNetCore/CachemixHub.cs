using Microsoft.AspNetCore.SignalR;

namespace Cachemix.AspNetCore;

/// <summary>
/// The SignalR hub that streams dashboard snapshots to connected clients. On
/// connect, the caller is authorized and sent one full snapshot; thereafter the
/// <see cref="DashboardBroadcaster"/> pushes updates.
/// </summary>
internal sealed class CachemixHub : Hub
{
    /// <summary>The client-side method invoked with each <see cref="DashboardSnapshot"/>.</summary>
    internal const string ReceiveSnapshotMethod = "ReceiveSnapshot";

    private readonly DashboardSnapshotProvider _snapshots;
    private readonly CachemixAuthorizer _authorizer;

    public CachemixHub(DashboardSnapshotProvider snapshots, CachemixAuthorizer authorizer)
    {
        _snapshots = snapshots;
        _authorizer = authorizer;
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        // The connection is authorized here as well as at the negotiate endpoint,
        // since the persistent connection does not pass through endpoint filters.
        if (!_authorizer.IsAuthorized(Context.GetHttpContext()))
        {
            Context.Abort();
            return;
        }

        await Clients.Caller
            .SendAsync(ReceiveSnapshotMethod, _snapshots.GetSnapshot())
            .ConfigureAwait(false);

        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
