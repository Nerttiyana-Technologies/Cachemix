using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cachemix.Core;

/// <summary>
/// Rewrites the <c>IMemoryCache</c> and <c>IDistributedCache</c> service
/// descriptors so the registered caches are wrapped by Cachemix decorators.
/// Decoration is idempotent: a marker service prevents double-wrapping.
/// </summary>
internal static class CacheServiceDecoration
{
    /// <summary>Decorates the registered <c>IMemoryCache</c>, if any.</summary>
    /// <param name="services">The service collection.</param>
    public static void DecorateMemoryCache(IServiceCollection services)
    {
        if (HasMarker(services, typeof(MemoryCacheDecorationMarker)))
        {
            return;
        }

        for (int i = services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = services[i];
            if (descriptor.IsKeyedService || descriptor.ServiceType != typeof(IMemoryCache))
            {
                continue;
            }

            services[i] = Decorate(descriptor, static (inner, sp) => new CachemixMemoryCache(
                (IMemoryCache)inner,
                sp.GetRequiredService<CacheEventPipeline>(),
                sp.GetRequiredService<IOptions<CachemixOptions>>(),
                sp.GetRequiredService<ILogger<CachemixMemoryCache>>()));

            services.AddSingleton(new MemoryCacheDecorationMarker());
            return;
        }
    }

    /// <summary>Decorates the registered <c>IDistributedCache</c>, if any.</summary>
    /// <param name="services">The service collection.</param>
    public static void DecorateDistributedCache(IServiceCollection services)
    {
        if (HasMarker(services, typeof(DistributedCacheDecorationMarker)))
        {
            return;
        }

        for (int i = services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = services[i];
            if (descriptor.IsKeyedService || descriptor.ServiceType != typeof(IDistributedCache))
            {
                continue;
            }

            services[i] = Decorate(descriptor, static (inner, sp) => new CachemixDistributedCache(
                (IDistributedCache)inner,
                sp.GetRequiredService<CacheEventPipeline>(),
                sp.GetRequiredService<IOptions<CachemixOptions>>(),
                sp.GetRequiredService<ILogger<CachemixDistributedCache>>()));

            services.AddSingleton(new DistributedCacheDecorationMarker());
            return;
        }
    }

    /// <summary>Tests whether a decoration marker service is already registered.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="markerType">The marker service type.</param>
    /// <returns><see langword="true"/> when the marker is present.</returns>
    internal static bool HasMarker(IServiceCollection services, Type markerType)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == markerType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds a descriptor that resolves the original service and wraps it.</summary>
    /// <param name="original">The descriptor being replaced.</param>
    /// <param name="decoratorFactory">Builds the decorator given the inner instance and the provider.</param>
    /// <returns>A replacement <see cref="ServiceDescriptor"/>.</returns>
    internal static ServiceDescriptor Decorate(
        ServiceDescriptor original,
        Func<object, IServiceProvider, object> decoratorFactory)
        => ServiceDescriptor.Describe(
            original.ServiceType,
            sp => decoratorFactory(CreateInner(original, sp), sp),
            original.Lifetime);

    private static object CreateInner(ServiceDescriptor original, IServiceProvider serviceProvider)
    {
        if (original.ImplementationInstance is not null)
        {
            return original.ImplementationInstance;
        }

        if (original.ImplementationFactory is not null)
        {
            return original.ImplementationFactory(serviceProvider);
        }

        if (original.ImplementationType is not null)
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, original.ImplementationType);
        }

        throw new InvalidOperationException(
            $"Cachemix cannot decorate service '{original.ServiceType}': its descriptor has no implementation.");
    }

    private sealed class MemoryCacheDecorationMarker
    {
    }

    private sealed class DistributedCacheDecorationMarker
    {
    }
}
