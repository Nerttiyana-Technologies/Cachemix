using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// Supplies a full key listing for a cache that supports enumeration — for
/// example, the Redis provider, which can <c>SCAN</c> the keyspace. Register
/// implementations in dependency injection; the dashboard consults every
/// registered provider to show keys beyond those observed through interception.
/// </summary>
public interface ICacheKeyProvider
{
    /// <summary>The logical name of the cache this provider enumerates.</summary>
    string CacheName { get; }

    /// <summary>The kind of cache this provider enumerates.</summary>
    CacheKind CacheKind { get; }

    /// <summary>Enumerates the entries currently held by the cache.</summary>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>A snapshot of the cache's entries.</returns>
    ValueTask<IReadOnlyList<CacheEntrySnapshot>> EnumerateAsync(CancellationToken cancellationToken = default);
}
