using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cachemix.Abstractions;
using Cachemix.Core;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Cachemix.AspNetCore;

/// <summary>
/// Renders an arbitrary cached value into bounded display text for the
/// inspector. Lives in <c>Cachemix.AspNetCore</c> rather than the trim-marked
/// core so the reflection-based JSON serializer stays out of the AOT surface.
/// </summary>
internal static class ValueFormatter
{
    /// <summary>The largest amount of display text the inspector will return.</summary>
    private const int MaxTextLength = 16 * 1024;

    /// <summary>How many leading bytes of a binary value to render as a hex preview.</summary>
    private const int HexPreviewBytes = 384;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 32,
    };

    /// <summary>Describes an in-memory cache value for display.</summary>
    /// <param name="value">The raw cached value.</param>
    /// <returns>An available <see cref="CacheValueResult"/>.</returns>
    public static CacheValueResult Describe(object? value)
    {
        if (value is null)
        {
            return Available("null", typeName: null, sizeBytes: 0L, truncated: false);
        }

        if (value is byte[] bytes)
        {
            return DescribeBytes(bytes);
        }

        Type type = value.GetType();
        string typeName = FriendlyTypeName(type);

        if (value is string text)
        {
            (string capped, bool truncated) = Cap(text);
            return Available(capped, typeName, Utf8Size(text), truncated);
        }

        if (type.IsPrimitive || value is decimal or DateTime or DateTimeOffset or TimeSpan or Guid)
        {
            string scalar = Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? value.ToString()
                ?? string.Empty;
            (string cappedScalar, bool scalarTruncated) = Cap(scalar);
            return Available(cappedScalar, typeName, SizeEstimator.TryEstimate(value), scalarTruncated);
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(value, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A value that resists serialization still has a useful ToString().
            json = value.ToString() ?? type.FullName ?? "(unprintable value)";
        }

        (string cappedJson, bool jsonTruncated) = Cap(json);
        return Available(cappedJson, typeName, Utf8Size(json), jsonTruncated);
    }

    /// <summary>Describes a raw byte payload, decoding it as text when it looks like text.</summary>
    /// <param name="bytes">The stored bytes.</param>
    /// <returns>An available <see cref="CacheValueResult"/>.</returns>
    public static CacheValueResult DescribeBytes(byte[] bytes)
    {
        if (LooksLikeText(bytes, out string? decoded))
        {
            (string capped, bool truncated) = Cap(decoded!);
            return Available(capped, "byte[] (text)", bytes.Length, truncated);
        }

        int previewLength = Math.Min(bytes.Length, HexPreviewBytes);
        var builder = new StringBuilder(previewLength * 3 + 48);
        builder.Append(CultureInfo.InvariantCulture, $"binary — {bytes.Length:N0} bytes\n\n");
        for (int i = 0; i < previewLength; i++)
        {
            builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            builder.Append((i + 1) % 24 == 0 ? '\n' : ' ');
        }

        bool clipped = bytes.Length > previewLength;
        if (clipped)
        {
            builder.Append("\n… (preview clipped)");
        }

        return Available(builder.ToString().TrimEnd(), "byte[] (binary)", bytes.Length, clipped);
    }

    private static CacheValueResult Available(string text, string? typeName, long? sizeBytes, bool truncated)
        => new()
        {
            Status = CacheValueStatus.Available,
            ValueText = text,
            ValueTypeName = typeName,
            SizeBytes = sizeBytes,
            Truncated = truncated,
        };

    private static (string Text, bool Truncated) Cap(string text)
        => text.Length <= MaxTextLength
            ? (text, false)
            : (string.Concat(text.AsSpan(0, MaxTextLength), "\n… (truncated)"), true);

    private static long Utf8Size(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary>
    /// Decides whether a byte payload is best shown as text. Decodes strictly
    /// as UTF-8 and rejects anything with a high share of non-whitespace
    /// control characters.
    /// </summary>
    private static bool LooksLikeText(byte[] bytes, out string? text)
    {
        if (bytes.Length == 0)
        {
            text = string.Empty;
            return true;
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            string decoded = strict.GetString(bytes);

            int controlCount = 0;
            foreach (char c in decoded)
            {
                if (char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
                {
                    controlCount++;
                }
            }

            // Reject when more than ~5% of characters are non-whitespace control chars.
            if (controlCount * 20 > decoded.Length)
            {
                text = null;
                return false;
            }

            text = decoded;
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = null;
            return false;
        }
    }

    private static string FriendlyTypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        string arguments = string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeName));
        return $"{name}<{arguments}>";
    }
}

/// <summary>An <see cref="ICacheValueReader"/> for the application's <c>IMemoryCache</c>.</summary>
internal sealed class MemoryCacheValueReader : ICacheValueReader
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the reader.</summary>
    /// <param name="services">The application service provider.</param>
    public MemoryCacheValueReader(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public string CacheName => CachemixConstants.DefaultMemoryCacheName;

    /// <inheritdoc />
    public ValueTask<CacheValueResult> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        IMemoryCache? cache = _services.GetService<IMemoryCache>();

        // Read through the undecorated cache so the inspector never records a
        // synthetic hit against telemetry.
        IMemoryCache? target = cache is CachemixMemoryCache decorated ? decorated.Inner : cache;
        if (target is null || !target.TryGetValue(key, out object? value))
        {
            return ValueTask.FromResult(CacheValueResult.NotFound);
        }

        return ValueTask.FromResult(ValueFormatter.Describe(value));
    }
}

/// <summary>An <see cref="ICacheValueReader"/> for the application's <c>IDistributedCache</c>.</summary>
internal sealed class DistributedCacheValueReader : ICacheValueReader
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the reader.</summary>
    /// <param name="services">The application service provider.</param>
    public DistributedCacheValueReader(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public string CacheName => CachemixConstants.DefaultDistributedCacheName;

    /// <inheritdoc />
    public async ValueTask<CacheValueResult> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        IDistributedCache? cache = _services.GetService<IDistributedCache>();

        // Read through the undecorated cache so the inspector never records a
        // synthetic hit against telemetry.
        IDistributedCache? target = cache is CachemixDistributedCache decorated ? decorated.Inner : cache;
        if (target is null)
        {
            return CacheValueResult.NotFound;
        }

        byte[]? bytes = await target.GetAsync(key, cancellationToken).ConfigureAwait(false);
        return bytes is null ? CacheValueResult.NotFound : ValueFormatter.DescribeBytes(bytes);
    }
}
