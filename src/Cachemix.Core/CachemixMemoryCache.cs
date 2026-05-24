using Cachemix.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cachemix.Core;

/// <summary>
/// An <see cref="IMemoryCache"/> decorator that observes every cache operation
/// for Cachemix. It never alters cache behavior: any telemetry failure is
/// swallowed and the underlying cache call always proceeds.
/// </summary>
internal sealed class CachemixMemoryCache : IMemoryCache
{
    private const int MaxTelemetryFailures = 64;

    private readonly IMemoryCache _inner;
    private readonly CacheEventPipeline _pipeline;
    private readonly CachemixOptions _options;
    private readonly ILogger _logger;
    private readonly string _cacheName;
    private readonly double _samplingRate;

    private int _telemetryFailures;
    private int _disableLogged;

    /// <summary>Wraps <paramref name="inner"/> with Cachemix observation.</summary>
    /// <param name="inner">The underlying memory cache.</param>
    /// <param name="pipeline">The telemetry channel to publish signals to.</param>
    /// <param name="options">Cachemix options.</param>
    /// <param name="logger">The logger used to report (and only report) telemetry failures.</param>
    /// <param name="cacheName">The logical name reported for this cache.</param>
    public CachemixMemoryCache(
        IMemoryCache inner,
        CacheEventPipeline pipeline,
        IOptions<CachemixOptions> options,
        ILogger<CachemixMemoryCache> logger,
        string cacheName = CachemixConstants.DefaultMemoryCacheName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheName = cacheName;
        _samplingRate = Math.Clamp(_options.SamplingRate, 0d, 1d);
    }

    /// <summary>The wrapped cache. Used by the reconciliation sweep.</summary>
    internal IMemoryCache Inner => _inner;

    /// <summary>The logical name reported for this cache.</summary>
    internal string CacheName => _cacheName;

    private bool TelemetryEnabled
        => _options.Enabled && Volatile.Read(ref _telemetryFailures) < MaxTelemetryFailures;

    /// <inheritdoc />
    public ICacheEntry CreateEntry(object key)
    {
        ICacheEntry entry = _inner.CreateEntry(key);
        if (!TelemetryEnabled)
        {
            return entry;
        }

        try
        {
            return new CachemixCacheEntry(entry, this);
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
            return entry;
        }
    }

    /// <inheritdoc />
    public bool TryGetValue(object key, out object? value)
    {
        bool found = _inner.TryGetValue(key, out value);
        if (TelemetryEnabled)
        {
            try
            {
                RecordLookup(found ? CacheEventKind.Hit : CacheEventKind.Miss, key);
            }
            catch (Exception ex)
            {
                OnTelemetryFailure(ex);
            }
        }

        return found;
    }

    /// <inheritdoc />
    public void Remove(object key)
    {
        // The underlying remove fires our post-eviction callback with reason
        // Removed, which is where the telemetry for a removal is recorded.
        _inner.Remove(key);
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();

    /// <summary>Builds a descriptor from a committing entry. Called by <see cref="CachemixCacheEntry"/>.</summary>
    /// <param name="entry">The entry being committed.</param>
    /// <returns>A descriptor capturing the entry's metadata.</returns>
    internal CacheEntryDescriptor CaptureDescriptor(ICacheEntry entry)
    {
        string keyText = CacheKeyFormatter.Format(entry.Key);
        object? value = entry.Value;

        return new CacheEntryDescriptor(keyText, _cacheName, CacheKind.Memory)
        {
            ValueTypeName = value?.GetType().FullName,
            Priority = entry.Priority.ToString(),
            SlidingExpiration = entry.SlidingExpiration,
            AbsoluteExpirationUtc = ResolveAbsoluteExpiration(entry),
            EstimatedSizeBytes = ResolveSize(entry, value),
        };
    }

    /// <summary>Records a committed entry. Called by <see cref="CachemixCacheEntry"/>.</summary>
    /// <param name="descriptor">The descriptor of the committed entry.</param>
    internal void OnEntrySet(CacheEntryDescriptor descriptor)
    {
        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            _pipeline.Publish(new CacheSignal(
                _cacheName, CacheKind.Memory, CacheEventKind.Set, descriptor.Key, DateTimeOffset.UtcNow)
            {
                Descriptor = descriptor,
                SizeBytes = descriptor.EstimatedSizeBytes,
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    /// <summary>The post-eviction callback registered on every entry created through this decorator.</summary>
    /// <param name="key">The evicted key.</param>
    /// <param name="value">The evicted value.</param>
    /// <param name="reason">Why the entry was evicted.</param>
    /// <param name="state">Unused callback state.</param>
    internal void OnEntryEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            string keyText = CacheKeyFormatter.Format(key);
            CacheEventKind kind = reason == EvictionReason.Removed
                ? CacheEventKind.Remove
                : CacheEventKind.Eviction;

            _pipeline.Publish(new CacheSignal(_cacheName, CacheKind.Memory, kind, keyText, DateTimeOffset.UtcNow)
            {
                EvictionReason = MapReason(reason),
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    /// <summary>Records a telemetry failure, self-disabling after a sustained run of errors.</summary>
    /// <param name="ex">The failure.</param>
    internal void OnTelemetryFailure(Exception ex)
    {
        int failures = Interlocked.Increment(ref _telemetryFailures);
        if (failures == 1)
        {
            Log.CacheTelemetryError(_logger, ex, _cacheName);
        }
        else if (failures >= MaxTelemetryFailures && Interlocked.Exchange(ref _disableLogged, 1) == 0)
        {
            Log.CacheTelemetryDisabled(_logger, _cacheName, failures);
        }
    }

    private void RecordLookup(CacheEventKind kind, object key)
    {
        if (!ShouldSample())
        {
            return;
        }

        string keyText = CacheKeyFormatter.Format(key);
        _pipeline.Publish(new CacheSignal(_cacheName, CacheKind.Memory, kind, keyText, DateTimeOffset.UtcNow));
    }

    private bool ShouldSample()
        => _samplingRate >= 1d || Random.Shared.NextDouble() < _samplingRate;

    private long? ResolveSize(ICacheEntry entry, object? value)
    {
        if (entry.Size is { } explicitSize)
        {
            return explicitSize;
        }

        return _options.EstimateEntrySize == EntrySizeMode.Off
            ? null
            : SizeEstimator.TryEstimate(value);
    }

    private static DateTimeOffset? ResolveAbsoluteExpiration(ICacheEntry entry)
    {
        if (entry.AbsoluteExpirationRelativeToNow is { } relative)
        {
            return DateTimeOffset.UtcNow.Add(relative);
        }

        return entry.AbsoluteExpiration?.ToUniversalTime();
    }

    private static CacheEvictionReason MapReason(EvictionReason reason) => reason switch
    {
        EvictionReason.Removed => CacheEvictionReason.Removed,
        EvictionReason.Replaced => CacheEvictionReason.Replaced,
        EvictionReason.Expired => CacheEvictionReason.Expired,
        EvictionReason.TokenExpired => CacheEvictionReason.TokenExpired,
        EvictionReason.Capacity => CacheEvictionReason.Capacity,
        _ => CacheEvictionReason.None,
    };
}
