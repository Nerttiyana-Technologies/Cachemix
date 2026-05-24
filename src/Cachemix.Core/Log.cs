using Microsoft.Extensions.Logging;

namespace Cachemix.Core;

/// <summary>
/// Source-generated, allocation-free log messages for <c>Cachemix.Core</c>.
/// Using the <c>LoggerMessage</c> pattern keeps logging efficient and satisfies
/// analyzer rule CA1848.
/// </summary>
internal static partial class Log
{
    /// <summary>Logs that the telemetry processor failed to apply one signal.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cachemix failed to apply a telemetry signal. Telemetry may be incomplete; the application is unaffected.")]
    public static partial void TelemetrySignalFailed(ILogger logger, Exception exception);

    /// <summary>Logs a recoverable telemetry error observed while instrumenting a cache.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="cacheName">The affected cache.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Cachemix telemetry error while observing cache '{CacheName}'. The cache itself is unaffected.")]
    public static partial void CacheTelemetryError(ILogger logger, Exception exception, string cacheName);

    /// <summary>Logs that telemetry has self-disabled for a cache after sustained failures.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="cacheName">The affected cache.</param>
    /// <param name="errorCount">The number of errors observed.</param>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cachemix telemetry disabled for cache '{CacheName}' after {ErrorCount} errors. The cache continues to operate normally.")]
    public static partial void CacheTelemetryDisabled(ILogger logger, string cacheName, int errorCount);
}
