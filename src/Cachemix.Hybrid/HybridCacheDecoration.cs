using Cachemix.Core;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cachemix.Hybrid;

/// <summary>
/// Rewrites the registered <c>HybridCache</c> service descriptor so the cache is
/// wrapped by the Cachemix decorator. Idempotent via a marker service.
/// </summary>
internal static class HybridCacheDecoration
{
    /// <summary>Decorates the registered <see cref="HybridCache"/>, if any.</summary>
    /// <param name="services">The service collection.</param>
    public static void Decorate(IServiceCollection services)
    {
        if (CacheServiceDecoration.HasMarker(services, typeof(HybridCacheDecorationMarker)))
        {
            return;
        }

        for (int i = services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = services[i];
            if (descriptor.IsKeyedService || descriptor.ServiceType != typeof(HybridCache))
            {
                continue;
            }

            services[i] = CacheServiceDecoration.Decorate(descriptor, static (inner, sp) => new CachemixHybridCache(
                (HybridCache)inner,
                sp.GetRequiredService<CacheEventPipeline>(),
                sp.GetRequiredService<IOptions<CachemixOptions>>(),
                sp.GetRequiredService<ILogger<CachemixHybridCache>>()));

            services.AddSingleton(new HybridCacheDecorationMarker());
            return;
        }
    }

    private sealed class HybridCacheDecorationMarker
    {
    }
}
