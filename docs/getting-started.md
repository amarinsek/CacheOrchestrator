# Getting started

> **Guide.** Product overview: [root README](../README.md). Next orientation: [Guide](guide/README.md). Catalog: [documentation index](README.md).

This page takes you from an empty project to a working domain endpoint, then points to the rest of the documentation.

If you have not run anything yet:

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

See [samples/CacheOrchestrator.Minimal](../samples/CacheOrchestrator.Minimal).

## How the pieces fit

A **domain** is a name in configuration (`catalog`, `osm-tiles`, …). It holds TTLs, Version, client headers, and which Fusion instance to use. You attach it to HTTP with `.CacheOutputWithDomain` or `[CacheDomain]`. `IDomainFusionCache.GetOrSetAsync` uses the same options.

- **Output Cache** stores the full HTTP response. You enable it by putting the domain on the endpoint.
- **FusionCache** stores the object your factory produced. You call `IDomainFusionCache`.
- **Client Cache-Control** is written from the domain on the way out. You do not set those headers by hand.

The core package uses in-memory stores. Redis is a separate package.

## Install

```bash
dotnet add package CacheOrchestrator
```

## Configure

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "Domains": {
      "catalog": {
        "Version": "1",
        "ClientCacheability": "Public",
        "ClientTtlSeconds": 60,
        "OutputCacheTtlSeconds": 120,
        "FusionCacheSoftTtlSeconds": 300
      }
    }
  }
}
```

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

The domain on the endpoint is enough. Fusion reads it from the request (or from endpoint metadata).

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

If the endpoint has no domain, pass the name: `GetOrSetAsync(http, "catalog", factory, cancellationToken)`. Without a domain the factory runs uncached. Details: [fusion-cache.md](fusion-cache.md).

## Reading `X-Cache`

On domain endpoints, with `EmitDiagnosticsHeaders` at its default of `true`:

```http
X-Cache: domain=catalog; version=1; client=public; phase=n/a; output=miss; data=miss; ms=12
```

- **output** — `miss`, `hit`, or `bypass` for Output Cache.
- **data** — Fusion (`hit`, `miss`, …). Omitted when Output Cache already hit.
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
"FusionCacheInstances": {
  "default": { "Provider": "Redis" }
},
"Redis": { "Configuration": "localhost:6379" }
```

## Next

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
