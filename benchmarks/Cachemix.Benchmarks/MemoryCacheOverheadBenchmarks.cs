using BenchmarkDotNet.Attributes;
using Cachemix.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Cachemix.Benchmarks;

/// <summary>
/// Measures the per-operation overhead the <see cref="CachemixMemoryCache"/>
/// observation decorator adds over an undecorated <see cref="MemoryCache"/>.
/// Read the overhead by comparing each <c>Cachemix:</c> row against its
/// <c>MemoryCache:</c> counterpart for the same operation.
/// </summary>
[MemoryDiagnoser]
public class MemoryCacheOverheadBenchmarks
{
    private const string HitKey = "cachemix:benchmark:hit";
    private const string MissKey = "cachemix:benchmark:absent";
    private const string SetKey = "cachemix:benchmark:set";

    private MemoryCache _plain = null!;
    private CachemixMemoryCache _observed = null!;
    private CacheEventPipeline _pipeline = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plain = new MemoryCache(new MemoryCacheOptions());
        _plain.Set(HitKey, 42);

        // A generous channel keeps Publish on its fast path for most of a run;
        // the telemetry processor that would normally drain it is not present
        // in a micro-benchmark, so [IterationCleanup] empties it between rounds.
        _pipeline = new CacheEventPipeline(1 << 16);
        _observed = new CachemixMemoryCache(
            new MemoryCache(new MemoryCacheOptions()),
            _pipeline,
            Options.Create(new CachemixOptions()),
            NullLogger<CachemixMemoryCache>.Instance);
        _observed.Set(HitKey, 42);
    }

    [IterationCleanup]
    public void DrainPipeline()
    {
        while (_pipeline.Reader.TryRead(out _))
        {
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _plain.Dispose();
        _observed.Dispose();
    }

    [Benchmark(Description = "MemoryCache: get hit")]
    public object? PlainHit()
    {
        _plain.TryGetValue(HitKey, out object? value);
        return value;
    }

    [Benchmark(Description = "Cachemix: get hit")]
    public object? CachemixHit()
    {
        _observed.TryGetValue(HitKey, out object? value);
        return value;
    }

    [Benchmark(Description = "MemoryCache: get miss")]
    public bool PlainMiss() => _plain.TryGetValue(MissKey, out _);

    [Benchmark(Description = "Cachemix: get miss")]
    public bool CachemixMiss() => _observed.TryGetValue(MissKey, out _);

    [Benchmark(Description = "MemoryCache: set")]
    public void PlainSet() => _plain.Set(SetKey, 7);

    [Benchmark(Description = "Cachemix: set")]
    public void CachemixSet() => _observed.Set(SetKey, 7);
}
