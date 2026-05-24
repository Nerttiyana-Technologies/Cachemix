using System.Diagnostics;
using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cachemix.Hybrid;

/// <summary>
/// A <see cref="HybridCache"/> decorator that observes cache operations for
/// Cachemix, including tags and value-factory timing. It never alters cache
/// behavior: a telemetry failure is swallowed and the cache call always proceeds.
/// </summary>
internal sealed class CachemixHybridCache : HybridCache
{
    private const int MaxTelemetryFailures = 64;

    private readonly HybridCache _inner;
    private readonly CacheEventPipeline _pipeline;
    private readonly CachemixOptions _options;
    private readonly ILogger _logger;
    private readonly string _cacheName;

    private int _telemetryFailures;
    private int _disableLogged;

    /// <summary>Wraps <paramref name="inner"/> with Cachemix observation.</summary>
    /// <param name="inner">The underlying hybrid cache.</param>
    /// <param name="pipeline">The telemetry channel to publish signals to.</param>
    /// <param name="options">Cachemix options.</param>
    /// <param name="logger">The logger used to report (and only report) telemetry failures.</param>
    /// <param name="cacheName">The logical name reported for this cache.</param>
    public CachemixHybridCache(
        HybridCache inner,
        CacheEventPipeline pipeline,
        IOptions<CachemixOptions> options,
        ILogger<CachemixHybridCache> logger,
        string cacheName = CachemixConstants.DefaultHybridCacheName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheName = cacheName;
    }

    private bool TelemetryEnabled
        => _options.Enabled && Volatile.Read(ref _telemetryFailures) < MaxTelemetryFailures;

    /// <inheritdoc />
    public override async ValueTask<T> GetOrCreateAsync<TState, T>(
        string key,
        TState state,
        Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options,
        IEnumerable<string>? tags,
        CancellationToken cancellationToken)
    {
        if (!TelemetryEnabled)
        {
            return await _inner
                .GetOrCreateAsync(key, state, factory, options, tags, cancellationToken)
                .ConfigureAwait(false);
        }

        bool factoryRan = false;
        long factoryStart = 0L;
        long factoryEnd = 0L;

        // The factory only runs on a miss. Wrapping it lets Cachemix detect
        // hit vs. miss and time the miss (the database trip).
        async ValueTask<T> Tracked(TState innerState, CancellationToken token)
        {
            factoryRan = true;
            factoryStart = Stopwatch.GetTimestamp();
            try
            {
                return await factory(innerState, token).ConfigureAwait(false);
            }
            finally
            {
                factoryEnd = Stopwatch.GetTimestamp();
            }
        }

        T result = await _inner
            .GetOrCreateAsync(key, state, Tracked, options, tags, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            double factoryMs = factoryRan
                ? Stopwatch.GetElapsedTime(factoryStart, factoryEnd).TotalMilliseconds
                : 0d;
            RecordGetOrCreate<T>(key, factoryRan, factoryMs, options, tags);
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }

        return result;
    }

    /// <inheritdoc />
    public override async ValueTask SetAsync<T>(
        string key,
        T value,
        HybridCacheEntryOptions? options,
        IEnumerable<string>? tags,
        CancellationToken cancellationToken)
    {
        await _inner.SetAsync(key, value, options, tags, cancellationToken).ConfigureAwait(false);

        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            _pipeline.Publish(new CacheSignal(
                _cacheName, CacheKind.Hybrid, CacheEventKind.Set, key, DateTimeOffset.UtcNow)
            {
                Descriptor = BuildDescriptor<T>(key, options, tags),
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    /// <inheritdoc />
    public override async ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        await _inner.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            _pipeline.Publish(new CacheSignal(
                _cacheName, CacheKind.Hybrid, CacheEventKind.Remove, key, DateTimeOffset.UtcNow)
            {
                EvictionReason = CacheEvictionReason.Removed,
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    /// <inheritdoc />
    public override async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        await _inner.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);

        if (!TelemetryEnabled)
        {
            return;
        }

        try
        {
            // Tag invalidation is surfaced in the event feed. Individual entries
            // carrying the tag are reconciled out of the registry as they next
            // expire — HybridCache does not expose the affected key set.
            _pipeline.Publish(new CacheSignal(
                _cacheName, CacheKind.Hybrid, CacheEventKind.Remove, $"#tag:{tag}", DateTimeOffset.UtcNow)
            {
                EvictionReason = CacheEvictionReason.Removed,
            });
        }
        catch (Exception ex)
        {
            OnTelemetryFailure(ex);
        }
    }

    private void RecordGetOrCreate<T>(
        string key,
        bool wasMiss,
        double factoryMs,
        HybridCacheEntryOptions? options,
        IEnumerable<string>? tags)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (!wasMiss)
        {
            _pipeline.Publish(new CacheSignal(_cacheName, CacheKind.Hybrid, CacheEventKind.Hit, key, now)
            {
                Descriptor = BuildDescriptor<T>(key, options, tags),
            });
            return;
        }

        _pipeline.Publish(new CacheSignal(_cacheName, CacheKind.Hybrid, CacheEventKind.Miss, key, now)
        {
            DurationMs = factoryMs,
        });

        _pipeline.Publish(new CacheSignal(_cacheName, CacheKind.Hybrid, CacheEventKind.Set, key, now)
        {
            Descriptor = BuildDescriptor<T>(key, options, tags),
        });
    }

    private CacheEntryDescriptor BuildDescriptor<T>(
        string key,
        HybridCacheEntryOptions? options,
        IEnumerable<string>? tags)
    {
        IReadOnlyList<string> tagList = tags is null ? [] : [.. tags];
        return new CacheEntryDescriptor(key, _cacheName, CacheKind.Hybrid)
        {
            ValueTypeName = typeof(T).FullName,
            Tags = tagList,
            AbsoluteExpirationUtc = options?.Expiration is { } expiration
                ? DateTimeOffset.UtcNow.Add(expiration)
                : null,
        };
    }

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
}
