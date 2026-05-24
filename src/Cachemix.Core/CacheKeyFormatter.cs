namespace Cachemix.Core;

/// <summary>Converts an arbitrary cache key object into a stable text form.</summary>
internal static class CacheKeyFormatter
{
    /// <summary>Formats <paramref name="key"/> as text.</summary>
    /// <param name="key">The cache key, which may be any object.</param>
    /// <returns>A non-null text representation of the key.</returns>
    public static string Format(object? key) => key switch
    {
        null => "(null)",
        string text => text,
        _ => key.ToString() ?? key.GetType().Name,
    };
}
