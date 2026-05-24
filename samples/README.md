# Cachemix samples

Three runnable apps that demonstrate Cachemix against each cache backend. Every
sample drives its cache from a background workload, so the dashboard shows live
activity as soon as the app starts. Each app redirects its root URL to the
dashboard at **`/cachemix`**.

## Cachemix.Samples.MinimalApi

A Minimal API app using `IMemoryCache`.

```bash
dotnet run --project samples/Cachemix.Samples.MinimalApi
```

Also try `GET /weather/{city}` to populate a key on demand.

## Cachemix.Samples.MvcRedis

An MVC app using `IDistributedCache` backed by Redis, with full SCAN-based key
enumeration. Start Redis first:

```bash
docker compose -f samples/docker-compose.yml up -d
dotnet run --project samples/Cachemix.Samples.MvcRedis
```

Also try `GET /products/{id}` to read a product through the cache.

## Cachemix.Samples.HybridCache

A Minimal API app using `HybridCache` with tag-based invalidation.

```bash
dotnet run --project samples/Cachemix.Samples.HybridCache
```

Also try `GET /product/{id}` to populate a tagged entry on demand.

---

Each app prints its listening URL on startup — open it and you land on the
Cachemix dashboard.
