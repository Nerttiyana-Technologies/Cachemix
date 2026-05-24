namespace Cachemix.Core;

/// <summary>
/// A conservative, reflection-free estimator of cache value sizes. It returns
/// an estimate only for well-known value types and <see langword="null"/> for
/// anything else, which keeps <c>Cachemix.Core</c> trim- and AOT-safe. Richer
/// object-graph estimation is intentionally out of scope here.
/// </summary>
internal static class SizeEstimator
{
    private const long ObjectOverhead = 24L;

    /// <summary>Estimates the size of <paramref name="value"/> in bytes.</summary>
    /// <param name="value">The cached value.</param>
    /// <returns>An estimated byte size, or <see langword="null"/> when the type is not recognised.</returns>
    public static long? TryEstimate(object? value) => value switch
    {
        null => 0L,
        string s => ObjectOverhead + ((long)s.Length * 2L),
        byte[] b => ObjectOverhead + b.Length,
        bool => 1L,
        char => 2L,
        sbyte or byte => 1L,
        short or ushort => 2L,
        int or uint or float => 4L,
        long or ulong or double => 8L,
        decimal => 16L,
        Guid => 16L,
        DateTime or DateTimeOffset or TimeSpan => 16L,
        _ => null,
    };
}
