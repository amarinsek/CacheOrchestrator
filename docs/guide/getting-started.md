# Getting started

> **Guide.** Product overview: [root README](../../README.md). Next: [Guide index](README.md). Catalog: [documentation index](../README.md).

This page takes you from an empty ASP.NET Core project to a working cached endpoint. For the “why”, see the [root README](../../README.md). For mental model after this page, read [concepts](concepts.md).

Prefer to see a hit first?

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

The first response is an Output Cache miss; the second is a hit. Look at `X-Cache`: `oc=miss`, then `oc=hit`. Notes: [Minimal sample](../../samples/CacheOrchestrator.Minimal).

---

## What you are configuring

A **domain** is a named policy in configuration — not a cache store of its own. One domain coordinates three layers:

| Layer | What it stores | How you turn it on |
|-------|----------------|--------------------|
| **Client Cache-Control** | Browser / CDN headers | Nested `ClientCache` on the domain |
| **Output Cache** | Full HTTP response (GET/HEAD + Url by default; other methods via [cache identity](../reference/cache-identity.md)) | `.CacheOutputWithDomain` / `[CacheDomain]` |
| **Data cache** | The object your factory returns | `IDomainDataCache.GetOrSetAsync` (or Core `ICacheOrchestrator`) |

In-memory stores ship with the meta package. Redis, HybridCache, the HTTP cluster bus, and EF invalidation are separate packages — [packages](packages.md).

---

## 1. Install

```bash
dotnet add package CacheOrchestrator --prerelease
```

That meta package is AspNetCore + FusionCache — the usual web stack. Other compositions: [packages](packages.md) · [composition how-to](../how-to/composition.md).

---

## 2. Configure a domain

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
        "DataCache": { "TtlSeconds": 300 },
        "OutputCache": { "TtlSeconds": 120 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 60 }
      }
    }
  }
}
```

TTL fields in JSON are **integer seconds** (`TtlSeconds`, …). Optional Fusion-only knobs (hard TTL, fail-safe, …) sit under nested `FusionCache` on the domain — [configuration](../reference/configuration.md).

---

## 3. Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();
```

---

## 4. Apply the domain

### Minimal APIs

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

The domain on the endpoint is enough. `IDomainDataCache` reuses the same options for the data-cache call.

### Controllers

```csharp
[CacheDomain("catalog")]
public sealed class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromServices] IDomainDataCache cache,
        CancellationToken cancellationToken)
    {
        var data = await cache.GetOrSetAsync(HttpContext, LoadProductsAsync, cancellationToken);
        return Ok(data);
    }
}
```

### Data cache without Output Cache on the route

Pass the domain name explicitly:

```csharp
await cache.GetOrSetAsync(http, "catalog", LoadProductsAsync, cancellationToken);
```

Without a domain, the factory runs **uncached**. Details: [data cache](../reference/data-cache.md). Class libraries use `ICacheOrchestrator` from Core — [composition §7](../how-to/composition.md#scenario-7).

> **Query strings:** native ASP.NET Output Caching ignores query parameters by default. CacheOrchestrator domains **vary by all non-tracking query parameters**, so `?id=1` and `?id=2` are separate entries.

---

## 5. Read `X-Cache`

On domain endpoints (default `EmitDiagnosticsHeaders: true`):

```http
X-Cache: domain=catalog; version=1; client=public; phase=n/a; oc=miss; dc=miss; fa=run; ms=12
```

A successful second request typically shows `oc=hit`. Full field list: [observability](../reference/observability.md).

---

## Optional: Redis for the data cache

```bash
dotnet add package CacheOrchestrator.Redis --prerelease
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

Layouts and multi-instance behaviour: [topologies](topologies.md) · [composition §4](../how-to/composition.md#scenario-4).

---

## What to read next

| Goal | Page |
|------|------|
| Mental model (domain, Version, layers) | [Concepts](concepts.md) |
| Snapshot tiles vs CRUD products | [Domain profiles](domain-profiles.md) |
| Which NuGet / how to wire Hybrid, EF, libraries | [Packages](packages.md) · [Composition](../how-to/composition.md) |
| Planned client cutovers | [Client Cache Schedule](client-cache-schedule.md) |
| Common mistakes | [FAQ](faq.md) |
| Full playground | [Sample](../../samples/CacheOrchestrator.Sample) |

Catalog: [docs/README.md](../README.md).
