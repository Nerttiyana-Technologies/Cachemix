using Microsoft.AspNetCore.Http;

namespace Cachemix.AspNetCore;

/// <summary>
/// Authorizes a request to the Cachemix dashboard. Register one or more
/// implementations to control who may reach the dashboard — this is mandatory
/// when the dashboard is enabled outside the Development environment.
/// </summary>
/// <remarks>Implementations must be thread-safe; a single instance handles every request.</remarks>
public interface ICachemixAuthorizationFilter
{
    /// <summary>Decides whether <paramref name="context"/> may access the dashboard.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns><see langword="true"/> to allow the request; otherwise <see langword="false"/>.</returns>
    bool Authorize(HttpContext context);
}
