namespace Cachemix.Abstractions;

/// <summary>How a cache value should be presented in the dashboard.</summary>
public enum RedactionDisposition
{
    /// <summary>The value may be shown in full.</summary>
    Allow,

    /// <summary>The value must be hidden; only its type and size may be shown.</summary>
    Redact,
}

/// <summary>The outcome of a redaction decision for a single cache value.</summary>
public readonly record struct RedactionResult
{
    private RedactionResult(RedactionDisposition disposition, string? placeholder)
    {
        Disposition = disposition;
        Placeholder = placeholder;
    }

    /// <summary>Whether the value may be shown.</summary>
    public RedactionDisposition Disposition { get; }

    /// <summary>The text to display in place of a redacted value, when redacted.</summary>
    public string? Placeholder { get; }

    /// <summary>Creates a result that allows the value to be shown in full.</summary>
    /// <returns>An allowing <see cref="RedactionResult"/>.</returns>
    public static RedactionResult Allow() => new(RedactionDisposition.Allow, placeholder: null);

    /// <summary>Creates a result that hides the value behind a placeholder.</summary>
    /// <param name="placeholder">The text to display in place of the value.</param>
    /// <returns>A redacting <see cref="RedactionResult"/>.</returns>
    public static RedactionResult Redact(string placeholder = "[redacted]")
        => new(RedactionDisposition.Redact, placeholder);
}
