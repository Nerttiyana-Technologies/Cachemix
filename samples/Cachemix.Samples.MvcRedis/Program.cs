using Cachemix.AspNetCore;
using Cachemix.Core;
using Cachemix.StackExchangeRedis;
using Microsoft.Extensions.Caching.Distributed;

const string RedisConnection = "localhost:6379";
const string KeyPrefix = "mvcredis:";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = RedisConnection;
    options.InstanceName = KeyPrefix;
});

// Cachemix is registered after the cache. AddRedisTelemetry enables full
// SCAN-based key enumeration; AddDashboard mounts the dashboard services.
builder.Services
    .AddCachemix()
    .AddRedisTelemetry(RedisConnection, options => options.KeyPrefix = KeyPrefix)
    .AddDashboard();
builder.Services.AddHostedService<CacheWorkload>();

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Redirect("/cachemix"));
app.MapControllers();
app.MapCachemix();
app.Run();

/// <summary>Exercises the distributed cache so the dashboard shows live data.</summary>
internal sealed class CacheWorkload(IDistributedCache cache) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string key = "product:" + Random.Shared.Next(1, 20);
                string? value = await cache.GetStringAsync(key, stoppingToken);
                if (value is null)
                {
                    await cache.SetStringAsync(
                        key,
                        "Product " + key,
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Random.Shared.Next(20, 60)),
                        },
                        stoppingToken);
                }

                if (Random.Shared.Next(15) == 0)
                {
                    await cache.RemoveAsync("product:" + Random.Shared.Next(1, 20), stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(900), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Redis may not be running yet — back off, then retry.
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
