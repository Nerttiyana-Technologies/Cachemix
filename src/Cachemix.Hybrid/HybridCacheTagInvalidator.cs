using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Cachemix.Hybrid;

/// <summary>An <see cref="ICacheTagInvalidator"/> for the application's <c>HybridCache</c>.</summary>
internal sealed class HybridCacheTagInvalidator : ICacheTagInvalidator
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the invalidator.</summary>
    /// <param name="services">The application service provider.</param>
    public HybridCacheTagInvalidator(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public string CacheName => CachemixConstants.DefaultHybridCacheName;

    /// <inheritdoc />
    public async ValueTask InvalidateTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        HybridCache? cache = _services.GetService<HybridCache>();
        if (cache is not null)
        {
            await cache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        }
    }
}
