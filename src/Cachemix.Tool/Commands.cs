using System.Globalization;
using StackExchange.Redis;

namespace Cachemix.Tool;

/// <summary>The implementations behind each CLI command.</summary>
internal static class Commands
{
    private const int MaxItems = 50;
    private const int MaxKeyWidth = 60;

    private static readonly string[] KeysHeader = ["KEY", "TYPE", "TTL", "SIZE"];

    /// <summary>Lists cache keys matching a glob pattern.</summary>
    /// <param name="cli">The parsed command line.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> KeysAsync(CliArgs cli)
    {
        using RedisGateway redis = await RedisGateway.ConnectAsync(cli).ConfigureAwait(false);

        string pattern = cli.Positionals.Count > 0 ? cli.Positionals[0] : "*";
        int limit = Math.Max(1, cli.GetInt("limit") ?? 100);
        int fetch = limit == int.MaxValue ? limit : limit + 1;

        List<RedisKey> keys = await redis.ScanKeysAsync(pattern, fetch).ConfigureAwait(false);
        bool truncated = keys.Count > limit;
        if (truncated)
        {
            keys.RemoveRange(limit, keys.Count - limit);
        }

        if (keys.Count == 0)
        {
            Output.Plain($"No keys match \"{pattern}\" in database {redis.DatabaseNumber}.");
            return 0;
        }

        KeyInfo[] infos = await Task.WhenAll(keys.Select(redis.DescribeKeyAsync)).ConfigureAwait(false);

        var rows = new List<string[]>(infos.Length);
        foreach (KeyInfo info in infos)
        {
            rows.Add(
            [
                Output.Truncate(info.Key.ToString() ?? string.Empty, MaxKeyWidth),
                info.Type.ToString(),
                Output.FormatTtl(info.Ttl),
                Output.FormatBytes(info.SizeBytes),
            ]);
        }

        Output.Table(KeysHeader, rows);
        Output.Plain(string.Empty);

        string summary = $"{infos.Length} key(s) in database {redis.DatabaseNumber}";
        Output.Plain(truncated ? $"{summary} (limited to {limit}; raise with --limit)." : $"{summary}.");
        return 0;
    }

    /// <summary>Shows a single key's value, type and TTL.</summary>
    /// <param name="cli">The parsed command line.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> GetAsync(CliArgs cli)
    {
        string key = cli.RequirePositional("<key>");
        using RedisGateway redis = await RedisGateway.ConnectAsync(cli).ConfigureAwait(false);

        KeyInfo info = await redis.DescribeKeyAsync(key).ConfigureAwait(false);
        if (info.Type == RedisType.None)
        {
            Output.Error($"Key '{key}' was not found in database {redis.DatabaseNumber}.");
            return 1;
        }

        Output.Heading("Key");
        Output.Info("Name", key);
        Output.Info("Type", info.Type.ToString());
        Output.Info("TTL", Output.FormatTtl(info.Ttl));
        Output.Info("Size", Output.FormatBytes(info.SizeBytes));
        Output.Plain(string.Empty);

        switch (info.Type)
        {
            case RedisType.String:
                RedisValue value = await redis.GetStringAsync(key).ConfigureAwait(false);
                Output.Heading("Value");
                Output.WritePayload((byte[]?)value);
                break;

            case RedisType.Hash:
                HashEntry[] hash = await redis.GetHashAsync(key).ConfigureAwait(false);
                if (!TryRenderDistributedCacheEntry(hash))
                {
                    Output.Heading($"Hash ({hash.Length} field(s))");
                    RenderPairs(hash.Select(e =>
                        (e.Name.ToString() ?? string.Empty, (byte[]?)e.Value)));
                }

                break;

            case RedisType.List:
                RedisValue[] list = await redis.GetListAsync(key).ConfigureAwait(false);
                Output.Heading($"List ({list.Length} element(s))");
                RenderValues(list);
                break;

            case RedisType.Set:
                RedisValue[] set = await redis.GetSetAsync(key).ConfigureAwait(false);
                Output.Heading($"Set ({set.Length} member(s))");
                RenderValues(set);
                break;

            case RedisType.SortedSet:
                SortedSetEntry[] sorted = await redis.GetSortedSetAsync(key).ConfigureAwait(false);
                Output.Heading($"Sorted set ({sorted.Length} member(s))");
                RenderPairs(sorted.Select(e =>
                    (e.Score.ToString(CultureInfo.InvariantCulture), (byte[]?)e.Element)));
                break;

            default:
                Output.Plain($"(no preview available for type '{info.Type}')");
                break;
        }

        return 0;
    }

