using Cachemix.Abstractions;

namespace Cachemix.Core;

/// <summary>
/// Turns raw cache statistics into an actionable <see cref="CacheHealthReport"/>:
/// a letter grade, a numeric score, and concrete findings.
/// </summary>
public static class CacheHealthEvaluator
{
    /// <summary>Evaluates cache health across every supplied cache.</summary>
    /// <param name="statistics">Per-cache statistics to assess.</param>
    /// <returns>A <see cref="CacheHealthReport"/> summarising cache health.</returns>
    public static CacheHealthReport Evaluate(IReadOnlyList<CacheStatisticsSnapshot> statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        var findings = new List<CacheHealthFinding>();
        int score = 100;

        long totalHits = 0L;
        long totalMisses = 0L;
        long totalSets = 0L;
        long totalEvictions = 0L;
        long totalStampedes = 0L;
        foreach (CacheStatisticsSnapshot stat in statistics)
        {
            totalHits += stat.TotalHits;
            totalMisses += stat.TotalMisses;
            totalSets += stat.TotalSets;
            totalEvictions += stat.TotalEvictions;
            totalStampedes += stat.StampedeIncidents;
        }

        long lookups = totalHits + totalMisses;

        if (lookups == 0L)
        {
            findings.Add(new CacheHealthFinding
            {
                Severity = HealthSeverity.Info,
                Title = "No cache activity yet",
                Detail = "No cache lookups have been observed. Exercise the application to populate telemetry.",
            });
        }
        else
        {
            double hitRatio = (double)totalHits / lookups;
            if (hitRatio < 0.5d)
            {
                score -= 30;
                findings.Add(new CacheHealthFinding
                {
                    Severity = HealthSeverity.Critical,
                    Title = "Low hit ratio",
                    Detail = $"The cache hit ratio is {hitRatio:P0}. Most lookups miss the cache — review how keys are built and how long entries live.",
                });
            }
            else if (hitRatio < 0.8d)
            {
                score -= 12;
                findings.Add(new CacheHealthFinding
                {
                    Severity = HealthSeverity.Warning,
                    Title = "Moderate hit ratio",
                    Detail = $"The cache hit ratio is {hitRatio:P0}. There may be room to cache more aggressively or extend expirations.",
                });
            }
        }

        if (totalSets > 0L)
        {
            double churn = (double)totalEvictions / totalSets;
            if (churn > 0.8d)
            {
                score -= 20;
                findings.Add(new CacheHealthFinding
                {
                    Severity = HealthSeverity.Warning,
                    Title = "High eviction churn",
                    Detail = $"{churn:P0} of created entries have already been evicted — the cache may be undersized or its expirations too short.",
                });
            }
        }

        if (totalStampedes > 0L)
        {
            score -= 15;
            findings.Add(new CacheHealthFinding
            {
                Severity = HealthSeverity.Warning,
                Title = "Cache stampede detected",
                Detail = $"{totalStampedes} likely stampede(s) observed — the same key recomputed concurrently. Consider HybridCache, which coalesces concurrent factory calls.",
            });
        }

        if (findings.Count == 0)
        {
            findings.Add(new CacheHealthFinding
            {
                Severity = HealthSeverity.Info,
                Title = "Cache looks healthy",
                Detail = "No cache-efficiency problems were detected.",
            });
        }

        score = Math.Clamp(score, 0, 100);
        return new CacheHealthReport
        {
            Grade = ToGrade(score),
            Score = score,
            Findings = findings,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static string ToGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F",
    };
}
