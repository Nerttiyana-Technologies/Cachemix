namespace Cachemix.Core;

/// <summary>The outcome of an on-demand attempt to read a cache entry's value.</summary>
public enum CacheValueStatus
{
    /// <summary>The value was read and is available for display.</summary>
    Available,

    /// <summary>No live entry exists under the requested key.</summary>
    NotFound,

    /// <summary>The value exists but is masked by the redaction policy.</summary>
    Redacted,

    /// <summary>
    /// Value reading is switched off by
    /// <see cref="CachemixOptions.CaptureValues"/>.
    /// </summary>
    Disabled,

    /// <summary>The cache cannot read values back (for example, HybridCache).</summary>
    Unsupported,
}

/// <summary>
/// The result of an on-demand read of a single cache entry's value for the
/// dashboard's value inspector. Immutable; safe to serialize directly.
/// </summary>
public sealed record CacheValueResult
{
    /// <summary>The outcome of the read.</summary>
    public required CacheValueStatus Status { get; init; }

    /// <summary>
    /// The value rendered as display text. Populated only when
    /// <see cref="Status"/> is <see cref="CacheValueStatus.Available"/>.
    /// </summary>
    public string? ValueText { get; init; }

    /// <summary>The CLR type name of the value, when known.</summary>
    public string? ValueTypeName { get; init; }

    /// <summary>The size of the value in bytes, when known.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>
    /// Whether <see cref="ValueText"/> was clipped to fit the display cap.
    /// </summary>
    public bool Truncated { get; init; }

    /// <summary>A result indicating no entry exists under the requested key.</summary>
    public static CacheValueResult NotFound { get; } =
        new() { Status = CacheValueStatus.NotFound };

    /// <summary>A result indicating value reading is switched off.</summary>
    public static CacheValueResult Disabled { get; } =
        new() { Status = CacheValueStatus.Disabled };

    /// <summary>A result indicating the value is masked by the redaction policy.</summary>
    public static CacheValueResult Redacted { get; } =
        new() { Status = CacheValueStatus.Redacted };

    /// <summary>A result indicating the cache cannot read values back.</summary>
    public static CacheValueResult Unsupported { get; } =
        new() { Status = CacheValueStatus.Unsupported };
}

/// <summary>
/// Reads a single cache entry's value on demand for the dashboard's value
/// inspector. One implementation is registered per cache; the dashboard
/// dispatches by <see cref="CacheName"/>.
/// </summary>
/// <remarks>
/// A read goes straight to the underlying store and bypasses Cachemix
/// telemetry, so inspecting a value never records a synthetic hit. It may,
/// however, renew a sliding expiration — the underlying cache APIs expose no
/// side-effect-free peek.
/// </remarks>
public interface ICacheValueReader
{
    /// <summary>The logical name of the cache this reader reads from.</summary>
    string CacheName { get; }

    /// <summary>Reads the value stored under <paramref name="key"/>.</summary>
    /// <param name="key">The cache key to read, formatted as text.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="CacheValueResult"/> describing the value or why it is unavailable.</returns>
    ValueTask<CacheValueResult> ReadAsync(string key, CancellationToken cancellationToken = default);
}
