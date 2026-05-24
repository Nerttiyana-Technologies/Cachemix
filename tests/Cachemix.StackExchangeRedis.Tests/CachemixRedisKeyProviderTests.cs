using System.Text;
using Cachemix.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Cachemix.StackExchangeRedis.Tests;

/// <summary>
/// Integration tests for <see cref="CachemixRedisKeyProvider"/> that run against
/// a real Redis-compatible server (Redis or Valkey — the provider speaks the same
/// RESP protocol to both). They verify the provider enumerates the keyspace with
/// <c>SCAN</c> and reconstructs entry metadata from the Microsoft distributed-cache
/// hash format (the <c>absexp</c> / <c>sldexp</c> / <c>data</c> fields).
/// <para>
/// Every test writes its keys under a unique prefix and deletes them afterwards,
/// and never issues <c>FLUSHDB</c> — so the suite is safe to run against a shared
/// development server. The server is supplied by <see cref="RedisCompatibleServerFixture"/>;
/// when none is available the tests are skipped by <see cref="ServerFactAttribute"/>.
/// </para>
/// </summary>
[Collection(ServerCollectionDefinition.Name)]
public sealed class CachemixRedisKeyProviderTests(RedisCompatibleServerFixture fixture)
{
    private readonly RedisCompatibleServerFixture _fixture = fixture;

    [ServerFact]
    public async Task EnumerateAsync_WithNoMatchingKeys_ReturnsNoEntries()
    {
        string prefix = NewPrefix();
        using ConnectionMultiplexer multiplexer = await ConnectAsync();
        try
        {
            var options = new CachemixRedisOptions { KeyPrefix = prefix };
            using var provider = new CachemixRedisKeyProvider(options, multiplexer);

            Assert.Empty(await provider.EnumerateAsync());
        }
        finally
        {
            await DeleteByPrefixAsync(multiplexer, prefix);
        }
    }

    [ServerFact]
    public async Task EnumerateAsync_ReturnsEntriesStoredInMicrosoftHashFormat()
    {
        string prefix = NewPrefix();
        using ConnectionMultiplexer multiplexer = await ConnectAsync();
        try
        {
            IDatabase db = multiplexer.GetDatabase();
            var expectedSizes = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["user:1"] = await WriteHashEntryAsync(db, prefix + "user:1", Payload(16)),
                ["user:2"] = await WriteHashEntryAsync(db, prefix + "user:2", Payload(64)),
                ["session:abc"] = await WriteHashEntryAsync(db, prefix + "session:abc", Payload(256)),
            };

            var options = new CachemixRedisOptions { KeyPrefix = prefix };
            using var provider = new CachemixRedisKeyProvider(options, multiplexer);
            IReadOnlyList<CacheEntrySnapshot> entries = await provider.EnumerateAsync();

            // Ordered sequence equality covers both the entry count and the key set.
            Assert.Equal(
                expectedSizes.Keys.OrderBy(k => k, StringComparer.Ordinal),
                entries.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal));

