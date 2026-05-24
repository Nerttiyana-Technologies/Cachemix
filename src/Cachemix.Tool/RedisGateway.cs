using System.Net;
using StackExchange.Redis;

namespace Cachemix.Tool;

/// <summary>Aggregate metadata for a single cache key.</summary>
/// <param name="Key">The key.</param>
/// <param name="Type">The Redis data type of the key.</param>
/// <param name="Ttl">The remaining time to live, or <see langword="null"/> when the key never expires.</param>
/// <param name="SizeBytes">The estimated memory footprint, or <see langword="null"/> when unavailable.</param>
internal sealed record KeyInfo(RedisKey Key, RedisType Type, TimeSpan? Ttl, long? SizeBytes);

/// <summary>
/// A thin, command-oriented wrapper over a Redis connection. Resolves the
/// connection string, opens the multiplexer, and exposes just the operations
/// the CLI needs.
/// </summary>
internal sealed class RedisGateway : IDisposable
{
    private const int DeleteBatchSize = 512;

    private readonly ConnectionMultiplexer _multiplexer;
    private readonly IDatabase _database;
    private readonly IServer _server;

    private RedisGateway(ConnectionMultiplexer multiplexer, int databaseNumber)
    {
        _multiplexer = multiplexer;
        DatabaseNumber = databaseNumber;
        _database = multiplexer.GetDatabase(databaseNumber);
        _server = multiplexer.GetServer(multiplexer.GetEndPoints()[0]);
    }

    /// <summary>The database number this gateway operates on.</summary>
    public int DatabaseNumber { get; }

    /// <summary>The endpoint the gateway is connected to, as text.</summary>
    public string EndPoint => Describe(_multiplexer.GetEndPoints()[0]);

    /// <summary>Resolves the connection string and opens a connection.</summary>
    /// <param name="cli">The parsed command line.</param>
    /// <returns>A connected gateway.</returns>
    public static async Task<RedisGateway> ConnectAsync(CliArgs cli)
    {
        string connection = ResolveConnectionString(cli);

        ConfigurationOptions options;
        try
        {
            options = ConfigurationOptions.Parse(connection);
        }
        catch (Exception ex)
        {
            throw new CliException($"Invalid connection string '{connection}': {ex.Message}");
        }

        options.AllowAdmin = true;
        options.AbortOnConnectFail = true;
        options.ConnectRetry = 1;
        options.ConnectTimeout = Math.Max(options.ConnectTimeout, 5000);

        int databaseNumber = cli.GetInt("db") ?? options.DefaultDatabase ?? 0;

        try
        {
            ConnectionMultiplexer multiplexer =
                await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            return new RedisGateway(multiplexer, databaseNumber);
        }
        catch (RedisConnectionException ex)
        {
            throw new CliException(
                $"Could not connect to Redis at {connection}: {ex.Message}");
        }
    }

    /// <summary>Scans for keys matching a glob pattern, up to a limit.</summary>
    /// <param name="pattern">The Redis glob pattern.</param>
    /// <param name="limit">The maximum number of keys to return.</param>
    /// <returns>The matching keys.</returns>
    public async Task<List<RedisKey>> ScanKeysAsync(string pattern, int limit)
    {
        var keys = new List<RedisKey>();
        await foreach (RedisKey key in _server
            .KeysAsync(DatabaseNumber, pattern, pageSize: 250)
            .ConfigureAwait(false))
        {
            keys.Add(key);
            if (keys.Count >= limit)
            {
                break;
            }
        }

        return keys;
    }

    /// <summary>Reads the type, TTL and memory footprint of a key in one round of pipelined calls.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The aggregated key metadata.</returns>
    public async Task<KeyInfo> DescribeKeyAsync(RedisKey key)
    {
        Task<RedisType> typeTask = _database.KeyTypeAsync(key);
        Task<TimeSpan?> ttlTask = _database.KeyTimeToLiveAsync(key);
        Task<long?> sizeTask = MemoryUsageAsync(key);

        await Task.WhenAll(typeTask, ttlTask, sizeTask).ConfigureAwait(false);
        return new KeyInfo(key, typeTask.Result, ttlTask.Result, sizeTask.Result);
    }

    /// <summary>Returns whether a key exists.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the key exists.</returns>
    public Task<bool> KeyExistsAsync(RedisKey key) => _database.KeyExistsAsync(key);

    /// <summary>Reads a string value.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    public Task<RedisValue> GetStringAsync(RedisKey key) => _database.StringGetAsync(key);

    /// <summary>Reads all fields of a hash.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The hash entries.</returns>
    public Task<HashEntry[]> GetHashAsync(RedisKey key) => _database.HashGetAllAsync(key);

    /// <summary>Reads all elements of a list.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The list elements.</returns>
    public Task<RedisValue[]> GetListAsync(RedisKey key) => _database.ListRangeAsync(key);

