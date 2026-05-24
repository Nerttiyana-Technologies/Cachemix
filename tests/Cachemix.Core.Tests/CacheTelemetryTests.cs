using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cachemix.Core.Tests;

public sealed class CacheTelemetryTests
{
    private const string CacheName = "MemoryCache";

    private static CacheTelemetry NewTelemetry()
        => new(Options.Create(new CachemixOptions()));

    [Fact]
    public void Apply_Set_TracksTheEntry()
    {
        CacheTelemetry telemetry = NewTelemetry();

        telemetry.Apply(new CacheSignal(CacheName, CacheKind.Memory, CacheEventKind.Set, "k1", DateTimeOffset.UtcNow)
        {
            Descriptor = new CacheEntryDescriptor("k1", CacheName, CacheKind.Memory) { ValueTypeName = "System.Int32" },
        });

        IReadOnlyList<CacheEntrySnapshot> entries = telemetry.GetEntries();
        Assert.Single(entries);
        Assert.Equal("k1", entries[0].Key);
    }

    [Fact]
    public void Apply_HitsAndMisses_UpdateStatistics()
    {
        CacheTelemetry telemetry = NewTelemetry();

        ApplyLookup(telemetry, CacheEventKind.Miss, "k1");
        ApplyLookup(telemetry, CacheEventKind.Hit, "k1");
        ApplyLookup(telemetry, CacheEventKind.Hit, "k1");

        CacheStatisticsSnapshot stats = Assert.Single(telemetry.GetStatistics());
        Assert.Equal(2L, stats.TotalHits);
        Assert.Equal(1L, stats.TotalMisses);
    }

    [Fact]
    public void Apply_Eviction_RemovesEntryAndCountsIt()
    {
        CacheTelemetry telemetry = NewTelemetry();

        telemetry.Apply(new CacheSignal(CacheName, CacheKind.Memory, CacheEventKind.Set, "k1", DateTimeOffset.UtcNow)
        {
            Descriptor = new CacheEntryDescriptor("k1", CacheName, CacheKind.Memory),
        });
        telemetry.Apply(new CacheSignal(CacheName, CacheKind.Memory, CacheEventKind.Eviction, "k1", DateTimeOffset.UtcNow)
        {
            EvictionReason = CacheEvictionReason.Capacity,
        });

        Assert.Empty(telemetry.GetEntries());
        CacheStatisticsSnapshot stats = Assert.Single(telemetry.GetStatistics());
        Assert.Equal(1L, stats.TotalEvictions);
    }

    [Fact]
    public void GetRecentEvents_ReturnsAppliedEvents()
    {
        CacheTelemetry telemetry = NewTelemetry();

        ApplyLookup(telemetry, CacheEventKind.Miss, "k1");

        Assert.Contains(telemetry.GetRecentEvents(10), e => e.Kind == CacheEventKind.Miss && e.Key == "k1");
    }

    private static void ApplyLookup(CacheTelemetry telemetry, CacheEventKind kind, string key)
        => telemetry.Apply(new CacheSignal(CacheName, CacheKind.Memory, kind, key, DateTimeOffset.UtcNow));
}
