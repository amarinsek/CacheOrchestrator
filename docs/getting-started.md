# Getting started

**CacheOrchestrator** is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

This page is **day 1**: get a working endpoint, understand the main pieces, then choose what to learn next.

If you have not run anything yet:

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
curl -i http://localhost:5290/hello   # twice
```

See [samples/CacheOrchestrator.Minimal](../samples/CacheOrchestrator.Minimal).

---

## Mental model (one minute)

```text
Domain (config name, e.g. "catalog")
  → TTLs, Version, client Cache-Control, which Fusion instance
  → applied on HTTP with .CacheOutputWithDomain / [CacheDomain]
  → IDomainFusionCache.GetOrSetAsync uses the same domain options
```

| Layer | What it stores | You touch it via |
|-------|----------------|------------------|
| **Output Cache** | Full HTTP responses | Endpoint policy (domain) |
| **FusionCache** | Objects from your factory | `IDomainFusionCache` |
| **Client headers** | Browser/CDN `Cache-Control` | Domain settings (automatic on response) |

You do **not** need Redis for the happy path. InMemory is built into the core package.

---

## Install and wire-up

```bash
dotnet add package CacheOrchestrator
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
// …
app.UseCacheOrchestrator();
```

### Minimal config

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

### Minimal endpoint

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductsAsync(ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Domain is declared **once** on the endpoint. Fusion resolves options from the request / metadata — no manual `EnsureDomainOptions` on this path.

### Controllers

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

---

## Reading `X-Cache`

On domain endpoints (with `EmitDiagnosticsHeaders: true`, the default):

```http
X-Cache: domain=catalog; version=1; client=public; phase=n/a; output=miss; data=miss; ms=12
```

| Token | Meaning |
|-------|---------|
| `output=miss` / `hit` / `bypass` | Output Cache |
| `data=miss` / `hit` / … | Fusion (omitted when `output=hit`) |
| `phase=` | Client Cache Schedule (or `n/a`) |

Production tip: set `"EmitDiagnosticsHeaders": false` if you do not want this visible to clients — metrics still work. See [observability.md](observability.md).

---

## Common next steps

| I want to… | Read / do |
|------------|-----------|
| Play with TTLs and schedule in a UI | [Sample playground](../samples/CacheOrchestrator.Sample) |
| OSM cutover vs product CRUD | [domain-profiles.md](domain-profiles.md) |
| Ramp client `max-age` before deploy | [client-cache-schedule.md](client-cache-schedule.md) |
| Purge one product or whole domain | [invalidation.md](invalidation.md) |
| Live stats / ops invalidate on one process | [admin.md](admin.md) (Local Admin) |
| Multi-instance ops dashboard | [admin.md](admin.md) · run `src/CacheOrchestrator.Admin` |
| Multi-instance InMemory purge | [cluster-bus.md](cluster-bus.md) |
| Invalidate after EF `SaveChanges` | [ef-core-invalidation.md](ef-core-invalidation.md) |
| Use Redis | [backends.md](backends.md) · [deployment.md](deployment.md) |
| Auth / private pages | [output-cache.md](output-cache.md) · [faq.md](faq.md) |
| Full settings list | [configuration.md](configuration.md) |
| Why this vs hand-rolled cache | [comparison.md](comparison.md) |

### Fusion without Output Cache

Pass the domain explicitly:

```csharp
await cache.GetOrSetAsync(http, "catalog", factory, cancellationToken);
```

If no domain is resolved, the factory runs **uncached** (Warning log + `data=unresolved`). Details: [fusion-cache.md](fusion-cache.md).

### Redis (optional package)

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

---

## Learning path

```text
Minimal sample  →  this page  →  playground sample
                              →  domain-profiles / client-cache-schedule
                              →  configuration + deployment (production)
```

Full catalog: [docs/README.md](README.md).
