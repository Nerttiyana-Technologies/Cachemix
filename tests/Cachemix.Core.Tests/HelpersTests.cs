using Cachemix.Abstractions;
using Cachemix.Core;
using Xunit;

namespace Cachemix.Core.Tests;

public sealed class CacheKeyFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsPlaceholder()
        => Assert.Equal("(null)", CacheKeyFormatter.Format(null));

    [Fact]
    public void Format_String_ReturnsSameValue()
        => Assert.Equal("user:1", CacheKeyFormatter.Format("user:1"));

    [Fact]
    public void Format_Number_ReturnsText()
        => Assert.Equal("42", CacheKeyFormatter.Format(42));
}

public sealed class SizeEstimatorTests
{
    [Fact]
    public void TryEstimate_Null_ReturnsZero()
        => Assert.Equal(0L, SizeEstimator.TryEstimate(null));

    [Fact]
    public void TryEstimate_String_CountsCharacters()
        => Assert.Equal(24L + (3L * 2L), SizeEstimator.TryEstimate("abc"));

    [Fact]
    public void TryEstimate_Int_ReturnsFourBytes()
        => Assert.Equal(4L, SizeEstimator.TryEstimate(7));

    [Fact]
    public void TryEstimate_UnknownType_ReturnsNull()
        => Assert.Null(SizeEstimator.TryEstimate(new object()));
}

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("usertoken", "*token*", true)]
    [InlineData("USERTOKEN", "*token*", true)]
    [InlineData("username", "*token*", false)]
    [InlineData("abc", "a?c", true)]
    [InlineData("abc", "a?d", false)]
    [InlineData("anything", "*", true)]
    public void IsMatch_MatchesAsExpected(string input, string pattern, bool expected)
        => Assert.Equal(expected, GlobMatcher.IsMatch(input, pattern));
}

public sealed class EventRingBufferTests
{
    [Fact]
    public void Snapshot_ReturnsNewestFirst()
    {
        var buffer = new EventRingBuffer(10);
        for (int i = 0; i < 3; i++)
        {
            buffer.Add(new CacheEvent("c", CacheKind.Memory, CacheEventKind.Hit, "k" + i, DateTimeOffset.UtcNow));
        }

        IReadOnlyList<CacheEvent> snapshot = buffer.Snapshot(10);

        Assert.Equal(3, snapshot.Count);
        Assert.Equal("k2", snapshot[0].Key);
        Assert.Equal("k0", snapshot[2].Key);
    }

    [Fact]
    public void Add_BeyondCapacity_KeepsOnlyNewest()
    {
        var buffer = new EventRingBuffer(2);
        for (int i = 0; i < 5; i++)
        {
            buffer.Add(new CacheEvent("c", CacheKind.Memory, CacheEventKind.Hit, "k" + i, DateTimeOffset.UtcNow));
        }

        IReadOnlyList<CacheEvent> snapshot = buffer.Snapshot(10);

        Assert.Equal(2, snapshot.Count);
        Assert.Equal("k4", snapshot[0].Key);
        Assert.Equal("k3", snapshot[1].Key);
    }
}

public sealed class CacheHealthEvaluatorTests
{
    [Fact]
    public void Evaluate_NoActivity_GradesAWithInfoFinding()
    {
        CacheHealthReport report = CacheHealthEvaluator.Evaluate([]);

        Assert.Equal("A", report.Grade);
        Assert.Contains(report.Findings, f => f.Severity == HealthSeverity.Info);
    }

    [Fact]
    public void Evaluate_LowHitRatio_LowersScoreAndFlagsCritical()
    {
        CacheStatisticsSnapshot[] stats =
        [
            new CacheStatisticsSnapshot
            {
                CacheName = "c",
                CacheKind = CacheKind.Memory,
                TotalHits = 10,
                TotalMisses = 90,
            },
        ];

        CacheHealthReport report = CacheHealthEvaluator.Evaluate(stats);

        Assert.True(report.Score < 90);
        Assert.Contains(report.Findings, f => f.Severity == HealthSeverity.Critical);
    }
}
