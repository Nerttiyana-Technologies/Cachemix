using Cachemix.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cachemix.AspNetCore;

/// <summary>Health-check registration extensions for Cachemix.</summary>
public static class CachemixHealthCheckBuilderExtensions
{
    /// <summary>
    /// Adds the Cachemix cache-health check. Register it after <c>AddCachemix()</c>:
    /// <c>services.AddHealthChecks().AddCachemix();</c>
    /// </summary>
    /// <param name="builder">The health-checks builder.</param>
    /// <param name="name">The name the check is registered under.</param>
    /// <param name="tags">Optional tags used to filter health-check execution.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IHealthChecksBuilder AddCachemix(
        this IHealthChecksBuilder builder,
        string name = "cachemix",
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(new HealthCheckRegistration(
            name,
            static sp => new CachemixHealthCheck(sp.GetRequiredService<ICacheTelemetry>()),
            failureStatus: null,
            tags));

        return builder;
    }
}
