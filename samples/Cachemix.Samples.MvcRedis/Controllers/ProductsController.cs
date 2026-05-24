using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace Cachemix.Samples.MvcRedis.Controllers;

/// <summary>A controller that reads through an <see cref="IDistributedCache"/> backed by Redis.</summary>
[ApiController]
[Route("products")]
public sealed class ProductsController(IDistributedCache cache) : ControllerBase
{
    /// <summary>Returns a product, serving it from Redis when available.</summary>
    /// <param name="id">The product id.</param>
    /// <returns>The product and whether it came from the cache.</returns>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        string key = "product:" + id;

        string? cached = await cache.GetStringAsync(key, HttpContext.RequestAborted);
        if (cached is not null)
        {
            return Ok(new { id, name = cached, source = "cache" });
        }

        string name = "Product " + id;
        await cache.SetStringAsync(
            key,
            name,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
            },
            HttpContext.RequestAborted);

        return Ok(new { id, name, source = "database" });
    }
}
