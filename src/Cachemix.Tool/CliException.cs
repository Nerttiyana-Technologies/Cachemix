namespace Cachemix.Tool;

/// <summary>
/// A runtime failure that should end the process cleanly with exit code 1 and a
/// one-line message — no stack trace. Thrown for expected conditions such as a
/// missing key or an unreachable server.
/// </summary>
internal class CliException : Exception
{
    /// <summary>Creates the exception with a user-facing message.</summary>
    /// <param name="message">The message shown to the user.</param>
    public CliException(string message) : base(message)
    {
    }
}

/// <summary>
/// A misuse of the command line — an unknown command or a missing required
/// argument. Ends the process with exit code 2.
/// </summary>
internal sealed class CliUsageException : CliException
{
    /// <summary>Creates the exception with a user-facing message.</summary>
    /// <param name="message">The message shown to the user.</param>
    public CliUsageException(string message) : base(message)
    {
    }
}
