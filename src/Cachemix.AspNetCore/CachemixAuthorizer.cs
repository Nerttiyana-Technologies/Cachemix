using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Cachemix.AspNetCore;

/// <summary>
/// Runs the configured <see cref="ICachemixAuthorizationFilter"/> set for every
/// dashboard request. When no filter is configured the dashboard is allowed only
/// in the Development environment.
/// </summary>
internal sealed class CachemixAuthorizer
{
    private readonly ICachemixAuthorizationFilter[] _filters;
    private readonly bool _isDevelopment;

    public CachemixAuthorizer(IEnumerable<ICachemixAuthorizationFilter> filters, IHostEnvironment environment)
    {
        _filters = filters as ICachemixAuthorizationFilter[] ?? [.. filters];
        _isDevelopment = environment.IsDevelopment();
    }

    /// <summary>Decides whether the request may access the dashboard.</summary>
    /// <param name="context">The current HTTP request, or <see langword="null"/> when unavailable.</param>
    /// <returns><see langword="true"/> when access is allowed.</returns>
    public bool IsAuthorized(HttpContext? context)
    {
        if (context is null)
        {
            return false;
        }

        if (_filters.Length == 0)
        {
            // No filter configured: permitted only in Development. Mapping logic
            // already guarantees this state cannot occur outside Development.
            return _isDevelopment;
        }

        foreach (ICachemixAuthorizationFilter filter in _filters)
        {
            if (!filter.Authorize(context))
            {
                return false;
            }
        }

        return true;
    }
}