    /// <summary>Deletes a key, or every key matching a glob.</summary>
    /// <param name="cli">The parsed command line.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> NukeAsync(CliArgs cli)
    {
        using RedisGateway redis = await RedisGateway.ConnectAsync(cli).ConfigureAwait(false);

        string? match = cli.GetOption("match");
        if (match is not null)
        {
            return await NukeByMatchAsync(cli, redis, match).ConfigureAwait(false);
        }

        string key = cli.RequirePositional("<key> or --match <glob>");
        if (!await redis.KeyExistsAsync(key).ConfigureAwait(false))
        {
            Output.Error($"Key '{key}' was not found in database {redis.DatabaseNumber}.");
            return 1;
        }

        if (!Confirm(cli, $"Delete key '{key}' from database {redis.DatabaseNumber}?"))
        {
            Output.Plain("Aborted.");
            return 1;
        }

        bool deleted = await redis.DeleteAsync(key).ConfigureAwait(false);
        Output.Plain(deleted ? $"Deleted '{key}'." : $"Key '{key}' was already gone.");
        return 0;
    }

    /// <summary>Shows Redis server insights: memory, keyspace and hit ratio.</summary>
    /// <param name="cli">The parsed command line.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> InfoAsync(CliArgs cli)
    {
        using RedisGateway redis = await RedisGateway.ConnectAsync(cli).ConfigureAwait(false);
        RedisServerInfo info = await redis.ServerInfoAsync().ConfigureAwait(false);

        Output.Heading("Server");
        Output.Info("Endpoint", redis.EndPoint);
        Output.Info("Redis version", info.Get("redis_version") ?? "-");
        Output.Info("Mode", info.Get("redis_mode") ?? "-");
        Output.Info("Uptime", FormatUptime(info.GetLong("uptime_in_seconds")));
        Output.Plain(string.Empty);

        Output.Heading("Memory");
        Output.Info("Used", info.Get("used_memory_human") ?? "-");
        Output.Info("Peak", info.Get("used_memory_peak_human") ?? "-");
        long? maxMemory = info.GetLong("maxmemory");
        Output.Info("Max", maxMemory is > 0 ? Output.FormatBytes(maxMemory) : "unbounded");
        string? policy = info.Get("maxmemory_policy");
        Output.Info("Eviction policy", policy ?? "-");
        Output.Plain(string.Empty);

        Output.Heading("Stats");
        long hits = info.GetLong("keyspace_hits") ?? 0;
        long misses = info.GetLong("keyspace_misses") ?? 0;
        long lookups = hits + misses;
        double ratio = lookups > 0 ? (double)hits / lookups : 0d;
        string ratioText = lookups > 0
            ? ratio.ToString("P1", CultureInfo.InvariantCulture)
            : "-";
        Output.Info("Keyspace hits", hits.ToString("N0", CultureInfo.InvariantCulture));
        Output.Info("Keyspace misses", misses.ToString("N0", CultureInfo.InvariantCulture));
        Output.Info("Hit ratio", ratioText);
        Output.Info("Evicted keys",
            (info.GetLong("evicted_keys") ?? 0).ToString("N0", CultureInfo.InvariantCulture));
        Output.Info("Expired keys",
            (info.GetLong("expired_keys") ?? 0).ToString("N0", CultureInfo.InvariantCulture));
        Output.Info("Connected clients", info.Get("connected_clients") ?? "-");
        Output.Plain(string.Empty);

        Output.Heading("Keyspace");
        if (info.Keyspace.Count == 0)
        {
            Output.Plain("  (no databases populated)");
        }
        else
        {
            foreach ((string database, string raw) in info.Keyspace)
            {
                Output.Info(database, FormatKeyspace(raw));
            }
        }

        if (lookups > 1000 && ratio < 0.80d)
        {
            Output.Plain(string.Empty);
            Output.Warn($"Hit ratio is {ratioText} - a large share of lookups are missing the cache.");
        }

        if (string.Equals(policy, "noeviction", StringComparison.OrdinalIgnoreCase) && maxMemory is > 0)
        {
            Output.Warn("Eviction policy is 'noeviction' - writes will fail once maxmemory is reached.");
        }

        return 0;
    }

    /// <summary>Checks connectivity to the server.</summary>
    /// <param name="cli">The parsed command line.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> PingAsync(CliArgs cli)
    {
        using RedisGateway redis = await RedisGateway.ConnectAsync(cli).ConfigureAwait(false);
        TimeSpan latency = await redis.PingAsync().ConfigureAwait(false);
        string milliseconds = latency.TotalMilliseconds.ToString("0.00", CultureInfo.InvariantCulture);
        Output.Plain($"PONG from {redis.EndPoint} ({milliseconds} ms)");
        return 0;
    }

