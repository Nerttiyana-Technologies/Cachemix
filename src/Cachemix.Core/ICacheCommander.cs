using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// Performs destructive actions on a cache for the dashboard's Nuke button.
/// One implementation is registered per cache; the dashboard dispatches by
/// <see cref="CacheName"/>.
/// </summary>
public interface ICacheCommander
{
    /// <summary>The logical name of the cache this commander acts on.</summary>
    string CacheName { get; }

    /// <summary>The kind of cache this commander acts on.</summary>
    CacheKind CacheKind { get; }

    /// <summary>Evicts a single key from the cache.</summary>
    /// <param name="key">The key to evict.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes once the eviction has been requested.</returns>
    ValueTask EvictAsync(string key, CancellationToken cancellationToken = default);
}
