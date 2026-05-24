namespace Cachemix.Abstractions;

/// <summary>The category of a recorded cache operation.</summary>
public enum CacheEventKind
{
    /// <summary>A lookup that found a live entry.</summary>
    Hit,

    /// <summary>A lookup that did not find a live entry.</summary>
    Miss,

    /// <summary>An entry was created or overwritten.</summary>
    Set,

    /// <summary>An entry was explicitly removed by the application or the dashboard.</summary>
    Remove,

    /// <summary>
    /// An entry left the cache for a reason other than an explicit remove.
    /// The reason is carried by <see cref="CacheEvent.EvictionReason"/>.
    /// </summary>
    Eviction,
}
