using Cachemix.AspNetCore;
using Cachemix.Core;
using Microsoft.Extensions.Caching.Memory;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

// Cachemix is registered after the cache, then the dashboard is added.
builder.Services.AddCachemix().AddDashboard();
builder.Services.AddHostedService<CacheWorkload>();

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Redirect("/cachemix"));

app.MapGet("/weather/{city}", (string city, IMemoryCache cache) =>
{
    string? forecast = cache.GetOrCreate("weather:" + city, entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
        return Random.Shared.Next(-5, 35) + " C";
    });
    return Results.Ok(new { city, forecast });
});

app.MapCachemix();
app.Run();

/// <summary>Exercises the memory cache continuously so the dashboard shows live data.</summary>
internal sealed class CacheWorkload(IMemoryCache cache) : BackgroundService
{
    private static readonly string[] Keys =
    [
        "weather:london", "weather:tokyo", "weather:cairo",
        "weather:lima", "weather:oslo", "weather:delhi",
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            string key = Keys[Random.Shared.Next(Keys.Length)];
            _ = cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Random.Shared.Next(10, 40));
                return Random.Shared.Next(-5, 35);
            });

            // Occasionally remove a key so removals appear in the event feed.
            if (Random.Shared.Next(12) == 0)
            {
                cache.Remove(Keys[Random.Shared.Next(Keys.Length)]);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
