using Cachemix.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cachemix.Hybrid;

/// <summary>Extends <see cref="ICachemixBuilder"/> with HybridCache observation.</summary>
public static class CachemixHybridBuilderExtensions
{
    /// <summary>
    /// Decorates the registered <c>HybridCache</c> so Cachemix observes it, and
    /// registers the commander and tag invalidator that back the dashboard's
    /// evict and tag-invalidation actions. Call this after <c>AddCachemix()</c>
    /// and after <c>AddHybridCache()</c>.
    /// </summary>
    /// <param name="builder">The Cachemix builder returned by <c>AddCachemix()</c>.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ICachemixBuilder AddHybridCacheTelemetry(this ICachemixBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        HybridCacheDecoration.Decorate(builder.Services);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheCommander, HybridCacheCommander>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICacheTagInvalidator, HybridCacheTagInvalidator>());

        return builder;
    }
}
