using Microsoft.Extensions.DependencyInjection;

namespace Cachemix.Core;

/// <summary>
/// The result of calling <c>AddCachemix</c>. Provider packages (Redis, Hybrid)
/// and the dashboard package add their own extension methods to this type.
/// </summary>
public interface ICachemixBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }
}

/// <summary>The default <see cref="ICachemixBuilder"/> implementation.</summary>
internal sealed class CachemixBuilder : ICachemixBuilder
{
    /// <summary>Creates a builder over the given service collection.</summary>
    /// <param name="services">The service collection.</param>
    public CachemixBuilder(IServiceCollection services) => Services = services;

    /// <inheritdoc />
    public IServiceCollection Services { get; }
}
