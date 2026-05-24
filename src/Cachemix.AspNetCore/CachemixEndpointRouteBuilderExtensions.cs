using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cachemix.AspNetCore;

/// <summary>Maps the Cachemix dashboard into an application's endpoint routing.</summary>
public static class CachemixEndpointRouteBuilderExtensions
{
    private const string DashboardLoggerCategory = "Cachemix.Dashboard";
    private const string BasePlaceholder = "__cachemix_base__";
    private const string ActionHeader = "X-Cachemix-Action";

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Maps the Cachemix dashboard. The dashboard mounts only in the Development
    /// environment unless <see cref="CachemixOptions.EnableOutsideDevelopment"/>
    /// is set — in which case at least one <see cref="ICachemixAuthorizationFilter"/>
    /// must be registered, or this call throws. Call
    /// <c>AddCachemix().AddDashboard()</c> during service registration first.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder (typically the <c>WebApplication</c>).</param>
    /// <returns>A convention builder for the dashboard's endpoints.</returns>
    public static IEndpointConventionBuilder MapCachemix(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        IServiceProvider services = endpoints.ServiceProvider;
        CachemixOptions options = services.GetRequiredService<IOptions<CachemixOptions>>().Value;
        IHostEnvironment environment = services.GetRequiredService<IHostEnvironment>();
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(DashboardLoggerCategory);

        bool isDevelopment = environment.IsDevelopment();
        RouteGroupBuilder group = endpoints.MapGroup(options.DashboardPath);

        if (!isDevelopment && !options.EnableOutsideDevelopment)
        {
            // Safe by default: outside Development the dashboard is not exposed.
            DashboardLog.NotMapped(logger, environment.EnvironmentName);
            return group;
        }

        if (!isDevelopment && !services.GetServices<ICachemixAuthorizationFilter>().Any())
        {
            throw new InvalidOperationException(
                "The Cachemix dashboard cannot run outside the Development environment without an " +
                "authorization filter. Register an ICachemixAuthorizationFilter (for example with " +
                "AllowLocalRequestsOnly()), or leave CachemixOptions.EnableOutsideDevelopment set to false.");
        }

        group.AddEndpointFilter(GuardRequestAsync);

        // A single endpoint at the group root serves the shell. ASP.NET Core
        // routing treats the trailing slash as optional, so this one route
        // matches both "/cachemix" and "/cachemix/".
        group.MapGet("/", ServeIndexAsync);
        group.MapGet("/assets/{**assetPath}", ServeAssetAsync);
        group.MapGet("/api/snapshot", ServeSnapshot);
        group.MapGet("/api/value", ReadValueAsync);
        group.MapGet("/api/diff", DiffAsync);
        group.MapPost("/api/evict", EvictAsync);
        group.MapPost("/api/evict-tag", EvictTagAsync);
        group.MapHub<CachemixHub>("/hub");

        DashboardLog.Mapped(logger, options.DashboardPath);
        return group;
    }

    private static async ValueTask<object?> GuardRequestAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        ApplySecurityHeaders(http.Response);

        CachemixAuthorizer authorizer = http.RequestServices.GetRequiredService<CachemixAuthorizer>();
        if (!authorizer.IsAuthorized(http))
        {
            // Reply 404 rather than 403 so the dashboard's presence is not revealed.
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return Results.Empty;
        }

