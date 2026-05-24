using Cachemix.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cachemix.Core;

/// <summary>An <see cref="ICacheCommander"/> for the application's <c>IMemoryCache</c>.</summary>
internal sealed class MemoryCacheCommander : ICacheCommander
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the commander.</summary>
    /// <param name="services">The application service provider.</param>
    public MemoryCacheCommander(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public string CacheName => CachemixConstants.DefaultMemoryCacheName;

    /// <inheritdoc />
    public CacheKind CacheKind => CacheKind.Memory;

    /// <inheritdoc />
    public ValueTask EvictAsync(string key, CancellationToken cancellationToken = default)
    {
        _services.GetService<IMemoryCache>()?.Remove(key);
        return ValueTask.CompletedTask;
    }
}

/// <summary>An <see cref="ICacheCommander"/> for the application's <c>IDistributedCache</c>.</summary>
internal sealed class DistributedCacheCommander : ICacheCommander
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the commander.</summary>
    /// <param name="services">The application service provider.</param>
    public DistributedCacheCommander(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public string CacheName => CachemixConstants.DefaultDistributedCacheName;

    /// <inheritdoc />
    public CacheKind CacheKind => CacheKind.Distributed;

    /// <inheritdoc />
    public async ValueTask EvictAsync(string key, CancellationToken cancellationToken = default)
    {
        IDistributedCache? cache = _services.GetService<IDistributedCache>();
        if (cache is not null)
        {
            await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }
}
