using Cachemix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cachemix.Core;

/// <summary>
/// Dependency-injection extensions that register Cachemix and wire it into the
/// application's caches.
/// </summary>
public static class CachemixServiceCollectionExtensions
{
    /// <summary>
    /// Registers Cachemix and decorates the <c>IMemoryCache</c> and
    /// <c>IDistributedCache</c> registrations present at the time it is called.
    /// Call this <b>after</b> the cache registrations (<c>AddMemoryCache</c>,
    /// <c>AddStackExchangeRedisCache</c>, and so on).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback to configure <see cref="CachemixOptions"/>.</param>
    /// <returns>An <see cref="ICachemixBuilder"/> for further configuration.</returns>
    public static ICachemixBuilder AddCachemix(
        this IServiceCollection services,
        Action<CachemixOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!IsAlreadyAdded(services))
        {
            OptionsBuilder<CachemixOptions> optionsBuilder = services.AddOptions<CachemixOptions>();

            // Environment-aware defaults run before the user's callback, so the
            // user can still opt back in for non-Development environments.
            optionsBuilder.Configure<IHostEnvironment>(static (options, environment) =>
            {
                if (!environment.IsDevelopment())
                {
                    options.AllowDestructiveActions = false;
                    options.CaptureValues = ValueCaptureMode.Never;
                }
            });

            if (configure is not null)
            {
                services.Configure(configure);
            }

            services.TryAddSingleton(static sp => new CacheEventPipeline(
                sp.GetRequiredService<IOptions<CachemixOptions>>().Value.SignalChannelCapacity));
            services.TryAddSingleton<CacheTelemetry>();
            services.TryAddSingleton<ICacheTelemetry>(static sp => sp.GetRequiredService<CacheTelemetry>());
            services.TryAddSingleton<CacheMetrics>();
            services.TryAddSingleton<ICacheValueRedactor, DefaultCacheValueRedactor>();
            services.AddHostedService<TelemetryProcessor>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheCommander, MemoryCacheCommander>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheCommander, DistributedCacheCommander>());
        }
        else if (configure is not null)
        {
            services.Configure(configure);
        }

        // Decoration is safe to re-run: it catches caches registered between
        // two AddCachemix calls and never double-wraps an already-wrapped cache.
        CacheServiceDecoration.DecorateMemoryCache(services);
        CacheServiceDecoration.DecorateDistributedCache(services);

        return new CachemixBuilder(services);
    }

    private static bool IsAlreadyAdded(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(CacheTelemetry))
            {
                return true;
            }
        }

        return false;
    }
}
