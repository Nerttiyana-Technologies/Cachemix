namespace Cachemix.Tool;

/// <summary>
/// A parsed command line: the command verb, its positional arguments, and its
/// options. Options are <c>--name value</c>, <c>--name=value</c> or short
/// <c>-c value</c>; a fixed set of names are treated as valueless flags.
/// </summary>
internal sealed class CliArgs
{
    // Tokens recognised as boolean flags; everything else consumes a value.
    private static readonly HashSet<string> BooleanFlags = new(StringComparer.Ordinal)
    {
        "yes", "y", "help", "h", "no-color", "version",
    };

    private readonly List<string> _positionals = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    private CliArgs()
    {
    }

    /// <summary>The command verb (the first non-option token), or <see langword="null"/>.</summary>
    public string? Command { get; private set; }

    /// <summary>The positional arguments that followed the command.</summary>
    public IReadOnlyList<string> Positionals => _positionals;

    /// <summary>Whether help was requested.</summary>
    public bool WantsHelp => HasFlag("help", "h");

    /// <summary>Whether the tool version was requested.</summary>
    public bool WantsVersion => HasFlag("version");

    /// <summary>Parses raw command-line tokens into a <see cref="CliArgs"/>.</summary>
    /// <param name="tokens">The process arguments (without the executable name).</param>
    /// <returns>The parsed command line.</returns>
    public static CliArgs Parse(string[] tokens)
    {
        var result = new CliArgs();

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i];

            if (token.Length > 1 && token[0] == '-')
            {
                string name = token.TrimStart('-');
                int equals = name.IndexOf('=', StringComparison.Ordinal);
                if (equals >= 0)
                {
                    result._options[name[..equals]] = name[(equals + 1)..];
                }
                else if (BooleanFlags.Contains(name))
                {
                    result._flags.Add(name);
                }
                else if (i + 1 < tokens.Length)
                {
                    // A value option consumes the following token.
                    result._options[name] = tokens[++i];
                }
                else
                {
                    result._options[name] = string.Empty;
                }

                continue;
            }

            if (result.Command is null)
            {
                result.Command = token;
            }
            else
            {
                result._positionals.Add(token);
            }
        }

        return result;
    }

    /// <summary>Returns whether any of the given flag names was supplied.</summary>
    /// <param name="names">The flag names to check.</param>
    /// <returns><see langword="true"/> if any flag was present.</returns>
    public bool HasFlag(params string[] names) => Array.Exists(names, _flags.Contains);

    /// <summary>Returns the value of the first matching option, or <see langword="null"/>.</summary>
    /// <param name="names">The option names to check, in priority order.</param>
    /// <returns>The option value, or <see langword="null"/> when none was supplied.</returns>
    public string? GetOption(params string[] names)
    {
        foreach (string name in names)
        {
            if (_options.TryGetValue(name, out string? value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Returns an integer option value, or <see langword="null"/> when absent or invalid.</summary>
    /// <param name="names">The option names to check.</param>
    /// <returns>The parsed integer, or <see langword="null"/>.</returns>
    public int? GetInt(params string[] names)
    {
        string? raw = GetOption(names);
        return raw is not null && int.TryParse(raw, out int parsed) ? parsed : null;
    }

    /// <summary>Returns the first positional argument, or throws a usage error.</summary>
    /// <param name="what">A description of the expected argument, for the error message.</param>
    /// <returns>The first positional argument.</returns>
    public string RequirePositional(string what)
        => _positionals.Count > 0
            ? _positionals[0]
            : throw new CliUsageException($"Missing required argument: {what}.");
}