            foreach (CacheEntrySnapshot entry in entries)
            {
                // A Microsoft distributed-cache hash always holds an opaque byte payload.
                Assert.Equal("System.Byte[]", entry.ValueTypeName);
                Assert.Equal(CacheKind.Distributed, entry.CacheKind);
                Assert.Equal(expectedSizes[entry.Key], entry.EstimatedSizeBytes);
            }
        }
        finally
        {
            await DeleteByPrefixAsync(multiplexer, prefix);
        }
    }

    [ServerFact]
    public async Task EnumerateAsync_ParsesAbsoluteExpirationFromHashField()
    {
        string prefix = NewPrefix();
        using ConnectionMultiplexer multiplexer = await ConnectAsync();
        try
        {
            var absoluteExpiration = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
            await WriteHashEntryAsync(
                multiplexer.GetDatabase(), prefix + "report:q1", Payload(32), absoluteExpiration.UtcTicks);

            var options = new CachemixRedisOptions { KeyPrefix = prefix };
            using var provider = new CachemixRedisKeyProvider(options, multiplexer);
            CacheEntrySnapshot entry = Assert.Single(await provider.EnumerateAsync());

            // The expiration came straight from the hash, so it is not inferred.
            Assert.False(entry.ExpirationInferred);
            Assert.Equal(absoluteExpiration, entry.AbsoluteExpirationUtc);
        }
        finally
        {
            await DeleteByPrefixAsync(multiplexer, prefix);
        }
    }

    [ServerFact]
    public async Task EnumerateAsync_InfersExpirationFromKeyTtl_WhenHashHasNoAbsExp()
    {
        string prefix = NewPrefix();
        using ConnectionMultiplexer multiplexer = await ConnectAsync();
        try
        {
            IDatabase db = multiplexer.GetDatabase();

            // No absexp field — only a payload and a server-level TTL.
            await WriteHashEntryAsync(db, prefix + "token:xyz", Payload(8));
            await db.KeyExpireAsync(prefix + "token:xyz", TimeSpan.FromMinutes(10));

            var options = new CachemixRedisOptions { KeyPrefix = prefix };
            using var provider = new CachemixRedisKeyProvider(options, multiplexer);
            CacheEntrySnapshot entry = Assert.Single(await provider.EnumerateAsync());

            // With no absexp, the provider falls back to the TTL and flags it as inferred.
            Assert.True(entry.ExpirationInferred);
            Assert.NotNull(entry.AbsoluteExpirationUtc);
            Assert.InRange(
                entry.AbsoluteExpirationUtc.Value,
                DateTimeOffset.UtcNow.AddMinutes(8),
                DateTimeOffset.UtcNow.AddMinutes(12));
        }
        finally
        {
            await DeleteByPrefixAsync(multiplexer, prefix);
        }
    }

    [ServerFact]
    public async Task EnumerateAsync_ScopesScanToKeyPrefix_AndStripsItFromKeyNames()
    {
        string prefix = NewPrefix();
        string otherPrefix = NewPrefix();
        using ConnectionMultiplexer multiplexer = await ConnectAsync();
        try
        {
            IDatabase db = multiplexer.GetDatabase();
            await WriteHashEntryAsync(db, prefix + "user:1", Payload(16));
            await WriteHashEntryAsync(db, prefix + "user:2", Payload(16));
            await WriteHashEntryAsync(db, otherPrefix + "user:3", Payload(16));

            var options = new CachemixRedisOptions { KeyPrefix = prefix };
            using var provider = new CachemixRedisKeyProvider(options, multiplexer);
            IReadOnlyList<CacheEntrySnapshot> entries = await provider.EnumerateAsync();

            // Only the configured prefix is scanned, and it is stripped for display.
            string[] expectedKeys = ["user:1", "user:2"];
            Assert.Equal(
                expectedKeys,
                entries.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal));
        }
        finally
        {
            await DeleteByPrefixAsync(multiplexer, prefix);
            await DeleteByPrefixAsync(multiplexer, otherPrefix);
        }
    }

    [ServerFact]
    public async Task EnumerateAsync_ReportsConfiguredCacheName()
    {
        string prefix = NewPrefix();
        using ConnectionMultiplexer multiplexer = await ConnectAsync();
        try
        {
            IDatabase db = multiplexer.GetDatabase();
            await WriteHashEntryAsync(db, prefix + "k1", Payload(16));
            await WriteHashEntryAsync(db, prefix + "k2", Payload(16));

            var options = new CachemixRedisOptions { KeyPrefix = prefix, CacheName = "OrdersRedis" };
            using var provider = new CachemixRedisKeyProvider(options, multiplexer);
            IReadOnlyList<CacheEntrySnapshot> entries = await provider.EnumerateAsync();

            Assert.Equal("OrdersRedis", provider.CacheName);
            Assert.NotEmpty(entries);
            Assert.All(entries, e => Assert.Equal("OrdersRedis", e.CacheName));
        }
        finally
        {
            await DeleteByPrefixAsync(multiplexer, prefix);
        }
    }

    /// <summary>A fresh, collision-proof key prefix that isolates one test's keys.</summary>
    private static string NewPrefix() => "cachemix-it:" + Guid.NewGuid().ToString("N") + ":";

    private static byte[] Payload(int size) => Encoding.UTF8.GetBytes(new string('x', size));

    /// <summary>Writes a key in the Microsoft distributed-cache hash layout and returns the payload size.</summary>
    private static async Task<long> WriteHashEntryAsync(
        IDatabase db,
        string key,
        byte[] data,
        long? absExpTicks = null)
    {
        var fields = new List<HashEntry>(2) { new("data", data) };
        if (absExpTicks is { } abs)
        {
            fields.Add(new HashEntry("absexp", abs));
        }

        await db.HashSetAsync(key, fields.ToArray());
        return data.Length;
    }

    private async Task<ConnectionMultiplexer> ConnectAsync()
    {
        string? connectionString = _fixture.ConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("No Redis-compatible server is available.");
        }

        return await ConnectionMultiplexer.ConnectAsync(connectionString);
    }

    /// <summary>Removes every key written under <paramref name="prefix"/> — the test's own keys only.</summary>
    private static async Task DeleteByPrefixAsync(ConnectionMultiplexer multiplexer, string prefix)
    {
        IDatabase db = multiplexer.GetDatabase();
        IServer server = multiplexer.GetServer(multiplexer.GetEndPoints()[0]);
        await foreach (RedisKey key in server.KeysAsync(pattern: prefix + "*"))
        {
            await db.KeyDeleteAsync(key);
        }
    }
}
