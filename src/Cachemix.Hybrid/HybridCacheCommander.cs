using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Cachemix.Hybrid;

/// <summary>An <see cref="ICacheCommander"/> for the application's <c>HybridCache</c>.</summary>
internal sealed class HybridCacheCommander : ICacheCommander
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the commander.</summary>
    /// <param name="services">The application service provider.</param>
    public HybridCacheCommander(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public string CacheName => CachemixConstants.DefaultHybridCacheName;

    /// <inheritdoc />
    public CacheKind CacheKind => CacheKind.Hybrid;

    /// <inheritdoc />
    public async ValueTask EvictAsync(string key, CancellationToken cancellationToken = default)
    {
        HybridCache? cache = _services.GetService<HybridCache>();
        if (cache is not null)
        {
            await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }
}
