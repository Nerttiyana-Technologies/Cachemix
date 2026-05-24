namespace Cachemix.Core;

/// <summary>
/// Invalidates every cache entry carrying a given tag. Registered only for
/// caches with native tag support (HybridCache); the dashboard dispatches by
/// <see cref="CacheName"/> and offers tag invalidation only when a matching
/// implementation is present.
/// </summary>
public interface ICacheTagInvalidator
{
    /// <summary>The logical name of the cache this invalidator acts on.</summary>
    string CacheName { get; }

    /// <summary>Removes every entry carrying <paramref name="tag"/>.</summary>
    /// <param name="tag">The tag to invalidate.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes once invalidation has been requested.</returns>
    ValueTask InvalidateTagAsync(string tag, CancellationToken cancellationToken = default);
}