    private static async Task<int> NukeByMatchAsync(CliArgs cli, RedisGateway redis, string match)
    {
        List<RedisKey> keys = await redis.ScanKeysAsync(match, int.MaxValue).ConfigureAwait(false);
        if (keys.Count == 0)
        {
            Output.Plain($"No keys match \"{match}\" in database {redis.DatabaseNumber}.");
            return 0;
        }

        Output.Plain($"{keys.Count} key(s) match \"{match}\" in database {redis.DatabaseNumber}.");
        if (!Confirm(cli, $"Delete all {keys.Count} matching key(s)?"))
        {
            Output.Plain("Aborted.");
            return 1;
        }

        long deleted = await redis.DeleteManyAsync(keys).ConfigureAwait(false);
        Output.Plain($"Deleted {deleted} key(s).");
        return 0;
    }

    /// <summary>
    /// Recognises and renders the Microsoft <c>IDistributedCache</c> hash shape
    /// (the <c>absexp</c> / <c>sldexp</c> / <c>data</c> fields).
    /// </summary>
    private static bool TryRenderDistributedCacheEntry(HashEntry[] hash)
    {
        RedisValue data = RedisValue.Null;
        RedisValue absoluteExpiration = RedisValue.Null;
        RedisValue slidingExpiration = RedisValue.Null;
        bool hasData = false;

        foreach (HashEntry entry in hash)
        {
            switch (entry.Name.ToString())
            {
                case "data":
                    data = entry.Value;
                    hasData = true;
                    break;
                case "absexp":
                    absoluteExpiration = entry.Value;
                    break;
                case "sldexp":
                    slidingExpiration = entry.Value;
                    break;
                default:
                    break;
            }
        }

        if (!hasData)
        {
            return false;
        }

        Output.Heading("Microsoft IDistributedCache entry");
        Output.Info(
            "Absolute expiry",
            absoluteExpiration.TryParse(out long absoluteTicks) && absoluteTicks != -1
                ? new DateTime(absoluteTicks, DateTimeKind.Utc).ToString("u", CultureInfo.InvariantCulture)
                : "none");
        Output.Info(
            "Sliding window",
            slidingExpiration.TryParse(out long slidingTicks) && slidingTicks != -1
                ? Output.FormatDuration(TimeSpan.FromTicks(slidingTicks))
                : "none");
        Output.Plain(string.Empty);

        byte[]? payload = (byte[]?)data;
        Output.Heading($"Payload ({Output.FormatBytes(payload?.Length)})");
        Output.WritePayload(payload);
        return true;
    }

    private static void RenderValues(RedisValue[] values)
    {
        foreach (RedisValue value in values.Take(MaxItems))
        {
            Output.Plain("  " + Output.PreviewValue((byte[]?)value));
        }

        if (values.Length > MaxItems)
        {
            Output.More(values.Length - MaxItems);
        }
    }

    private static void RenderPairs(IEnumerable<(string Name, byte[]? Value)> pairs)
    {
        List<(string Name, byte[]? Value)> buffer = pairs.ToList();
        foreach ((string name, byte[]? value) in buffer.Take(MaxItems))
        {
            Output.Field(name, Output.PreviewValue(value));
        }

        if (buffer.Count > MaxItems)
        {
            Output.More(buffer.Count - MaxItems);
        }
    }

    private static bool Confirm(CliArgs cli, string question)
    {
        if (cli.HasFlag("yes", "y"))
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            Output.Error("Refusing to continue without confirmation; re-run with --yes.");
            return false;
        }

        Console.Write(question + " [y/N] ");
        string? answer = Console.ReadLine();
        return answer is not null
            && (answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatUptime(long? seconds)
        => seconds is { } value ? Output.FormatDuration(TimeSpan.FromSeconds(value)) : "-";

    private static string FormatKeyspace(string raw)
    {
        long keys = 0;
        long expires = 0;
        foreach (string part in raw.Split(','))
        {
            int equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
            {
                continue;
            }

            string name = part[..equals];
            string text = part[(equals + 1)..];
            if (name == "keys" && long.TryParse(text, out long parsedKeys))
            {
                keys = parsedKeys;
            }
            else if (name == "expires" && long.TryParse(text, out long parsedExpires))
            {
                expires = parsedExpires;
            }
        }

        string keysText = keys.ToString("N0", CultureInfo.InvariantCulture);
        string expiresText = expires.ToString("N0", CultureInfo.InvariantCulture);
        return $"{keysText} keys ({expiresText} with TTL)";
    }
}
