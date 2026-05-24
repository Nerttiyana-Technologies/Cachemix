# Cachemix

**In-memory, Redis & HybridCache visualizer for .NET.**

Caching is intentionally invisible — and that invisibility is what makes it hard
to debug. Cachemix mounts a live dashboard at `/cachemix` inside your own app so
you can see every cache key, its size, its expiration, and whether your app is
actually *hitting* the cache or quietly repeating the same database trip.

> **Status:** pre-1.0, under active development. APIs may change before 1.0.

## Highlights

- **Live heap inspector** — every key with size, value, absolute & sliding expiration.
- **Hit/miss telemetry** — real-time cache-efficiency charts.
- **Nuke button** — evict a key or a whole namespace to force recomputation.
- Works with `IMemoryCache`, `IDistributedCache` (Redis / Garnet / Valkey) and `HybridCache`.
- Self-contained: one NuGet package, embedded UI, no CDN, no build step.
- Safe by default: Development-only unless you explicitly opt in.

## Quick start

```csharp
builder.Services.AddMemoryCache();
builder.Services.AddCachemix();      // call after your cache registrations

var app = builder.Build();
app.MapCachemix();                   // dashboard at /cachemix (Development only)
```

Then browse to `/cachemix`.

## Supported frameworks

.NET 8, .NET 9, and .NET 10. Built and developed on the .NET SDK 10.0.300.


## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). For security issues, see [SECURITY.md](SECURITY.md).

## License

MIT — see [LICENSE](LICENSE).
