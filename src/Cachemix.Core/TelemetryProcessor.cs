using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cachemix.Core;

/// <summary>
/// The background service that drains the telemetry channel and applies each
/// signal to the telemetry store and the metrics meter. A failure to apply one
/// signal is logged and skipped; it never stops the processor or the host.
/// </summary>
internal sealed class TelemetryProcessor : BackgroundService
{
    private readonly CacheEventPipeline _pipeline;
    private readonly CacheTelemetry _telemetry;
    private readonly CacheMetrics _metrics;
    private readonly ILogger<TelemetryProcessor> _logger;

    /// <summary>Creates the processor.</summary>
    /// <param name="pipeline">The telemetry channel to drain.</param>
    /// <param name="telemetry">The telemetry store to apply signals to.</param>
    /// <param name="metrics">The metrics meter to record signals to.</param>
    /// <param name="logger">The logger.</param>
    public TelemetryProcessor(
        CacheEventPipeline pipeline,
        CacheTelemetry telemetry,
        CacheMetrics metrics,
        ILogger<TelemetryProcessor> logger)
    {
        _pipeline = pipeline;
        _telemetry = telemetry;
        _metrics = metrics;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (CacheSignal signal in _pipeline.Reader
                .ReadAllAsync(stoppingToken)
                .ConfigureAwait(false))
            {
                try
                {
                    _telemetry.Apply(signal);
                    _metrics.Record(signal);
                }
                catch (Exception ex)
                {
                    Log.TelemetrySignalFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown.
        }
    }
}
