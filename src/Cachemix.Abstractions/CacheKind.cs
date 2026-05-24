namespace Cachemix.Abstractions;

/// <summary>
/// Identifies which kind of .NET cache abstraction an entry or event originated from.
/// </summary>
public enum CacheKind
{
    /// <summary>An <c>IMemoryCache</c> instance.</summary>
    Memory,

    /// <summary>An <c>IDistributedCache</c> instance, such as Redis.</summary>
    Distributed,

    /// <summary>A <c>HybridCache</c> instance (combined L1 in-memory and L2 distributed tiers).</summary>
    Hybrid,
}
