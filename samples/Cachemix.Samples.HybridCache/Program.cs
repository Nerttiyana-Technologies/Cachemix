using Cachemix.AspNetCore;
using Cachemix.Core;
using Cachemix.Hybrid;
using Microsoft.Extensions.Caching.Hybrid;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddHybridCache();

// Cachemix is registered after the cache. AddHybridCacheTelemetry decorates the
// HybridCache; AddDashboard mounts the dashboard services.
builder.Services
    .AddCachemix()
    .AddHybridCacheTelemetry()
    .AddDashboard();
builder.Services.AddHostedService<CacheWorkload>();

WebApplication app = builder.Build();

app.MapGet("/", () => Results.Redirect("/cachemix"));

app.MapGet("/product/{id:int}", async (int id, HybridCache cache, CancellationToken cancellationToken) =>
{
    string product = await cache.GetOrCreateAsync(
        $"product:{id}",
        async token =>
        {
            await Task.Delay(60, token);
            return $"Product {id}";
        },
        tags: ProductTags.For(id),
        cancellationToken: cancellationToken);
    return Results.Ok(new { id, product });
});

app.MapCachemix();
app.Run();

/// <summary>Derives the tag set for a product so the dashboard's tag explorer has variety.</summary>
internal static class ProductTags
{
    private static readonly string[] Categories = ["apparel", "books", "electronics", "home"];

    /// <summary>Builds the tags for the product with the given id.</summary>
    /// <param name="id">The product id.</param>
    /// <returns>A tag set: every product, plus its category and price tier.</returns>
    public static string[] For(int id)
    {
        int slot = Math.Abs(id) % Categories.Length;
        return
        [
            "products",
            $"category:{Categories[slot]}",
            id % 3 == 0 ? "tier:premium" : "tier:standard",
        ];
    }
}

/// <summary>Exercises the hybrid cache (with tags) so the dashboard shows live data.</summary>
internal sealed class CacheWorkload(HybridCache cache) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int id = Random.Shared.Next(1, 25);
                _ = await cache.GetOrCreateAsync(
                    $"product:{id}",
                    async token =>
                    {
                        await Task.Delay(Random.Shared.Next(20, 120), token);
                        return $"Product {id}";
                    },
                    tags: ProductTags.For(id),
                    cancellationToken: stoppingToken);

                // Occasionally invalidate one category so tag counts visibly shift.
                if (Random.Shared.Next(30) == 0)
                {
                    int category = Random.Shared.Next(1, 25);
                    await cache.RemoveByTagAsync(ProductTags.For(category)[1], stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(700), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
