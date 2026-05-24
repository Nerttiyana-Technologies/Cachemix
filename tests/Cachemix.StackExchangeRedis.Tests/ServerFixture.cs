using DotNet.Testcontainers.Builders;
using Testcontainers.Redis;
using Xunit;

namespace Cachemix.StackExchangeRedis.Tests;

/// <summary>
/// Supplies the connection string for the Redis-compatible server the
/// integration tests run against. It prefers a server already listening at
/// <see cref="TestServer.Address"/> — your local Redis or Valkey — and only
/// starts a throwaway Valkey container when no such server is found and Docker
/// is available.
/// </summary>
public sealed class RedisCompatibleServerFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    /// <summary>The connection string, or <see langword="null"/> when no server is available.</summary>
    public string? ConnectionString { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (TestServer.IsLocalServerReachable)
        {
            // Use the server the developer already has running. The tests are
            // non-destructive (every key is written under a unique prefix), so
            // this is safe to point at a real Redis or Valkey instance.
            ConnectionString = TestServer.Address;
            return;
        }

        if (!TestServer.IsDockerAvailable)
        {
            // Nothing to run against — ServerFactAttribute skips the tests.
            return;
        }

        _container = new RedisBuilder()
            .WithImage("valkey/valkey:8.0")
            // A log-based wait works for the Valkey image, which ships
            // `valkey-cli` rather than the `redis-cli` the module probes for.
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"))
            .Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>Binds the server fixture to the integration test collection.</summary>
[CollectionDefinition(Name)]
public sealed class ServerCollectionDefinition : ICollectionFixture<RedisCompatibleServerFixture>
{
    /// <summary>The integration test collection name.</summary>
    public const string Name = "redis-compatible-server";
}
