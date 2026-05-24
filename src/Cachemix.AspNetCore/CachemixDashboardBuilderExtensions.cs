using Cachemix.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cachemix.AspNetCore;

/// <summary>Extends <see cref="ICachemixBuilder"/> with the dashboard.</summary>
public static class CachemixDashboardBuilderExtensions
{
    /// <summary>
    /// Registers the Cachemix dashboard services (SignalR, the snapshot
    /// broadcaster and the authorizer). Call this after <c>AddCachemix()</c>,
    /// then map the dashboard with <c>app.MapCachemix()</c>.
    /// </summary>
    /// <param name="builder">The Cachemix builder returned by <c>AddCachemix()</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ICachemixBuilder AddDashboard(this ICachemixBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IServiceCollection services = builder.Services;
        services.AddSignalR();
        services.TryAddSingleton<DashboardSnapshotProvider>();
        services.TryAddSingleton<CachemixAuthorizer>();
        services.AddHostedService<DashboardBroadcaster>();

        // Value inspector readers — one per cache kind, dispatched by name.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheValueReader, MemoryCacheValueReader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheValueReader, DistributedCacheValueReader>());

        // Multi-Instance Diff fetches peer snapshots over HTTP.
        services.AddHttpClient();
        services.TryAddSingleton<InstanceDiffService>();

        return builder;
    }

    /// <summary>Adds an authorization filter that gates access to the dashboard.</summary>
    /// <param name="builder">The Cachemix builder.</param>
    /// <param name="filter">The filter instance.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ICachemixBuilder AddAuthorizationFilter(
        this ICachemixBuilder builder,
        ICachemixAuthorizationFilter filter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(filter);

        builder.Services.AddSingleton(filter);
        return builder;
    }

    /// <summary>
    /// Restricts the dashboard to requests originating from the local machine.
    /// A convenient authorization filter for opting the dashboard into
    /// non-Development environments.
    /// </summary>
    /// <param name="builder">The Cachemix builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ICachemixBuilder AllowLocalRequestsOnly(this ICachemixBuilder builder)
        => builder.AddAuthorizationFilter(new LocalRequestsOnlyAuthorizationFilter());

    /// <summary>
    /// Registers a peer Cachemix dashboard for the Multi-Instance Diff. The diff
    /// compares this instance's cache state against every registered peer, so it
    /// can surface keys that are stale or missing on individual nodes.
    /// </summary>
    /// <param name="builder">The Cachemix builder.</param>
    /// <param name="name">A short, unique label for the peer instance.</param>
    /// <param name="url">The peer's dashboard base URL (for example, <c>http://node-b:8080/cachemix</c>).</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ICachemixBuilder AddPeer(this ICachemixBuilder builder, string name, string url)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed))
        {
            throw new ArgumentException($"'{url}' is not a valid absolute URL.", nameof(url));
        }

        builder.Services.AddSingleton(new DashboardPeer(name, parsed));
        return builder;
    }
}
