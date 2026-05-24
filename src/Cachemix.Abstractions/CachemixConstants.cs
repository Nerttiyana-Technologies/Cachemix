namespace Cachemix.Abstractions;

/// <summary>Well-known constant values shared across the Cachemix packages.</summary>
public static class CachemixConstants
{
    /// <summary>The default base path the dashboard is mounted at.</summary>
    public const string DefaultDashboardPath = "/cachemix";

    /// <summary>The name of the <c>System.Diagnostics.Metrics.Meter</c> that Cachemix publishes.</summary>
    public const string MeterName = "Cachemix";

    /// <summary>The default logical name used for the application's primary memory cache.</summary>
    public const string DefaultMemoryCacheName = "MemoryCache";

    /// <summary>The default logical name used for the application's primary distributed cache.</summary>
    public const string DefaultDistributedCacheName = "DistributedCache";

    /// <summary>The default logical name used for the application's HybridCache.</summary>
    public const string DefaultHybridCacheName = "HybridCache";
}
