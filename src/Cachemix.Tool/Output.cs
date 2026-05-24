using System.Globalization;
using System.Reflection;
using System.Text;

namespace Cachemix.Tool;

/// <summary>
/// Console output helpers: optional ANSI coloring, aligned tables, and
/// human-friendly formatting of sizes, durations and cache payloads. All output
/// is ASCII so it renders correctly on every terminal.
/// </summary>
internal static class Output
{
    private const int PayloadTextCap = 4096;
    private const int PayloadHexCap = 256;
    private const int PreviewCap = 100;

    // The ASCII escape character (0x1B) that begins an ANSI control sequence.
    private static readonly string Escape = ((char)27).ToString();

    private static bool _color = DetectColor();

    /// <summary>Disables ANSI coloring (used by the <c>--no-color</c> flag).</summary>
    public static void DisableColor() => _color = false;

    /// <summary>Writes an error line to standard error.</summary>
    /// <param name="message">The message.</param>
    public static void Error(string message)
        => Console.Error.WriteLine(Paint("error: ", "31") + message);

    /// <summary>Writes a warning/advisory line.</summary>
    /// <param name="message">The message.</param>
    public static void Warn(string message)
        => Console.WriteLine(Paint("note: ", "33") + message);

    /// <summary>Writes a plain line.</summary>
    /// <param name="message">The message.</param>
    public static void Plain(string message) => Console.WriteLine(message);

    /// <summary>Writes a section heading.</summary>
    /// <param name="text">The heading text.</param>
    public static void Heading(string text) => Console.WriteLine(Paint(text, "1;36"));

    /// <summary>Writes an indented label/value pair under a heading.</summary>
    /// <param name="label">The label.</param>
    /// <param name="value">The value.</param>
    public static void Info(string label, string value)
        => Console.WriteLine("  " + label.PadRight(20) + value);

    /// <summary>Writes an indented field name and value (for hash fields, members).</summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The field value.</param>
    public static void Field(string name, string value)
        => Console.WriteLine("  " + Paint(name, "36") + "  " + value);

    /// <summary>Writes a trailing "N more" line.</summary>
    /// <param name="count">The number of omitted items.</param>
    public static void More(int count)
        => Console.WriteLine("  " + Paint($"... and {count} more", "2"));

    /// <summary>Renders an aligned text table with a colored header row.</summary>
    /// <param name="headers">The column headers.</param>
    /// <param name="rows">The data rows.</param>
    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        int columns = headers.Count;
        int[] width = new int[columns];
        for (int c = 0; c < columns; c++)
        {
            width[c] = headers[c].Length;
        }

        foreach (string[] row in rows)
        {
            for (int c = 0; c < columns && c < row.Length; c++)
            {
                width[c] = Math.Max(width[c], row[c].Length);
            }
        }

        var header = new StringBuilder();
        for (int c = 0; c < columns; c++)
        {
            header.Append(c == columns - 1 ? headers[c] : headers[c].PadRight(width[c]));
            if (c < columns - 1)
            {
                header.Append("  ");
            }
        }

        Console.WriteLine(Paint(header.ToString(), "1;36"));