    /// <summary>Reads all members of a set.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The set members.</returns>
    public Task<RedisValue[]> GetSetAsync(RedisKey key) => _database.SetMembersAsync(key);

    /// <summary>Reads all members of a sorted set, with scores.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The sorted-set entries.</returns>
    public Task<SortedSetEntry[]> GetSortedSetAsync(RedisKey key)
        => _database.SortedSetRangeByRankWithScoresAsync(key);

    /// <summary>Deletes a single key.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when a key was removed.</returns>
    public Task<bool> DeleteAsync(RedisKey key) => _database.KeyDeleteAsync(key);

    /// <summary>Deletes many keys, in batches.</summary>
    /// <param name="keys">The keys to delete.</param>
    /// <returns>The number of keys removed.</returns>
    public async Task<long> DeleteManyAsync(IReadOnlyList<RedisKey> keys)
    {
        long deleted = 0;
        for (int offset = 0; offset < keys.Count; offset += DeleteBatchSize)
        {
            RedisKey[] batch = keys
                .Skip(offset)
                .Take(DeleteBatchSize)
                .ToArray();
            deleted += await _database.KeyDeleteAsync(batch).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>Measures the round-trip latency to the server.</summary>
    /// <returns>The ping latency.</returns>
    public Task<TimeSpan> PingAsync() => _database.PingAsync();

    /// <summary>Reads and parses the server's <c>INFO</c> output.</summary>
    /// <returns>The parsed server info.</returns>
    public async Task<RedisServerInfo> ServerInfoAsync()
    {
        IGrouping<string, KeyValuePair<string, string>>[] groups =
            await _server.InfoAsync().ConfigureAwait(false);
        return RedisServerInfo.From(groups);
    }

    /// <inheritdoc />
    public void Dispose() => _multiplexer.Dispose();

    private async Task<long?> MemoryUsageAsync(RedisKey key)
    {
        try
        {
            RedisResult result = await _database
                .ExecuteAsync("MEMORY", "USAGE", key.ToString())
                .ConfigureAwait(false);
            return result.IsNull ? null : (long?)result;
        }
        catch (RedisServerException)
        {
            // MEMORY USAGE is unavailable on some servers and older versions.
            return null;
        }
    }

    private static string ResolveConnectionString(CliArgs cli)
        => cli.GetOption("connection", "c")
           ?? Environment.GetEnvironmentVariable("CACHEMIX_REDIS")
           ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION")
           ?? "localhost:6379";

    private static string Describe(EndPoint endPoint) => endPoint switch
    {
        DnsEndPoint dns => $"{dns.Host}:{dns.Port}",
        IPEndPoint ip => $"{ip.Address}:{ip.Port}",
        _ => endPoint.ToString() ?? "(unknown)",
    };
}

/// <summary>Parsed <c>INFO</c> output from a Redis-compatible server.</summary>
internal sealed class RedisServerInfo
{
    private readonly Dictionary<string, string> _fields;

    private RedisServerInfo(
        Dictionary<string, string> fields,
        IReadOnlyList<(string Database, string Raw)> keyspace)
    {
        _fields = fields;
        Keyspace = keyspace;
    }

    /// <summary>The per-database keyspace lines (database name and raw stats).</summary>
    public IReadOnlyList<(string Database, string Raw)> Keyspace { get; }

    /// <summary>Builds a <see cref="RedisServerInfo"/> from grouped INFO output.</summary>
    /// <param name="groups">The INFO sections.</param>
    /// <returns>The parsed server info.</returns>
    public static RedisServerInfo From(IEnumerable<IGrouping<string, KeyValuePair<string, string>>> groups)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var keyspace = new List<(string, string)>();

        foreach (IGrouping<string, KeyValuePair<string, string>> group in groups)
        {
            bool isKeyspace = string.Equals(group.Key, "Keyspace", StringComparison.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> entry in group)
            {
                if (isKeyspace)
                {
                    keyspace.Add((entry.Key, entry.Value));
                }
                else
                {
                    fields[entry.Key] = entry.Value;
                }
            }
        }

        return new RedisServerInfo(fields, keyspace);
    }

    /// <summary>Returns a string field, or <see langword="null"/> when absent.</summary>
    /// <param name="field">The field name.</param>
    /// <returns>The field value.</returns>
    public string? Get(string field) => _fields.TryGetValue(field, out string? value) ? value : null;

    /// <summary>Returns an integer field, or <see langword="null"/> when absent or unparsable.</summary>
    /// <param name="field">The field name.</param>
    /// <returns>The field value.</returns>
    public long? GetLong(string field)
        => _fields.TryGetValue(field, out string? value) && long.TryParse(value, out long parsed)
            ? parsed
            : null;
}
