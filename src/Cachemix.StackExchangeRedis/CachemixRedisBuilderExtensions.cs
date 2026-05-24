using Cachemix.Core;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Cachemix.StackExchangeRedis;

/// <summary>Extends <see cref="ICachemixBuilder"/> with Redis key enumeration.</summary>
public static class CachemixRedisBuilderExtensions
{
    /// <summary>
    /// Registers a Redis key-enumeration provider that opens its own connection
    /// from <paramref name="connectionString"/>. Use this when the application
    /// does not expose an <c>IConnectionMultiplexer</c> through dependency injection.
    /// </summary>
    /// <param name="builder">The Cachemix builder returned by <c>AddCachemix()</c>.</param>
    /// <param name="connectionString">The Redis connection string.</param>
    /// <param name="configure">An optional callback to configure the provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ICachemixBuilder AddRedisTelemetry(
        this ICachemixBuilder builder,
        string connectionString,
        Action<CachemixRedisOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new CachemixRedisOptions { ConnectionString = connectionString };
        configure?.Invoke(options);

        RegisterRedisTelemetry(builder.Services, _ => new CachemixRedisKeyProvider(options));
        return builder;
    }

    /// <summary>
    /// Registers a Redis key-enumeration provider over a connection resolved by
    /// <paramref name="connectionFactory"/> — typically
    /// <c>sp =&gt; sp.GetRequiredService&lt;IConnectionMultiplexer&gt;()</c>. The
    /// resolved connection is not disposed by Cachemix.
    /// </summary>
    /// <param name="builder">The Cachemix builder returned by <c>AddCachemix()</c>.</param>
    /// <param name="connectionFactory">Resolves the Redis connection to enumerate.</param>
    /// <param name="configure">An optional callback to configure the provider.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ICachemixBuilder AddRedisTelemetry(
        this ICachemixBuilder builder,
        Func<IServiceProvider, IConnectionMultiplexer> connectionFactory,
        Action<CachemixRedisOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        var options = new CachemixRedisOptions();
        configure?.Invoke(options);

        RegisterRedisTelemetry(builder.Services, sp => new CachemixRedisKeyProvider(options, connectionFactory(sp)));
        return builder;
    }

    private static void RegisterRedisTelemetry(
        IServiceCollection services,
        Func<IServiceProvider, CachemixRedisKeyProvider> providerFactory)
    {
        // The container creates and owns the provider (and disposes its connection).
        services.AddSingleton(providerFactory);
        services.AddSingleton<ICacheKeyProvider>(static sp => sp.GetRequiredService<CachemixRedisKeyProvider>());
        services.AddHostedService<RedisKeyspaceListener>();
    }
}
