using System.Net;
using Microsoft.AspNetCore.Http;

namespace Cachemix.AspNetCore;

/// <summary>
/// An <see cref="ICachemixAuthorizationFilter"/> that allows only requests
/// originating from the local machine (a loopback address, or the same address
/// the server is listening on).
/// </summary>
public sealed class LocalRequestsOnlyAuthorizationFilter : ICachemixAuthorizationFilter
{
    /// <inheritdoc />
    public bool Authorize(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPAddress? remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        IPAddress? localIp = context.Connection.LocalIpAddress;
        return localIp is not null && remoteIp.Equals(localIp);
    }
}
