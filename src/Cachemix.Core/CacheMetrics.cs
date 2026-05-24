using System.Diagnostics.Metrics;
using Cachemix.Abstractions;
using Microsoft.Extensions.Options;

namespace Cachemix.Core;

/// <summary>
/// Publishes cache telemetry through <c>System.Diagnostics.Metrics</c> so the
/// same hit/miss/eviction signals can flow to OpenTelemetry, Prometheus, or
/// Application Insights. The meter name is <see cref="CachemixConstants.MeterName"/>.
/// </summary>
internal sealed class CacheMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _sets;
    private readonly Counter<long> _evictions;
    private readonly bool _enabled;

    /// <summary>Creates the meter and its instruments.</summary>
    /// <param name="options">Cachemix options.</param>
    public CacheMetrics(IOptions<CachemixOptions> options)
    {
        _enabled = options.Value.MetricsEnabled;
        _meter = new Meter(CachemixConstants.MeterName);
        _hits = _meter.CreateCounter<long>("cachemix.cache.hits", "{hit}", "Cache hits observed by Cachemix.");
        _misses = _meter.CreateCounter<long>("cachemix.cache.misses", "{miss}", "Cache misses observed by Cachemix.");
        _sets = _meter.CreateCounter<long>("cachemix.cache.sets", "{set}", "Cache entries created or replaced.");
        _evictions = _meter.CreateCounter<long>("cachemix.cache.evictions", "{eviction}", "Cache entries evicted.");
    }

    /// <summary>Records a signal to the meter, if metrics are enabled.</summary>
    /// <param name="signal">The signal to record.</param>
    public void Record(in CacheSignal signal)
    {
        if (!_enabled)
        {
            return;
        }

        var tag = new KeyValuePair<string, object?>("cache.name", signal.CacheName);
        switch (signal.Kind)
        {
            case CacheEventKind.Hit:
                _hits.Add(1L, tag);
                break;
            case CacheEventKind.Miss:
                _misses.Add(1L, tag);
                break;
            case CacheEventKind.Set:
                _sets.Add(1L, tag);
                break;
            case CacheEventKind.Eviction:
                _evictions.Add(1L, tag);
                break;
            default:
                break;
        }
    }

    /// <summary>Disposes the meter.</summary>
    public void Dispose() => _meter.Dispose();
}
