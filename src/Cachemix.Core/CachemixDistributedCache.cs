using Cachemix.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cachemix.Core;

/// <summary>
/// An <see cref="IDistributedCache"/> decorator that observes cache operations
/// for Cachemix. Because <see cref="IDistributedCache"/> exposes no enumeration
/// API, this decorator reports only the keys it has observed passing through.
/// It never alters cache behavior.
/// </summary>
internal sealed class CachemixDistributedCache : IDistributedCache
{
    private const int MaxTelemetryFailures = 64;
    private const string ByteArrayTypeName = "System.Byte[]";

    private readonly IDistributedCache _inner;
    private readonly CacheEventPipeline _pipeline;
    private readonly CachemixOptions _options;
    private readonly ILogger _logger;
    private readonly string _cacheName;
    private readonly double _samplingRate;

    private int _telemetryFailures;
    private int _disableLogged;

    /// <summary>Wraps <paramref name="inner"/> with Cachemix observation.</summary>
    /// <param name="inner">The underlying distributed cache.</param>
    /// <param name="pipeline">The telemetry channel to publish signals to.</param>
    /// <param name="options">Cachemix options.</param>
    /// <param name="logger">The logger used to report (and only report) telemetry failures.</param>
    /// <param name="cacheName">The logical name reported for this cache.</param>
    public CachemixDistributedCache(
        IDistributedCache inner,
        CacheEventPipeline pipeline,
        IOptions<CachemixOptions> options,
        ILogger<CachemixDistributedCache> logger,
        string cacheName = CachemixConstants.DefaultDistributedCacheName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheName = cacheName;
        _samplingRate = Math.Clamp(_options.SamplingRate, 0d, 1d);
    }

    /// <summary>The wrapped cache. Used by the value inspector to read without recording telemetry.</summary>
    internal IDistributedCache Inner => _inner;

    /// <summary>The logical name reported for this cache.</summary>
    internal string CacheName => _cacheName;

    private bool TelemetryEnabled
        => _options.Enabled && Volatile.Read(ref _telemetryFailures) < MaxTelemetryFailures;

    /// <inheritdoc />
    public byte[]? Get(string key)
    {
        byte[]? value = _inner.Get(key);
        RecordLookup(key, value);
        return value;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        byte[]? value = await _inner.GetAsync(key, token).ConfigureAwait(false);
        RecordLookup(key, value);
        return value;
    }

    /// <inheritdoc />
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        _inner.Set(key, value, options);
        RecordSet(key, value, options);
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        await _inner.SetAsync(key, value, options, token).ConfigureAwait(false);
        RecordSet(key, value, options);
    }

    /// <inheritdoc />
    public void Refresh(string key) => _inner.Refresh(key);

    /// <inheritdoc />
    public Task RefreshAsync(string key, CancellationToken token = default)
        => _inner.RefreshAsync(key, token);

    /// <inheritdoc />
    public void Remove(string key)
    {
        _inner.Remove(key);
        RecordRemove(key);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        await _inner.RemoveAsync(key, token).ConfigureAwait(false);
        RecordRemove(key);
    }

    private void RecordLookup(string key, byte[]? value)
    {
        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            if (value is null)
            {
                if (ShouldSample())
                {
                    _pipeline.Publish(new CacheSignal(
                        _cacheName, CacheKind.Distributed, CacheEventKind.Miss, key, DateTimeOffset.UtcNow));
                }

                return;
            }

            // A hit also (re)registers the key: distributed caches cannot be
            // enumerated, so Cachemix tracks the keys it observes pass through.
            var descriptor = new CacheEntryDescriptor(key, _cacheName, CacheKind.Distributed)
            {
                ValueTypeName = ByteArrayTypeName,
                EstimatedSizeBytes = value.Length,
            };

            _pipeline.Publish(new CacheSignal(
                _cacheName, CacheKind.Distributed, CacheEventKind.Hit, key, DateTimeOffset.UtcNow)
            {
                Descriptor = descriptor,
                SizeBytes = value.Length,
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    private void RecordSet(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            var descriptor = new CacheEntryDescriptor(key, _cacheName, CacheKind.Distributed)
            {
                ValueTypeName = ByteArrayTypeName,
                EstimatedSizeBytes = value.Length,
                SlidingExpiration = options.SlidingExpiration,
                AbsoluteExpirationUtc = ResolveAbsoluteExpiration(options),
            };

            _pipeline.Publish(new CacheSignal(
                _cacheName, CacheKind.Distributed, CacheEventKind.Set, key, DateTimeOffset.UtcNow)
            {
                Descriptor = descriptor,
                SizeBytes = value.Length,
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    private void RecordRemove(string key)
    {
        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            _pipeline.Publish(new CacheSignal(
                _cacheName, CacheKind.Distributed, CacheEventKind.Remove, key, DateTimeOffset.UtcNow)
            {
                EvictionReason = CacheEvictionReason.Removed,
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    private bool ShouldSample()
        => _samplingRate >= 1d || Random.Shared.NextDouble() < _samplingRate;

    private void OnTelemetryFailure(Exception ex)
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

    private static DateTimeOffset? ResolveAbsoluteExpiration(DistributedCacheEntryOptions options)
    {
        if (options.AbsoluteExpirationRelativeToNow is { } relative)
        {
            return DateTimeOffset.UtcNow.Add(relative);
        }

        return options.AbsoluteExpiration?.ToUniversalTime();
    }
}
