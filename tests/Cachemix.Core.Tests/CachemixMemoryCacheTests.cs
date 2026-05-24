using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cachemix.Core.Tests;

public sealed class CachemixMemoryCacheTests
{
    private static (CachemixMemoryCache Cache, CacheEventPipeline Pipeline) NewCache()
    {
        var inner = new MemoryCache(new MemoryCacheOptions());
        var pipeline = new CacheEventPipeline(1024);
        var cache = new CachemixMemoryCache(
            inner,
            pipeline,
            Options.Create(new CachemixOptions()),
            NullLogger<CachemixMemoryCache>.Instance);
        return (cache, pipeline);
    }

    [Fact]
    public void CreateEntry_OnCommit_PublishesSetSignal()
    {
        (CachemixMemoryCache cache, CacheEventPipeline pipeline) = NewCache();

        using (ICacheEntry entry = cache.CreateEntry("k1"))
        {
            entry.Value = 123;
        }

        Assert.Contains(pipeline.DrainAll(), s => s.Kind == CacheEventKind.Set && s.Key == "k1");
    }

    [Fact]
    public void TryGetValue_PublishesHitAndMiss_AndReturnsCorrectResult()
    {
        (CachemixMemoryCache cache, CacheEventPipeline pipeline) = NewCache();
        using (ICacheEntry entry = cache.CreateEntry("k1"))
        {
            entry.Value = 1;
        }

        _ = pipeline.DrainAll();

        bool hit = cache.TryGetValue("k1", out object? hitValue);
        bool miss = cache.TryGetValue("absent", out _);

        Assert.True(hit);
        Assert.Equal(1, hitValue);
        Assert.False(miss);

        List<CacheSignal> signals = pipeline.DrainAll();
        Assert.Contains(signals, s => s.Kind == CacheEventKind.Hit && s.Key == "k1");
        Assert.Contains(signals, s => s.Kind == CacheEventKind.Miss && s.Key == "absent");
    }

    [Fact]
    public void TryGetValue_WhenTelemetryThrows_DoesNotThrow()
    {
        (CachemixMemoryCache cache, _) = NewCache();

        // A key whose ToString() throws drives the telemetry path into failure.
        // The cache lookup itself must still complete — this is the fail-open guarantee.
        Exception? error = Record.Exception(() => cache.TryGetValue(new ThrowingKey(), out _));

        Assert.Null(error);
    }

    [Fact]
    public async Task Remove_PublishesRemoveSignalWithReason()
    {
        (CachemixMemoryCache cache, CacheEventPipeline pipeline) = NewCache();
        using (ICacheEntry entry = cache.CreateEntry("k1"))
        {
            entry.Value = 1;
        }

        cache.Remove("k1");

        // The post-eviction callback runs on the thread pool, so poll for it.
        CacheSignal? removal = await WaitForKindAsync(pipeline, CacheEventKind.Remove, TimeSpan.FromSeconds(2));

        Assert.NotNull(removal);
        Assert.Equal("k1", removal.Value.Key);
        Assert.Equal(CacheEvictionReason.Removed, removal.Value.EvictionReason);
    }

    private static async Task<CacheSignal?> WaitForKindAsync(
        CacheEventPipeline pipeline,
        CacheEventKind kind,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            while (pipeline.Reader.TryRead(out CacheSignal signal))
            {
                if (signal.Kind == kind)
                {
                    return signal;
                }
            }

            await Task.Delay(25);
        }

        return null;
    }

    private sealed class ThrowingKey
    {
        public override string ToString() => throw new InvalidOperationException("Key formatting failed.");

        public override int GetHashCode() => 1;

        public override bool Equals(object? obj) => obj is ThrowingKey;
    }
}