        foreach (string[] row in rows)
        {
            var line = new StringBuilder();
            for (int c = 0; c < columns; c++)
            {
                string cell = c < row.Length ? row[c] : string.Empty;
                line.Append(c == columns - 1 ? cell : cell.PadRight(width[c]));
                if (c < columns - 1)
                {
                    line.Append("  ");
                }
            }

            Console.WriteLine(line.ToString());
        }
    }

    /// <summary>Writes a cache payload, decoding it as text or showing a hex preview.</summary>
    /// <param name="bytes">The payload bytes.</param>
    public static void WritePayload(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            Console.WriteLine("  (empty)");
            return;
        }

        if (LooksLikeText(bytes, out string text))
        {
            bool capped = text.Length > PayloadTextCap;
            string shown = capped ? text[..PayloadTextCap] : text;
            foreach (string line in shown.Split('\n'))
            {
                Console.WriteLine("  " + line.TrimEnd('\r'));
            }

            if (capped)
            {
                Console.WriteLine("  " + Paint("... (output clipped)", "2"));
            }

            return;
        }

        Console.WriteLine("  " + Paint($"binary, {bytes.Length} bytes", "2"));
        int preview = Math.Min(bytes.Length, PayloadHexCap);
        var hex = new StringBuilder();
        for (int i = 0; i < preview; i++)
        {
            hex.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            hex.Append((i + 1) % 24 == 0 ? '\n' : ' ');
        }

        foreach (string line in hex.ToString().Split('\n'))
        {
            if (line.Length > 0)
            {
                Console.WriteLine("  " + line.TrimEnd());
            }
        }

        if (bytes.Length > preview)
        {
            Console.WriteLine("  " + Paint("... (preview clipped)", "2"));
        }
    }

    /// <summary>Renders a value as a short single-line preview.</summary>
    /// <param name="bytes">The value bytes.</param>
    /// <returns>A single-line preview string.</returns>
    public static string PreviewValue(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return "(empty)";
        }

        if (!LooksLikeText(bytes, out string text))
        {
            return $"<binary, {bytes.Length} bytes>";
        }

        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return text.Length > PreviewCap ? text[..PreviewCap] + "..." : text;
    }

    /// <summary>Truncates a string to a maximum length with an ellipsis.</summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The maximum length.</param>
    /// <returns>The possibly-truncated text.</returns>
    public static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..Math.Max(0, max - 3)] + "...";

    /// <summary>Formats a byte count, or "-" when unknown.</summary>
    /// <param name="bytes">The byte count.</param>
    /// <returns>A human-readable size.</returns>
    public static string FormatBytes(long? bytes)
    {
        if (bytes is not { } n)
        {
            return "-";
        }

        if (n < 1024)
        {
            return n + " B";
        }

        if (n < 1024 * 1024)
        {
            return (n / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }

        if (n < 1024L * 1024 * 1024)
        {
            return (n / (1024d * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        return (n / (1024d * 1024 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>Formats a key time-to-live, or "no expiry" when there is none.</summary>
    /// <param name="ttl">The time to live.</param>
    /// <returns>A human-readable TTL.</returns>
    public static string FormatTtl(TimeSpan? ttl)
        => ttl is { } span ? FormatDuration(span) : "no expiry";

    /// <summary>Formats a duration compactly (for example "2h 10m").</summary>
    /// <param name="span">The duration.</param>
    /// <returns>A human-readable duration.</returns>
    public static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            return "expired";
        }

        if (span.TotalSeconds < 1)
        {
            return "<1s";
        }

        if (span.TotalSeconds < 60)
        {
            return $"{(int)span.TotalSeconds}s";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        }

        return $"{(int)span.TotalDays}d {span.Hours}h";
    }

    /// <summary>Prints the tool's usage help.</summary>
    public static void WriteHelp()
    {
        Console.WriteLine(
            """
            cachemix - inspect and manage cache keys on a Redis-compatible server.

            Usage:
              cachemix <command> [arguments] [options]

            Commands:
              keys [pattern]        List cache keys (default pattern *).
              get <key>             Show a key's value, type and TTL.
              nuke <key>            Delete a single key.
              nuke --match <glob>   Delete every key matching a glob.
              info                  Show server insights (memory, keyspace, hit ratio).
              ping                  Check connectivity to the server.
              diff <url> <url>...   Compare cache state across two or more running
                                    Cachemix dashboards.

            Options:
              -c, --connection <s>  Redis connection string. Defaults to the
                                    CACHEMIX_REDIS environment variable, then to
                                    localhost:6379.
                  --db <n>          Database number (default: 0).
                  --limit <n>       Max rows for `keys` (default: 100).
                  --match <glob>    Key glob for `nuke`.
              -y, --yes             Skip confirmation prompts.
                  --no-color        Disable colored output.
              -h, --help            Show this help.
                  --version         Show the tool version.

            Examples:
              cachemix keys "user:*" --limit 50
              cachemix get user:42 -c my-redis:6379
              cachemix nuke --match "session:*" --yes
              cachemix info
              cachemix diff http://app1:8080/cachemix http://app2:8080/cachemix
            """);
    }

    /// <summary>Prints the tool's version.</summary>
    public static void WriteVersion()
    {
        string version = typeof(Output).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            version = version[..plus];
        }

        Console.WriteLine("cachemix " + version);
    }

    private static bool DetectColor()
    {
        if (Environment.GetEnvironmentVariable("NO_COLOR") is { Length: > 0 })
        {
            return false;
        }

        return !Console.IsOutputRedirected;
    }

    private static string Paint(string text, string ansiCode)
        => _color ? $"{Escape}[{ansiCode}m{text}{Escape}[0m" : text;

    private static bool LooksLikeText(byte[] bytes, out string text)
    {
        if (bytes.Length == 0)
        {
            text = string.Empty;
            return true;
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            string decoded = strict.GetString(bytes);

            int control = 0;
            foreach (char c in decoded)
            {
                if (char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
                {
                    control++;
                }
            }

            if (control * 20 > decoded.Length)
            {
                text = string.Empty;
                return false;
            }

            text = decoded;
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }
}
