using System.Net;
using Cachemix.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Cachemix.AspNetCore.Tests;

/// <summary>
/// Builds an in-memory ASP.NET Core host that mounts the Cachemix dashboard over
/// a <see cref="TestServer"/>. This is the dashboard's real request pipeline —
/// routing, the endpoint group, the authorization guard and the embedded assets —
/// exercised end to end without a network socket.
/// </summary>
internal static class DashboardHost
{
    /// <summary>Starts a dashboard host and returns it; dispose it to shut the host down.</summary>
    /// <param name="environment">The hosting environment name (<c>Development</c> by default).</param>
    /// <param name="configure">An optional callback to adjust <see cref="CachemixOptions"/>.</param>
    /// <param name="configureBuilder">An optional callback to further configure the Cachemix builder.</param>
    /// <param name="forceLoopbackRemoteIp">
    /// When <see langword="true"/>, a middleware stamps every request with a loopback
    /// remote address — the equivalent of a request from the local machine.
    /// </param>
    /// <returns>The started host.</returns>
    public static async Task<IHost> StartAsync(
        string environment = "Development",
        Action<CachemixOptions>? configure = null,
        Action<ICachemixBuilder>? configureBuilder = null,
        bool forceLoopbackRemoteIp = false)
    {
        IHostBuilder builder = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment(environment);

                web.ConfigureServices(services =>
                {
                    // A bare HostBuilder registers no logging; the dashboard's
                    // endpoint mapping resolves ILoggerFactory, so add it explicitly.
                    services.AddLogging();
                    services.AddRouting();
                    services.AddMemoryCache();

                    ICachemixBuilder cachemix = services
                        .AddCachemix(options => configure?.Invoke(options))
                        .AddDashboard();
                    configureBuilder?.Invoke(cachemix);
                });

                web.Configure(app =>
                {
                    if (forceLoopbackRemoteIp)
                    {
                        app.Use(async (context, next) =>
                        {
                            context.Connection.RemoteIpAddress = IPAddress.Loopback;
                            context.Connection.LocalIpAddress = IPAddress.Loopback;
                            await next(context);
                        });
                    }

                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapCachemix());
                });
            });

        return await builder.StartAsync();
    }

    /// <summary>Creates an <see cref="HttpClient"/> bound to the host's in-memory test server.</summary>
    /// <param name="host">A host started by <see cref="StartAsync"/>.</param>
    /// <returns>A client whose requests are dispatched to the dashboard pipeline.</returns>
    public static HttpClient CreateClient(this IHost host) => host.GetTestClient();
}
