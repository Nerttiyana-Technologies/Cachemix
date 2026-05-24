using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Cachemix.Core;

/// <summary>
/// An <see cref="ICacheEntry"/> decorator. It forwards every member to the
/// underlying entry and, when the entry is committed (on <see cref="Dispose"/>),
/// records it with Cachemix. A telemetry failure never prevents the commit.
/// </summary>
internal sealed class CachemixCacheEntry : ICacheEntry
{
    private readonly ICacheEntry _inner;
    private readonly CachemixMemoryCache _owner;
    private bool _disposed;

    /// <summary>Wraps <paramref name="inner"/> and registers an eviction callback on it.</summary>
    /// <param name="inner">The underlying cache entry.</param>
    /// <param name="owner">The owning memory-cache decorator.</param>
    public CachemixCacheEntry(ICacheEntry inner, CachemixMemoryCache owner)
    {
        _inner = inner;
        _owner = owner;

        // Register our own post-eviction callback so evictions are observed with
        // their reason. Adding to the list is additive and does not disturb any
        // callbacks the application registers later.
        _inner.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = _owner.OnEntryEvicted,
        });
    }

    /// <inheritdoc />
    public object Key => _inner.Key;

    /// <inheritdoc />
    public object? Value
    {
        get => _inner.Value;
        set => _inner.Value = value;
    }

    /// <inheritdoc />
    public DateTimeOffset? AbsoluteExpiration
    {
        get => _inner.AbsoluteExpiration;
        set => _inner.AbsoluteExpiration = value;
    }

    /// <inheritdoc />
    public TimeSpan? AbsoluteExpirationRelativeToNow
    {
        get => _inner.AbsoluteExpirationRelativeToNow;
        set => _inner.AbsoluteExpirationRelativeToNow = value;
    }

    /// <inheritdoc />
    public TimeSpan? SlidingExpiration
    {
        get => _inner.SlidingExpiration;
        set => _inner.SlidingExpiration = value;
    }

    /// <inheritdoc />
    public IList<IChangeToken> ExpirationTokens => _inner.ExpirationTokens;

    /// <inheritdoc />
    public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => _inner.PostEvictionCallbacks;

    /// <inheritdoc />
    public CacheItemPriority Priority
    {
        get => _inner.Priority;
        set => _inner.Priority = value;
    }

    /// <inheritdoc />
    public long? Size
    {
        get => _inner.Size;
        set => _inner.Size = value;
    }

    /// <summary>
    /// Commits the entry to the underlying cache and records it with Cachemix.
    /// The commit always happens, even if recording fails.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Capture metadata while it is still readable, before the commit.
        CacheEntryDescriptor? descriptor = null;
        try
        {
            descriptor = _owner.CaptureDescriptor(_inner);
        }
        catch (Exception ex)
        {
            _owner.OnTelemetryFailure(ex);
        }

        // Committing the entry to the underlying cache must always happen.
        _inner.Dispose();

        if (descriptor is not null)
        {
            _owner.OnEntrySet(descriptor);
        }
    }
}
