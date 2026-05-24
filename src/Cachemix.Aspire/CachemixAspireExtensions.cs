using Cachemix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Cachemix.Aspire;

/// <summary>
/// Wires Cachemix telemetry into a .NET Aspire application's OpenTelemetry
/// pipeline so the cache hit, miss, set and eviction metrics Cachemix publishes
/// (the <c>cachemix.cache.*</c> instruments on the <c>Cachemix</c> meter) appear
/// in the Aspire dashboard's metrics view.
/// </summary>
public static class CachemixAspireExtensions
{
    /// <summary>
    /// Adds the Cachemix meter to a <see cref="MeterProviderBuilder"/>. Use this
    /// overload when configuring an OpenTelemetry metrics pipeline directly.
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static MeterProviderBuilder AddCachemixInstrumentation(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddMeter(CachemixConstants.MeterName);
    }

    /// <summary>
    /// Registers the Cachemix meter with the application's OpenTelemetry metrics
    /// pipeline. Call this from an Aspire service's <c>ServiceDefaults</c>
    /// project, next to the standard <c>AddServiceDefaults()</c> call, so
    /// Cachemix cache metrics flow to the Aspire dashboard.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHostApplicationBuilder AddCachemixInstrumentation(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(static metrics => metrics.AddMeter(CachemixConstants.MeterName));

        return builder;
    }
}
