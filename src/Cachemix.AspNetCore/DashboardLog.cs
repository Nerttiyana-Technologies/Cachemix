using Microsoft.Extensions.Logging;

namespace Cachemix.AspNetCore;

/// <summary>Source-generated log messages for the Cachemix dashboard.</summary>
internal static partial class DashboardLog
{
    /// <summary>Logs that the dashboard was mapped.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="path">The path the dashboard was mapped at.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Cachemix dashboard mapped at '{Path}'.")]
    public static partial void Mapped(ILogger logger, string path);

    /// <summary>Logs that the dashboard was not mapped because of the environment policy.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="environment">The current hosting environment name.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Cachemix dashboard not mapped: the environment is '{Environment}' and EnableOutsideDevelopment is false.")]
    public static partial void NotMapped(ILogger logger, string environment);

    /// <summary>Logs that a snapshot broadcast failed.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="exception">The failure.</param>
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cachemix dashboard failed to broadcast a snapshot. Connected clients may briefly see stale data.")]
    public static partial void BroadcastFailed(ILogger logger, Exception exception);

    /// <summary>Logs a destructive eviction performed from the dashboard (an audit record).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="key">The evicted key.</param>
    /// <param name="cache">The cache the key was evicted from.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Cachemix dashboard evicted key '{Key}' from cache '{Cache}'.")]
    public static partial void KeyEvicted(ILogger logger, string key, string cache);

    /// <summary>Logs a destructive tag invalidation performed from the dashboard (an audit record).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="tag">The invalidated tag.</param>
    /// <param name="cache">The cache the tag was invalidated within.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Cachemix dashboard invalidated tag '{Tag}' in cache '{Cache}'.")]
    public static partial void TagInvalidated(ILogger logger, string tag, string cache);
}
