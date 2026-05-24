namespace Cachemix.Abstractions;

/// <summary>
/// Why a cache entry was evicted. Mirrors the reasons surfaced by the
/// <c>EvictionReason</c> enum in <c>Microsoft.Extensions.Caching.Memory</c>.
/// </summary>
public enum CacheEvictionReason
{
    /// <summary>No reason was supplied.</summary>
    None,

    /// <summary>The entry was manually removed.</summary>
    Removed,

    /// <summary>The entry was overwritten by a new value for the same key.</summary>
    Replaced,

    /// <summary>The entry's absolute or sliding expiration elapsed.</summary>
    Expired,

    /// <summary>An associated change or expiration token fired.</summary>
    TokenExpired,

    /// <summary>The entry was evicted to keep the cache within its size limit.</summary>
    Capacity,
}
