# FusionCache (application data)

Caches **serializable objects** (JSON via System.Text.Json) with L1 memory and optional L2 distributed cache (e.g., Redis, SQL Server) + backplane.

## Domain resolution (no manual Ensure on the happy path)

`IDomainFusionCache.GetOrSetAsync` resolves domain options in this order:

1. **Already on the request** — `IDomainCacheOptionsProvider.GetDomainOptions(http)`  
   (typically set by `DomainOutputCachePolicy` when you use `.CacheOutputWithDomain` / `[CacheDomain]`).
2. **Explicit domain argument** — overload `GetOrSetAsync(http, domain, factory)` calls `EnsureDomainOptions`.
3. **Endpoint metadata** — `DomainOutputCachePolicy.ResolveDomain(http)` or `[CacheDomain]` on the endpoint, then `EnsureDomainOptions`.
4. **Still missing** — factory runs **uncached** (no Fusion).

### Recommended: Output Cache + Fusion

```csharp
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;

app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, async ct => await LoadAsync(ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("products");
```

No `EnsureDomainOptions` in the handler. The policy sets options before the handler; if not, Fusion still reads the domain from endpoint metadata.

### Controllers

```csharp
[CacheDomain("products")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromServices] IDomainFusionCache cache, CancellationToken ct)
    {
        var data = await cache.GetOrSetAsync(HttpContext, async token => await LoadAsync(token), ct);
        return Ok(data);
    }
}
```

### Fusion-only (no Output Cache)

When there is **no** output-cache domain on the endpoint, you must supply the domain:

```csharp
// Preferred for Fusion-only
await cache.GetOrSetAsync(http, "products", factory, cancellationToken);

// Equivalent
domains.EnsureDomainOptions(http, "products");
await cache.GetOrSetAsync(http, factory, cancellationToken);
```

If you omit both, `GetOrSetAsync` runs the factory **without** caching:

- Logs a **Warning** (`FusionCache skipped: no domain resolved…`)
- Emits metric `cache_orchestrator.fc.requests` with `domain=_`, `result=unresolved`
- Sets disposition `data=unresolved` for `X-Cache` when Output Cache still writes headers

Prefer an explicit domain overload for Fusion-only endpoints so this path is never hit by accident.

### Entity / resource id (dynamic CRUD)

```csharp
// Key includes kind + id; tags: domain:store + entity:store:products:42 + entitykind:store:products
var product = await cache.GetOrSetEntityAsync(http, "store", "products", productId, async ct =>
    await LoadProductAsync(productId, ct), cancellationToken);

// After admin update (same Version):
await invalidator.InvalidateEntityAsync("store", "products", productId, cancellationToken);
```

A domain is a policy group. `entityKind` is required — ids are not unique inside a domain.

See [domain-profiles.md](domain-profiles.md) and [invalidation.md](invalidation.md).

| Scenario | What to do |
|----------|------------|
| `.CacheOutputWithDomain("x")` or `[CacheDomain("x")]` (list/snapshot) | `GetOrSetAsync(http, factory)` only |
| Fusion only (no OC metadata) | `GetOrSetAsync(http, "x", factory)` or `EnsureDomainOptions` |
| Single entity (CRUD) | `GetOrSetEntityAsync(http, domain, entityKind, resourceId, factory)` |
| Entity when OC already set the domain | `GetOrSetEntityAsync(http, entityKind, resourceId, factory)` |
| Custom middleware already called Ensure | `GetOrSetAsync(http, factory)` |

## When the factory runs uncached

| Situation | Disposition / observability |
|-----------|------------------------------|
| No domain options and none resolved from domain/metadata | `Unresolved` — Warning log + metric `result=unresolved` |
| `FusionCacheEnabled: false` | `Off` |
| Request `no-store` and `FusionCacheRespectNoStore` | `Bypass` |

## Cache key

`DefaultDomainKeyGenerator` builds a deterministic key (XxHash3):

**With entity identity** (set by `GetOrSetEntityAsync` on `HttpContext.Items`):

- `domain:versionHex:id:{entityKind}:{resourceId}:{hash}` where hash covers kind + id + optional encoding/host  

**Without resource id** (URL-shaped keys):

1. Route pattern + route values (or path)  
2. Query string (excluding tracking params: `utm_*`, `gclid`, …)  
3. Accept-Encoding (if `FusionCacheVaryOnEncoding`)  
4. Scheme + host (if `FusionCacheVaryOnPublicAddress`)  

Final string form includes **domain** and **Version**.

Tag on entries: `domain:{name}`.

### Custom key generator

To vary the cache key on additional dimensions (e.g. authenticated tenant, custom header), implement `IDomainKeyGenerator` and register it **before** or **after** `AddCacheOrchestrator`:

```csharp
// Option A — register before AddCacheOrchestrator
// AddCacheOrchestrator uses TryAddSingleton internally, so yours takes priority:
services.AddSingleton<IDomainKeyGenerator, TenantKeyGenerator>();
services.AddCacheOrchestrator(configuration);

// Option B — replace after:
services.AddCacheOrchestrator(configuration);
services.Replace(ServiceDescriptor.Singleton<IDomainKeyGenerator, TenantKeyGenerator>());
```

A minimal implementation that wraps the default:

```csharp
public sealed class TenantKeyGenerator : IDomainKeyGenerator
{
    private readonly DefaultDomainKeyGenerator _inner = new();

    public string Generate(DomainCacheOptions options, HttpContext httpContext)
    {
        var baseKey = _inner.Generate(options, httpContext);
        var tenantId = httpContext.User.FindFirst("tenant_id")?.Value ?? "anon";
        return $"{baseKey}|t:{tenantId}";
    }
}
```

> **Rules for custom keys**: must be deterministic (same inputs → same output), must not contain secrets (stored in Redis, may appear in logs), and should stay reasonably short.



## Entry options mapping

| Domain setting | FusionCache |
|----------------|-------------|
| `FusionCacheSoftTtl` | `Duration` (capped by hard TTL if soft &gt; hard) |
| `FusionCacheFailSafe` | `FailSafeMaxDuration` |
| `FusionCacheJitterSeconds` | `JitterMaxDuration` |
| `FusionCacheEagerRefreshRatio` | `EagerRefreshThreshold` |
| Factory soft/hard timeouts | `FactorySoftTimeout` / `FactoryHardTimeout` |
| Background flags | distributed + backplane background ops |

Resiliency (stampede protection, fail-safe stale serve) is provided by FusionCache itself.

## Results (for metrics / X-Cache)

| Result | Meaning |
|--------|---------|
| `Hit` | Served from cache, factory not run |
| `Miss` | Factory ran, value stored |
| `Stale` | Factory failed; fail-safe may serve stale |
| `Bypass` | Skipped (e.g. no-store) |
| `Off` | Domain Fusion disabled |

## Related

- [cache-keys.md](cache-keys.md) — key composition and Namespace  
- [configuration.md](configuration.md)  
- [invalidation.md](invalidation.md)  
- [architecture.md](architecture.md)  
- [output-cache.md](output-cache.md)  
