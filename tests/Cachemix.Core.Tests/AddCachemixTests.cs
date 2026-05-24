using Cachemix.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cachemix.Core.Tests;

public sealed class AddCachemixTests
{
    [Fact]
    public void AddCachemix_DecoratesMemoryCache()
    {
        IServiceCollection services = TestServices.Create();
        services.AddMemoryCache();
        services.AddCachemix();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CachemixMemoryCache>(provider.GetRequiredService<IMemoryCache>());
    }

    [Fact]
    public void AddCachemix_DecoratesDistributedCache()
    {
        IServiceCollection services = TestServices.Create();
        services.AddDistributedMemoryCache();
        services.AddCachemix();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CachemixDistributedCache>(provider.GetRequiredService<IDistributedCache>());
    }

    [Fact]
    public void AddCachemix_RegistersTelemetry()
    {
        IServiceCollection services = TestServices.Create();
        services.AddMemoryCache();
        services.AddCachemix();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ICacheTelemetry>());
    }

    [Fact]
    public void AddCachemix_CalledTwice_DoesNotDoubleDecorate()
    {
        IServiceCollection services = TestServices.Create();
        services.AddMemoryCache();
        services.AddCachemix();
        services.AddCachemix();

        using ServiceProvider provider = services.BuildServiceProvider();
        IMemoryCache cache = provider.GetRequiredService<IMemoryCache>();

        // The decorator wraps a plain MemoryCache, never another decorator.
        var decorator = Assert.IsType<CachemixMemoryCache>(cache);
        Assert.IsType<MemoryCache>(decorator.Inner);
    }
}
