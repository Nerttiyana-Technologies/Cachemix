using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Cachemix.Abstractions;
using Microsoft.Extensions.Options;

namespace Cachemix.Core;

/// <summary>
/// The central, in-memory telemetry store. The telemetry processor applies
/// signals to it; the dashboard and health checks query it.
/// </summary>
internal sealed class CacheTelemetry : ICacheTelemetry
{
    private readonly ConcurrentDictionary<string, CacheRegistry> _registries =
        new(StringComparer.Ordinal);

    private readonly EventRingBuffer _events;
    private readonly int _maxTrackedEntries;

    /// <summary>Creates the telemetry store.</summary>
    /// <param name="options">Cachemix options.</param>
    public CacheTelemetry(IOptions<CachemixOptions> options)
    {
        CachemixOptions value = options.Value;
        _events = new EventRingBuffer(value.EventBufferCapacity);
        _maxTrackedEntries = value.MaxTrackedEntries;
    }

    /// <summary>Applies a single signal drained from the telemetry channel.</summary>
    /// <param name="signal">The signal to apply.</param>
    public void Apply(in CacheSignal signal)
    {
        CacheRegistry registry = _registries.GetOrAdd(
            signal.CacheName,
            static (name, arg) => new CacheRegistry(name, arg.Kind, arg.Max),
            (Kind: signal.CacheKind, Max: _maxTrackedEntries));

        switch (signal.Kind)
        {
            case CacheEventKind.Hit:
                if (signal.Descriptor is not null)
                {
                    registry.ObserveIfAbsent(signal.Descriptor);
                }

                registry.ApplyHit(signal.Key);
                break;

            case CacheEventKind.Miss:
                registry.ApplyMiss(signal.Key);
                break;

            case CacheEventKind.Set:
                if (signal.Descriptor is not null)
                {
                    registry.ApplySet(signal.Descriptor);
                }

                break;

            case CacheEventKind.Remove:
                registry.ApplyRemoval(signal.Key, eviction: false);
                break;

            case CacheEventKind.Eviction:
                registry.ApplyRemoval(signal.Key, eviction: true);
                break;
        }

        _events.Add(signal.ToEvent());
    }

    /// <inheritdoc />
    public IReadOnlyList<CacheStatisticsSnapshot> GetStatistics()
    {
        var list = new List<CacheStatisticsSnapshot>(_registries.Count);
        foreach (CacheRegistry registry in _registries.Values)
        {
            list.Add(registry.SnapshotStatistics());
        }

        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<CacheEntrySnapshot> GetEntries(string? cacheName = null)
    {
        if (cacheName is not null)
        {
            return _registries.TryGetValue(cacheName, out CacheRegistry? registry)
                ? registry.SnapshotEntries()
                : [];
        }

        var all = new List<CacheEntrySnapshot>();
        foreach (CacheRegistry registry in _registries.Values)
        {
            all.AddRange(registry.SnapshotEntries());
        }

        return all;
    }

    /// <inheritdoc />
    public IReadOnlyList<CacheEvent> GetRecentEvents(int maxEvents = 200)
        => _events.Snapshot(maxEvents < 1 ? 1 : maxEvents);

    /// <inheritdoc />
    public CacheHealthReport GetHealthReport() => CacheHealthEvaluator.Evaluate(GetStatistics());

    /// <summary>Attempts to retrieve the registry for a named cache.</summary>
    /// <param name="cacheName">The cache name.</param>
    /// <param name="registry">The registry, when found.</param>
    /// <returns><see langword="true"/> when the cache is known.</returns>
    public bool TryGetRegistry(string cacheName, [MaybeNullWhen(false)] out CacheRegistry registry)
        => _registries.TryGetValue(cacheName, out registry);
}
