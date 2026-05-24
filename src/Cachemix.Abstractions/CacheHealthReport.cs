namespace Cachemix.Abstractions;

/// <summary>The severity of a cache health finding.</summary>
public enum HealthSeverity
{
    /// <summary>Informational; no action needed.</summary>
    Info,

    /// <summary>A potential inefficiency worth reviewing.</summary>
    Warning,

    /// <summary>A significant problem that likely needs action.</summary>
    Critical,
}

/// <summary>A single actionable observation about cache health.</summary>
public sealed record CacheHealthFinding
{
    /// <summary>The severity of the finding.</summary>
    public required HealthSeverity Severity { get; init; }

    /// <summary>A short, human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>A fuller description, including a suggested action.</summary>
    public required string Detail { get; init; }
}

/// <summary>
/// An overall assessment of cache health: a letter grade, a numeric score, and
/// the actionable findings that produced them.
/// </summary>
public sealed record CacheHealthReport
{
    /// <summary>A letter grade from "A" (excellent) to "F" (poor).</summary>
    public required string Grade { get; init; }

    /// <summary>A numeric score from 0 to 100.</summary>
    public required int Score { get; init; }

    /// <summary>The findings that contributed to the score.</summary>
    public IReadOnlyList<CacheHealthFinding> Findings { get; init; } = [];

    /// <summary>When the report was generated, in UTC.</summary>
    public DateTimeOffset GeneratedAtUtc { get; init; }
}
