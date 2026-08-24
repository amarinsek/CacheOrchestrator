# Getting started

> **Guide.** Product overview: [root README](../README.md). Next orientation: [Guide](guide/README.md). Catalog: [documentation index](README.md). Packages: [packages.md](packages.md).

This page takes you from an empty project to a working domain endpoint, then points to the rest of the documentation.

If you have not run anything yet:

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

See [samples/CacheOrchestrator.Minimal](../samples/CacheOrchestrator.Minimal).

## How the pieces fit

A **domain** is a named policy, not a store. Output Cache, **data cache**, and client `Cache-Control` remain the three layers; CacheOrchestrator applies the same options to all three. In configuration it is a name (`catalog`, `osm-tiles`, …). It holds TTLs, Version, client headers, and which data-cache instance to use. You attach it to HTTP with `.CacheOutputWithDomain` or `[CacheDomain]`. `IDomainFusionCache.GetOrSetAsync` uses the same options (HTTP projection over `ICacheOrchestrator`).

- **Output Cache** stores the full HTTP response. You enable it by putting the domain on the endpoint.
- **Data cache** stores the object your factory produced (FusionCache by default with the meta package, or HybridCache). You call `IDomainFusionCache` or `ICacheOrchestrator`.
- **Client Cache-Control** is written from the domain on the way out. You do not set those headers by hand.

In-memory stores are built in. Redis, Hybrid, the HTTP cluster bus, and EF hooks are separate packages — [packages.md](packages.md).

## Install

```bash
dotnet add package CacheOrchestrator
```

That meta package includes AspNetCore + FusionCache. For Hybrid, Redis, HttpBus, or EF, see [packages.md](packages.md).

## Configure

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Ttl": "00:05:00" },
        "OutputCache": { "Ttl": "00:02:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:01:00" }
      }
    }
  }
}
```

Optional Fusion-only knobs (hard TTL, fail-safe, …) go under `FusionCache` on the domain — [fusion-cache.md](fusion-cache.md), [configuration.md](configuration.md).

## Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();
```

## Apply

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

The domain on the endpoint is enough. Data cache reads it from the request (or from endpoint metadata).

On a controller:

```csharp
[CacheDomain("catalog")]
public sealed class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromServices] IDomainFusionCache cache,
        CancellationToken cancellationToken)
    {
        var data = await cache.GetOrSetAsync(HttpContext, LoadProductsAsync, cancellationToken);
        return Ok(data);
    }
}
```

If the endpoint has no domain, pass the name: `GetOrSetAsync(http, "catalog", factory, cancellationToken)`. Without a domain the factory runs uncached. Details: [fusion-cache.md](fusion-cache.md). Class libraries can use `ICacheOrchestrator` instead — [packages.md](packages.md).

> **Note:** Unlike native ASP.NET Core Output Caching which ignores query parameters by default, CacheOrchestrator domains **vary by all non-tracking query parameters** by default. `?id=1` and `?id=2` will automatically be cached separately.

## Reading `X-Cache`

On domain endpoints, with `EmitDiagnosticsHeaders` at its default of `true`:

```http
X-Cache: domain=catalog; version=1; client=public; phase=n/a; oc=miss; dc=miss; fa=run; ms=12
```

- **oc** — Output Cache `miss`, `hit`, `bypass`, or `off`.
- **dc** — Data cache (`hit`, `miss`, `stale`, …). Omitted when Output Cache already hit.
- **fa** — `run` when `dc` is present and is not `hit` (factory callback ran).
- **phase** — Client Cache Schedule, or `n/a`.

To hide this header from clients, set `"EmitDiagnosticsHeaders": false`. Metrics continue. See [observability.md](observability.md).

## Redis

```bash
dotnet add package CacheOrchestrator.Redis
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());
```

```json
"DataCacheInstances": {
  "default": { "Provider": "Redis" }
},
"Redis": { "Configuration": "localhost:6379" }
```

## Next

- [Packages](packages.md) — compose Core / Fusion / Hybrid / AspNetCore
- [Guide](guide/README.md) — concepts, topologies, operations
- [Playground sample](../samples/CacheOrchestrator.Sample) — TTLs and schedule in a UI
- [Domain profiles](domain-profiles.md) — published datasets versus changing records
- [Client Cache Schedule](client-cache-schedule.md) — client `max-age` before a cutover
- [Invalidation](invalidation.md) — Version, tags, a single id
- [Admin](admin.md) — Admin API on one process; Admin Console App across instances
- [Cluster bus](cluster-bus.md) — commands to every instance
- [EF Core](ef-core-invalidation.md) — purge after `SaveChanges`
- [Output Cache](output-cache.md) — authenticated traffic
- [Configuration](configuration.md) — full settings list
- [Comparison](comparison.md) — the usual stack versus CacheOrchestrator

Index: [docs/README.md](README.md).
