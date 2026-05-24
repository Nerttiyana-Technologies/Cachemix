using Cachemix.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cachemix.Core.Tests;

/// <summary>A minimal <see cref="IHostEnvironment"/> for tests.</summary>
internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "Cachemix.Core.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Helpers for building test service collections.</summary>
internal static class TestServices
{
    /// <summary>Creates a service collection with logging and a host environment registered.</summary>
    /// <param name="environmentName">The hosting environment name.</param>
    /// <returns>A service collection ready for <c>AddCachemix</c>.</returns>
    public static IServiceCollection Create(string environmentName = "Development")
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        return services;
    }
}

/// <summary>Test helpers for draining the telemetry pipeline.</summary>
internal static class PipelineTestExtensions
{
    /// <summary>Synchronously reads every signal currently buffered in the pipeline.</summary>
    /// <param name="pipeline">The pipeline to drain.</param>
    /// <returns>The drained signals.</returns>
    public static List<CacheSignal> DrainAll(this CacheEventPipeline pipeline)
    {
        var drained = new List<CacheSignal>();
        while (pipeline.Reader.TryRead(out CacheSignal signal))
        {
            drained.Add(signal);
        }

        return drained;
    }
}
