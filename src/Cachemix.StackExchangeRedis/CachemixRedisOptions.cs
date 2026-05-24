using Cachemix.Abstractions;

namespace Cachemix.StackExchangeRedis;

/// <summary>Configuration for the Cachemix Redis key-enumeration provider.</summary>
public sealed class CachemixRedisOptions
{
    /// <summary>
    /// The logical cache name reported for enumerated keys. Defaults to the same
    /// name the decorated <c>IDistributedCache</c> uses, so the dashboard merges
    /// observed and enumerated keys into one view.
    /// </summary>
    public string CacheName { get; set; } = CachemixConstants.DefaultDistributedCacheName;

    /// <summary>The Redis database index to enumerate. <c>-1</c> uses the default database.</summary>
    public int Database { get; set; } = -1;

    /// <summary>
    /// The key prefix to scope enumeration to (typically the value of
    /// <c>RedisCacheOptions.InstanceName</c>). When set, only matching keys are
    /// scanned and the prefix is stripped from displayed key names. When
    /// <see langword="null"/>, every key in the database is enumerated.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>The page size used for the cursor-based <c>SCAN</c>.</summary>
    public int ScanPageSize { get; set; } = 250;

    /// <summary>
    /// The Redis connection string. Set automatically when the connection-string
    /// overload of <c>AddRedisTelemetry</c> is used.
    /// </summary>
    public string? ConnectionString { get; set; }
}
