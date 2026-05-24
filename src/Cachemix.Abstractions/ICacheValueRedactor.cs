namespace Cachemix.Abstractions;

/// <summary>
/// Decides whether a cache value may be displayed in the dashboard. Implement
/// this extension point to mask sensitive entries beyond the built-in
/// key-pattern rules.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe and fast: the redactor is consulted every
/// time the dashboard renders an entry's value.
/// </remarks>
public interface ICacheValueRedactor
{
    /// <summary>
    /// Decides how the value stored under <paramref name="key"/> should be presented.
    /// </summary>
    /// <param name="key">The cache key, formatted as text.</param>
    /// <param name="cacheName">The logical name of the cache holding the entry.</param>
    /// <param name="valueTypeName">The CLR type name of the value, when known.</param>
    /// <returns>A <see cref="RedactionResult"/> describing whether the value may be shown.</returns>
    RedactionResult Redact(string key, string cacheName, string? valueTypeName);
}
