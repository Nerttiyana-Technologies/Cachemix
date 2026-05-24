using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>Controls whether and how cache entry values are read for display.</summary>
public enum ValueCaptureMode
{
    /// <summary>Never read values; show only type and size.</summary>
    Never,

    /// <summary>Read a value only when the dashboard explicitly requests it.</summary>
    OnDemand,

    /// <summary>Capture values eagerly as entries are observed.</summary>
    Always,
}

/// <summary>Controls how entry sizes are estimated when the application does not supply one.</summary>
public enum EntrySizeMode
{
    /// <summary>Do not estimate; report only sizes the application supplies.</summary>
    Off,

    /// <summary>Estimate cheaply for well-known value types.</summary>
    Sampled,

    /// <summary>Always estimate when a size is not supplied.</summary>
    Always,
}

/// <summary>
/// Configuration for Cachemix. Bind from configuration or set it in the
/// <c>AddCachemix</c> callback. Every default is deliberately safe.
/// </summary>
public sealed class CachemixOptions
{
    /// <summary>
    /// Master switch. When <see langword="false"/>, no interception is registered
    /// and Cachemix has zero runtime cost.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Allows the dashboard to mount outside the Development environment. When set,
    /// at least one authorization filter must also be configured or startup fails.
    /// </summary>
    public bool EnableOutsideDevelopment { get; set; }

    /// <summary>The base path the dashboard is mounted at.</summary>
    public string DashboardPath { get; set; } = CachemixConstants.DefaultDashboardPath;

    /// <summary>
    /// A short, human-readable name for this running instance. It labels this
    /// instance in the dashboard's Multi-Instance Diff and is reported in the
    /// snapshot. Defaults to the machine name.
    /// </summary>
    public string InstanceName { get; set; } = Environment.MachineName;

    /// <summary>The fraction of in-memory lookups recorded as events, from 0 to 1.</summary>
    public double SamplingRate { get; set; } = 1.0d;

    /// <summary>The maximum number of entries tracked per cache. Oldest-by-access entries are dropped first.</summary>
    public int MaxTrackedEntries { get; set; } = 50_000;

    /// <summary>The capacity of the recent-events ring buffer.</summary>
    public int EventBufferCapacity { get; set; } = 5_000;

    /// <summary>The capacity of the internal telemetry channel. Signals beyond this are dropped, never blocked.</summary>
    public int SignalChannelCapacity { get; set; } = 16_384;

    /// <summary>Whether entry values are read for the inspector.</summary>
    public ValueCaptureMode CaptureValues { get; set; } = ValueCaptureMode.OnDemand;

    /// <summary>How entry sizes are estimated.</summary>
    public EntrySizeMode EstimateEntrySize { get; set; } = EntrySizeMode.Sampled;

    /// <summary>Whether the dashboard may evict keys, drop namespaces, or flush caches.</summary>
    public bool AllowDestructiveActions { get; set; } = true;

    /// <summary>Whether the dashboard may import cache state from JSON. Off by default for safety.</summary>
    public bool AllowStateImport { get; set; }

    /// <summary>Whether the OpenTelemetry meter is published.</summary>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>Glob patterns whose entry values are always masked in the dashboard.</summary>
    public IList<string> RedactionPatterns { get; } =
        new List<string> { "*secret*", "*token*", "*password*", "*pii*" };

    /// <summary>Redis-specific telemetry options.</summary>
    public RedisTelemetryOptions Redis { get; } = new();
}

/// <summary>Redis-specific telemetry options.</summary>
public sealed class RedisTelemetryOptions
{
    /// <summary>
    /// Subscribe to Redis keyspace notifications so server-side evictions and
    /// expirations appear in the event feed. The Redis server must be configured
    /// with <c>notify-keyspace-events</c> for this to have any effect.
    /// </summary>
    public bool SubscribeKeyspaceNotifications { get; set; }
}
