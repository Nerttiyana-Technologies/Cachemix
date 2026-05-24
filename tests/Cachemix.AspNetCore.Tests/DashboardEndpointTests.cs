using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cachemix.AspNetCore.Tests;

/// <summary>
/// In-memory integration tests for the Cachemix dashboard's HTTP surface: the
/// served shell and assets, the JSON API, the destructive-action guards and the
/// environment-aware mounting and authorization rules.
/// </summary>
public sealed class DashboardEndpointTests
{
    [Fact]
    public async Task Dashboard_InDevelopment_ServesTheIndexShell()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Cachemix", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_ReplacesTheBasePlaceholderWithTheDashboardPath()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        string body = await client.GetStringAsync("/cachemix");

        // The build-time placeholder must be substituted with the live base path.
        Assert.DoesNotContain("__cachemix_base__", body, StringComparison.Ordinal);
        Assert.Contains("<base href=\"/cachemix/\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_ReturnsTheDashboardPayloadAsJson()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix/api/snapshot");
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using JsonDocument document = await JsonDocumentAsync(stream);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(document.RootElement.TryGetProperty("caches", out _));
        Assert.True(document.RootElement.TryGetProperty("entries", out _));
        Assert.True(document.RootElement.TryGetProperty("instanceName", out _));
    }

    [Fact]
    public async Task Asset_ServesEmbeddedStylesheetWithTheCorrectContentType()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix/assets/cachemix.css");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task Asset_ServesEmbeddedScriptWithTheCorrectContentType()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix/assets/cachemix.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Asset_ForAnUnknownPath_ReturnsNotFound()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix/assets/no-such-asset.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_AppliesItsSecurityHeaders()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix");

        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diff_WithNoPeersConfigured_ReturnsASingleInstanceReport()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix/api/diff");
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using JsonDocument document = await JsonDocumentAsync(stream);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // With no peers there is only the local instance, and nothing to diverge.
        Assert.Equal(1, document.RootElement.GetProperty("instances").GetArrayLength());
        Assert.Equal(0, document.RootElement.GetProperty("divergences").GetArrayLength());
    }

    [Fact]
    public async Task Value_WithoutQueryParameters_ReturnsBadRequest()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix/api/value");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Evict_WithoutTheActionHeader_ReturnsBadRequest()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        // The custom action header is the dashboard's lightweight CSRF mitigation.
        HttpResponseMessage response = await client.PostAsync(
            "/cachemix/api/evict",
            JsonContent.Create(new { cache = "MemoryCache", key = "k" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Evict_ForAnUnknownCache_ReturnsNotFound()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/cachemix/api/evict")
        {
            Content = JsonContent.Create(new { cache = "NoSuchCache", key = "k" }),
        };
        request.Headers.Add("X-Cachemix-Action", "evict");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Evict_ForAKnownCacheWithTheActionHeader_Succeeds()
    {
        using IHost host = await DashboardHost.StartAsync();
        using HttpClient client = host.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/cachemix/api/evict")
        {
            Content = JsonContent.Create(new { cache = "MemoryCache", key = "missing-but-harmless" }),
        };
        request.Headers.Add("X-Cachemix-Action", "evict");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_OutsideDevelopmentWithoutOptIn_IsNotMapped()
    {
        using IHost host = await DashboardHost.StartAsync(environment: "Production");
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix");

        // Safe by default: the dashboard simply does not exist in Production.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_OutsideDevelopmentOptInWithoutAuthFilter_FailsToStart()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using IHost host = await DashboardHost.StartAsync(
                environment: "Production",
                configure: options => options.EnableOutsideDevelopment = true);
        });
    }

    [Fact]
    public async Task Dashboard_OutsideDevelopmentOptInWithLocalAuth_AllowsALoopbackRequest()
    {
        using IHost host = await DashboardHost.StartAsync(
            environment: "Production",
            configure: options => options.EnableOutsideDevelopment = true,
            configureBuilder: builder => builder.AllowLocalRequestsOnly(),
            forceLoopbackRemoteIp: true);
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_OutsideDevelopmentOptInWithLocalAuth_BlocksANonLocalRequest()
    {
        using IHost host = await DashboardHost.StartAsync(
            environment: "Production",
            configure: options => options.EnableOutsideDevelopment = true,
            configureBuilder: builder => builder.AllowLocalRequestsOnly());
        using HttpClient client = host.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/cachemix");

        // A non-loopback caller is answered with 404, never 403 — the dashboard's
        // presence is not disclosed to unauthorized callers.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonDocument> JsonDocumentAsync(Stream stream)
        => await JsonDocument.ParseAsync(stream);

    private static string Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? string.Join(',', values)
            : string.Empty;
}
