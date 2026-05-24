using System.Net;
using Cachemix.Abstractions;
using Cachemix.Core;
using StackExchange.Redis;

namespace Cachemix.StackExchangeRedis;

/// <summary>
/// An <see cref="ICacheKeyProvider"/> that enumerates a Redis (or Garnet /
/// Valkey) keyspace with the cursor-based <c>SCAN</c> command and reconstructs
/// entry metadata from the Microsoft distributed-cache hash format.
/// </summary>
internal sealed class CachemixRedisKeyProvider : ICacheKeyProvider, IDisposable
{
    private static readonly RedisValue[] ExpirationFields = ["absexp", "sldexp"];
    private static readonly RedisValue DataField = "data";

    private readonly CachemixRedisOptions _options;
    private readonly bool _ownsMultiplexer;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    private IConnectionMultiplexer? _multiplexer;
    private bool _disposed;

    /// <summary>Creates a provider that lazily connects using <see cref="CachemixRedisOptions.ConnectionString"/>.</summary>
    /// <param name="options">The provider options.</param>
    public CachemixRedisKeyProvider(CachemixRedisOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsMultiplexer = true;
    }

    /// <summary>Creates a provider over an existing, externally-owned connection.</summary>
    /// <param name="options">The provider options.</param>
    /// <param name="multiplexer">An existing Redis connection. It is not disposed by this provider.</param>
    public CachemixRedisKeyProvider(CachemixRedisOptions options, IConnectionMultiplexer multiplexer)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _ownsMultiplexer = false;
    }

    /// <inheritdoc />
    public string CacheName => _options.CacheName;

    /// <inheritdoc />
    public CacheKind CacheKind => CacheKind.Distributed;

    /// <summary>The provider's options. Shared with the keyspace listener.</summary>
    internal CachemixRedisOptions Options => _options;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CacheEntrySnapshot>> EnumerateAsync(
        CancellationToken cancellationToken = default)
    {
        IConnectionMultiplexer multiplexer = await GetMultiplexerAsync(cancellationToken).ConfigureAwait(false);
        IDatabase database = multiplexer.GetDatabase(_options.Database);
        RedisValue pattern = (_options.KeyPrefix ?? string.Empty) + "*";

        var results = new List<CacheEntrySnapshot>();

        foreach (EndPoint endpoint in multiplexer.GetEndPoints())
        {
            cancellationToken.ThrowIfCancellationRequested();

            IServer server = multiplexer.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica)
            {
                continue;
            }

            await foreach (RedisKey key in server
                .KeysAsync(_options.Database, pattern, _options.ScanPageSize)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                CacheEntrySnapshot? snapshot = await BuildSnapshotAsync(database, key, cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot is not null)
                {
                    results.Add(snapshot);
                }
            }
        }

        return results;
    }

    private async ValueTask<CacheEntrySnapshot?> BuildSnapshotAsync(
        IDatabase database,
        RedisKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            RedisType type = await database.KeyTypeAsync(key).ConfigureAwait(false);
            TimeSpan? ttl = await database.KeyTimeToLiveAsync(key).ConfigureAwait(false);

            DateTimeOffset? absoluteExpiration = null;
            TimeSpan? slidingExpiration = null;
            long? sizeBytes = null;
            bool inferred = true;

            if (type == RedisType.Hash)
            {
                RedisValue[] fields = await database.HashGetAsync(key, ExpirationFields).ConfigureAwait(false);
                long dataLength = await database.HashStringLengthAsync(key, DataField).ConfigureAwait(false);

                if (fields.Length == 2)
                {
                    if (!fields[0].IsNull
                        && fields[0].TryParse(out long absoluteTicks)
                        && absoluteTicks > 0L
                        && absoluteTicks <= DateTime.MaxValue.Ticks)
                    {
                        absoluteExpiration = new DateTimeOffset(absoluteTicks, TimeSpan.Zero);
                        inferred = false;
                    }

                    if (!fields[1].IsNull
                        && fields[1].TryParse(out long slidingTicks)
                        && slidingTicks > 0L)
                    {
                        slidingExpiration = new TimeSpan(slidingTicks);
                        inferred = false;
                    }
                }

                if (dataLength > 0L)
                {
                    sizeBytes = dataLength;
                }
            }

            // When no absolute expiration was found, fall back to the Redis TTL.
            if (absoluteExpiration is null && ttl is { } remaining)
            {
                absoluteExpiration = DateTimeOffset.UtcNow.Add(remaining);
            }

            return new CacheEntrySnapshot
            {
                Key = StripPrefix(key.ToString()),
                CacheName = _options.CacheName,
                CacheKind = CacheKind.Distributed,
                ValueTypeName = type == RedisType.Hash ? "System.Byte[]" : type.ToString(),
                EstimatedSizeBytes = sizeBytes,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                AbsoluteExpirationUtc = absoluteExpiration,
                SlidingExpiration = slidingExpiration,
                ExpirationInferred = inferred,
            };
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A single problematic key must not abort the whole enumeration.
            return null;
        }
    }

    private string StripPrefix(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        string? prefix = _options.KeyPrefix;
        return !string.IsNullOrEmpty(prefix) && key.StartsWith(prefix, StringComparison.Ordinal)
            ? key[prefix.Length..]
            : key;
    }

    /// <summary>Returns the Redis connection, opening it on first use.</summary>
    /// <param name="cancellationToken">A token to cancel connecting.</param>
    /// <returns>The shared connection multiplexer.</returns>
    internal async ValueTask<IConnectionMultiplexer> GetMultiplexerAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_multiplexer is { } connected)
        {
            return connected;
        }

        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_multiplexer is { } existing)
            {
                return existing;
            }

            if (string.IsNullOrWhiteSpace(_options.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Cachemix Redis telemetry has no connection. Provide a connection string or an IConnectionMultiplexer via AddRedisTelemetry.");
            }

            _multiplexer = await ConnectionMultiplexer
                .ConnectAsync(_options.ConnectionString)
                .ConfigureAwait(false);
            return _multiplexer;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>Disposes the connection, but only if this provider created it.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsMultiplexer)
        {
            _multiplexer?.Dispose();
        }

        _connectGate.Dispose();
    }
}
