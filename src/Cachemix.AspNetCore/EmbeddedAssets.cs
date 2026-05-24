using System.Reflection;

namespace Cachemix.AspNetCore;

/// <summary>A static dashboard asset loaded from an embedded resource.</summary>
/// <param name="Content">The asset bytes.</param>
/// <param name="ContentType">The HTTP content type to serve the asset with.</param>
internal sealed record EmbeddedAsset(byte[] Content, string ContentType);

/// <summary>
/// Loads the dashboard's HTML, CSS, JavaScript and other static assets from
/// resources embedded in this assembly. There is no dependency on the file
/// system or a CDN.
/// </summary>
internal static class EmbeddedAssets
{
    private static readonly Assembly ThisAssembly = typeof(EmbeddedAssets).Assembly;
    private const string ResourcePrefix = "Cachemix.AspNetCore.assets.";

    /// <summary>Loads the asset at <paramref name="assetPath"/>, if it exists.</summary>
    /// <param name="assetPath">A relative asset path, such as <c>index.html</c> or <c>cachemix.js</c>.</param>
    /// <returns>The asset, or <see langword="null"/> when it is not found.</returns>
    public static EmbeddedAsset? Get(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        string normalized = assetPath.Replace('\\', '/').Trim('/');

        // Embedded resource names are flat, so a traversal sequence can never
        // resolve — reject it outright as defence in depth.
        if (normalized.Length == 0 || normalized.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        string resourceName = ResourcePrefix + normalized.Replace('/', '.');
        using Stream? stream = ThisAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new EmbeddedAsset(buffer.ToArray(), ContentTypeFor(normalized));
    }

    private static string ContentTypeFor(string path)
    {
        int lastDot = path.LastIndexOf('.');
        string extension = lastDot >= 0 ? path[(lastDot + 1)..].ToLowerInvariant() : string.Empty;
        return extension switch
        {
            "html" => "text/html; charset=utf-8",
            "css" => "text/css; charset=utf-8",
            "js" => "text/javascript; charset=utf-8",
            "json" => "application/json; charset=utf-8",
            "svg" => "image/svg+xml",
            "woff2" => "font/woff2",
            "ico" => "image/x-icon",
            "png" => "image/png",
            _ => "application/octet-stream",
        };
    }
}
