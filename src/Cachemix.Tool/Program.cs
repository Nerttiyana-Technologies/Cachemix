using Cachemix.Tool;

// dotnet-cachemix - a terminal client for inspecting and managing cache keys on
// a Redis-compatible server (Redis, Garnet, Valkey). Exit codes:
//   0  success
//   1  runtime failure (unreachable server, missing key, action declined)
//   2  usage error (unknown command, missing argument)

try
{
    CliArgs cli = CliArgs.Parse(args);

    if (cli.HasFlag("no-color"))
    {
        Output.DisableColor();
    }

    if (cli.WantsVersion)
    {
        Output.WriteVersion();
        return 0;
    }

    if (cli.Command is null || cli.Command == "help" || cli.WantsHelp)
    {
        Output.WriteHelp();

        // No command at all is a usage error; an explicit help request is not.
        return cli.Command is null && !cli.WantsHelp ? 2 : 0;
    }

    return cli.Command switch
    {
        "keys" => await Commands.KeysAsync(cli).ConfigureAwait(false),
        "get" or "inspect" => await Commands.GetAsync(cli).ConfigureAwait(false),
        "nuke" or "evict" or "del" => await Commands.NukeAsync(cli).ConfigureAwait(false),
        "info" or "stats" => await Commands.InfoAsync(cli).ConfigureAwait(false),
        "ping" => await Commands.PingAsync(cli).ConfigureAwait(false),
        "diff" => await SnapshotDiff.RunAsync(cli).ConfigureAwait(false),
        _ => UnknownCommand(cli.Command),
    };
}
catch (CliUsageException ex)
{
    Output.Error(ex.Message);
    return 2;
}
catch (CliException ex)
{
    Output.Error(ex.Message);
    return 1;
}
catch (StackExchange.Redis.RedisException ex)
{
    Output.Error(ex.Message);
    return 1;
}
catch (Exception ex)
{
    // Last-resort guard so the tool fails with a message, not a stack trace.
    Output.Error("Unexpected failure: " + ex.Message);
    return 1;
}

static int UnknownCommand(string command)
{
    Output.Error($"Unknown command '{command}'. Run 'cachemix --help' for usage.");
    return 2;
}
