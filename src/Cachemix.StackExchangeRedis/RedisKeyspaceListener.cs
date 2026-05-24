using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Cachemix.StackExchangeRedis;

/// <summary>
/// Subscribes to Redis keyspace notifications so server-side expirations and
/// evictions — which the <c>IDistributedCache</c> decorator cannot observe —
/// reach the Cachemix event feed. Active only when
/// <see cref="RedisTelemetryOptions.SubscribeKeyspaceNotifications"/> is set and
/// the Redis server is configured with <c>notify-keyspace-events</c>.
/// </summary>
internal sealed class RedisKeyspaceListener : BackgroundService
{
    private readonly CachemixRedisKeyProvider _provider;
    private readonly CacheEventPipeline _pipeline;
    private readonly CachemixOptions _options;
    private readonly ILogger<RedisKeyspaceListener> _logger;

    public RedisKeyspaceListener(
        CachemixRedisKeyProvider provider,
        CacheEventPipeline pipeline,
        IOptions<CachemixOptions> options,
        ILogger<RedisKeyspaceListener> logger)
    {
        _provider = provider;
        _pipeline = pipeline;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Redis.SubscribeKeyspaceNotifications)
        {
            return;
        }

        try
        {
            IConnectionMultiplexer multiplexer =
                await _provider.GetMultiplexerAsync(stoppingToken).ConfigureAwait(false);
            ISubscriber subscriber = multiplexer.GetSubscriber();

            // Subscribe only to the events the decorator cannot see itself.
            // Explicit removes are already recorded by the decorator, so the
            // 'del' event is intentionally omitted to avoid double-counting.
            await subscriber.SubscribeAsync(
                RedisChannel.Pattern("__keyevent@*__:expired"),
                (_, key) => Publish(key, CacheEvictionReason.Expired)).ConfigureAwait(false);
            await subscriber.SubscribeAsync(
                RedisChannel.Pattern("__keyevent@*__:evicted"),
                (_, key) => Publish(key, CacheEvictionReason.Capacity)).ConfigureAwait(false);

            RedisKeyspaceLog.Subscribed(_logger);

            // The subscriptions deliver events through callbacks; stay alive
            // until the host shuts down.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown.
        }
        catch (Exception ex)
        {
            RedisKeyspaceLog.SubscribeFailed(_logger, ex);
        }
    }

    private void Publish(RedisValue key, CacheEvictionReason reason)
    {
        try
        {
            string? keyString = key.ToString();
            if (string.IsNullOrEmpty(keyString))
            {
                return;
            }

            string? prefix = _provider.Options.KeyPrefix;
            if (!string.IsNullOrEmpty(prefix) && !keyString.StartsWith(prefix, StringComparison.Ordinal))
            {
                // The event is for a key owned by a different application.
                return;
            }

            string keyText = string.IsNullOrEmpty(prefix)
                ? keyString
                : keyString[prefix.Length..];

            _pipeline.Publish(new CacheSignal(
                _provider.Options.CacheName,
                CacheKind.Distributed,
                CacheEventKind.Eviction,
                keyText,
                DateTimeOffset.UtcNow)
            {
                EvictionReason = reason,
            });
        }
        catch (Exception)
        {
            // Telemetry must never destabilize the Redis subscriber callback.
        }
    }
}

/// <summary>Source-generated log messages for the Redis keyspace listener.</summary>
internal static partial class RedisKeyspaceLog
{
    /// <summary>Logs that keyspace-notification subscriptions are active.</summary>
    /// <param name="logger">The logger.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Cachemix subscribed to Redis keyspace notifications (expired, evicted).")]
    public static partial void Subscribed(ILogger logger);

    /// <summary>Logs that the keyspace-notification subscription failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Cachemix could not subscribe to Redis keyspace notifications. Server-side evictions will not appear in the event feed.")]
    public static partial void SubscribeFailed(ILogger logger, Exception exception);
}
