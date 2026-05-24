using Cachemix.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cachemix.Core;

/// <summary>
/// An <see cref="IHealthCheck"/> that reports cache efficiency. It grades the
/// current <see cref="CacheHealthReport"/> into a health status: grade A or B is
/// healthy, C or D is degraded, and anything lower uses the registration's
/// configured failure status.
/// </summary>
/// <remarks>
/// Register this with the <c>AddCachemix</c> extension on <c>IHealthChecksBuilder</c>
/// (provided by the Cachemix.AspNetCore package), or directly with the standard
/// <c>AddCheck&lt;CachemixHealthCheck&gt;</c>.
/// </remarks>
public sealed class CachemixHealthCheck : IHealthCheck
{
    private readonly ICacheTelemetry _telemetry;

    /// <summary>Creates the health check.</summary>
    /// <param name="telemetry">The Cachemix telemetry store.</param>
    public CachemixHealthCheck(ICacheTelemetry telemetry)
        => _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        CacheHealthReport report = _telemetry.GetHealthReport();
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["grade"] = report.Grade,
            ["score"] = report.Score,
        };
        string description = $"Cache health {report.Grade} (score {report.Score}/100).";

        HealthCheckResult result = report.Grade switch
        {
            "A" or "B" => HealthCheckResult.Healthy(description, data),
            "C" or "D" => HealthCheckResult.Degraded(description, exception: null, data),
            _ => new HealthCheckResult(context.Registration.FailureStatus, description, exception: null, data),
        };

        return Task.FromResult(result);
    }
}
