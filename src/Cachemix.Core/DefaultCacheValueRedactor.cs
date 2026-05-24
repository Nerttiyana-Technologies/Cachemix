using Cachemix.Abstractions;
using Microsoft.Extensions.Options;

namespace Cachemix.Core;

/// <summary>
/// The default <see cref="ICacheValueRedactor"/>. It redacts a value when its
/// key matches any of the configured <see cref="CachemixOptions.RedactionPatterns"/>.
/// Replace it in dependency injection to apply custom rules.
/// </summary>
internal sealed class DefaultCacheValueRedactor : ICacheValueRedactor
{
    private readonly string[] _patterns;

    /// <summary>Creates the redactor from configured options.</summary>
    /// <param name="options">Cachemix options.</param>
    public DefaultCacheValueRedactor(IOptions<CachemixOptions> options)
    {
        _patterns = [.. options.Value.RedactionPatterns];
    }

    /// <inheritdoc />
    public RedactionResult Redact(string key, string cacheName, string? valueTypeName)
    {
        foreach (string pattern in _patterns)
        {
            if (GlobMatcher.IsMatch(key, pattern))
            {
                return RedactionResult.Redact();
            }
        }

        return RedactionResult.Allow();
    }
}
