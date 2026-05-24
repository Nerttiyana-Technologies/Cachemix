namespace Cachemix.Core;

/// <summary>
/// A small, allocation-free glob matcher. <c>*</c> matches any run of
/// characters and <c>?</c> matches a single character; matching is
/// case-insensitive. Used to apply redaction key patterns.
/// </summary>
internal static class GlobMatcher
{
    /// <summary>Tests whether <paramref name="input"/> matches the glob <paramref name="pattern"/>.</summary>
    /// <param name="input">The text to test.</param>
    /// <param name="pattern">The glob pattern.</param>
    /// <returns><see langword="true"/> when the input matches the pattern.</returns>
    public static bool IsMatch(string input, string pattern)
    {
        int s = 0;
        int p = 0;
        int star = -1;
        int mark = 0;

        while (s < input.Length)
        {
            if (p < pattern.Length &&
                (pattern[p] == '?' ||
                 char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(input[s])))
            {
                s++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = s;
            }
            else if (star >= 0)
            {
                p = star + 1;
                s = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}