        return await next(context).ConfigureAwait(false);
    }

    private static async Task ServeIndexAsync(HttpContext context)
    {
        EmbeddedAsset? asset = EmbeddedAssets.Get("index.html");
        if (asset is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Inject the configured dashboard base path so every relative URL in the
        // page (assets, the API and the hub) resolves correctly.
        CachemixOptions options = context.RequestServices
            .GetRequiredService<IOptions<CachemixOptions>>().Value;
        string html = Encoding.UTF8.GetString(asset.Content)
            .Replace(BasePlaceholder, options.DashboardPath.TrimEnd('/'), StringComparison.Ordinal);

        context.Response.ContentType = asset.ContentType;
        await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
    }

    private static Task ServeAssetAsync(HttpContext context, string assetPath)
        => WriteAssetAsync(context, assetPath);

    private static IResult ServeSnapshot(DashboardSnapshotProvider snapshots)
        => Results.Json(snapshots.GetSnapshot(), SnapshotJsonOptions);

    private static async Task<IResult> DiffAsync(HttpContext context, InstanceDiffService diff)
    {
        InstanceDiffReport report = await diff.BuildAsync(context.RequestAborted).ConfigureAwait(false);
        return Results.Json(report, SnapshotJsonOptions);
    }

    private static async Task<IResult> ReadValueAsync(HttpContext context, string? cache, string? key)
    {
        if (string.IsNullOrWhiteSpace(cache) || string.IsNullOrWhiteSpace(key))
        {
            return Results.BadRequest();
        }

        IServiceProvider services = context.RequestServices;
        CachemixOptions options = services.GetRequiredService<IOptions<CachemixOptions>>().Value;

        // Honour the capture policy: when values are never captured the
        // inspector must not read from the live cache at all.
        if (options.CaptureValues == ValueCaptureMode.Never)
        {
            return Results.Json(CacheValueResult.Disabled, SnapshotJsonOptions);
        }

        // Apply the redaction policy by key before any value is materialized,
        // so a sensitive value is never read into memory in the first place.
        ICacheValueRedactor redactor = services.GetRequiredService<ICacheValueRedactor>();
        if (redactor.Redact(key, cache, valueTypeName: null).Disposition == RedactionDisposition.Redact)
        {
            return Results.Json(CacheValueResult.Redacted, SnapshotJsonOptions);
        }

        ICacheValueReader? reader = services
            .GetServices<ICacheValueReader>()
            .FirstOrDefault(r => string.Equals(r.CacheName, cache, StringComparison.Ordinal));
        if (reader is null)
        {
            return Results.Json(CacheValueResult.Unsupported, SnapshotJsonOptions);
        }

        CacheValueResult result = await reader.ReadAsync(key, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result, SnapshotJsonOptions);
    }

    private static async Task<IResult> EvictAsync(
        HttpContext context,
        EvictRequest request,
        ILoggerFactory loggerFactory)
    {
        CachemixOptions options = context.RequestServices
            .GetRequiredService<IOptions<CachemixOptions>>().Value;
        if (!options.AllowDestructiveActions)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // Lightweight CSRF mitigation: require a custom header that a cross-origin
        // HTML form cannot set. The dashboard's own script always sends it.
        if (!context.Request.Headers.ContainsKey(ActionHeader))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.Cache)
            || string.IsNullOrWhiteSpace(request.Key))
        {
            return Results.BadRequest();
        }

        ICacheCommander? commander = context.RequestServices
            .GetServices<ICacheCommander>()
            .FirstOrDefault(c => string.Equals(c.CacheName, request.Cache, StringComparison.Ordinal));
        if (commander is null)
        {
            return Results.NotFound();
        }

        await commander.EvictAsync(request.Key, context.RequestAborted).ConfigureAwait(false);

        ILogger logger = loggerFactory.CreateLogger(DashboardLoggerCategory);
        DashboardLog.KeyEvicted(logger, request.Key, request.Cache);
        return Results.Ok();
    }

    private static async Task<IResult> EvictTagAsync(
        HttpContext context,
        EvictTagRequest request,
        ILoggerFactory loggerFactory)
    {
        CachemixOptions options = context.RequestServices
            .GetRequiredService<IOptions<CachemixOptions>>().Value;
        if (!options.AllowDestructiveActions)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // The same lightweight CSRF mitigation the evict endpoint uses.
        if (!context.Request.Headers.ContainsKey(ActionHeader))
        {
            return Results.StatusCode(StatusCodes.Status400BadRequest);
        }

        if (request is null
            || string.IsNullOrWhiteSpace(request.Cache)
            || string.IsNullOrWhiteSpace(request.Tag))
        {
            return Results.BadRequest();
        }

        ICacheTagInvalidator? invalidator = context.RequestServices
            .GetServices<ICacheTagInvalidator>()
            .FirstOrDefault(i => string.Equals(i.CacheName, request.Cache, StringComparison.Ordinal));
        if (invalidator is null)
        {
            // The named cache has no native tag support.
            return Results.NotFound();
        }

        await invalidator.InvalidateTagAsync(request.Tag, context.RequestAborted).ConfigureAwait(false);

        ILogger logger = loggerFactory.CreateLogger(DashboardLoggerCategory);
        DashboardLog.TagInvalidated(logger, request.Tag, request.Cache);
        return Results.Ok();
    }

    private static async Task WriteAssetAsync(HttpContext context, string assetPath)
    {
        EmbeddedAsset? asset = EmbeddedAssets.Get(assetPath);
        if (asset is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = asset.ContentType;
        await context.Response.Body.WriteAsync(asset.Content).ConfigureAwait(false);
    }

    private static void ApplySecurityHeaders(HttpResponse response)
    {
        IHeaderDictionary headers = response.Headers;
        headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        headers["X-Frame-Options"] = "DENY";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] =
            "default-src 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
    }
}

/// <summary>The request body for the dashboard's evict endpoint.</summary>
/// <param name="Cache">The logical name of the cache to evict from.</param>
/// <param name="Key">The cache key to evict.</param>
internal sealed record EvictRequest(string Cache, string Key);

/// <summary>The request body for the dashboard's tag-invalidation endpoint.</summary>
/// <param name="Cache">The logical name of the cache to invalidate within.</param>
/// <param name="Tag">The tag whose entries should be removed.</param>
internal sealed record EvictTagRequest(string Cache, string Tag);
