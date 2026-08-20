# FusionCache

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — concepts](guide/concepts.md). Catalog: [documentation index](README.md).

FusionCache stores **serializable objects** (JSON via System.Text.Json): L1 in memory, optional L2 in a distributed store, optional backplane. CacheOrchestrator scopes Fusion to the same **domain** as Output Cache and client headers.

## How Fusion finds the domain

`IDomainFusionCache.GetOrSetAsync` looks for domain options in this order:

1. The overload `GetOrSetAsync(http, domain, factory)` — same domain reuses the request snapshot; a **different** name replaces it (so `products` and `catalog` never share an entry).
2. Already on the request — usually set by Output Cache when you use `.CacheOutputWithDomain` or `[CacheDomain]`.
3. Endpoint metadata (the same attribute or extension), then the options are loaded.
4. If none of those apply, the factory runs uncached.

### With Output Cache

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("products");
```

```csharp
[CacheDomain("products")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromServices] IDomainFusionCache cache,
        CancellationToken cancellationToken)
    {
        var data = await cache.GetOrSetAsync(HttpContext, LoadAsync, cancellationToken);
        return Ok(data);
    }
}
```

### Fusion only

When the endpoint has no Output Cache domain, pass the name:

```csharp
await cache.GetOrSetAsync(http, "products", factory, cancellationToken);
```

Equivalent:

```csharp
domains.EnsureDomainOptions(http, "products");
await cache.GetOrSetAsync(http, factory, cancellationToken);
```

If you omit the domain:

- a Warning is logged (`FusionCache skipped: no domain resolved…`)
- metric `cache_orchestrator.fc.requests` is recorded with `domain=_`, `result=unresolved`
- `X-Cache` may show `data=unresolved` when Output Cache still writes headers

### Entity identity

```csharp
var product = await cache.GetOrSetEntityAsync(http, "store", "products", productId,
    ct => LoadProductAsync(productId, ct), cancellationToken);

await invalidator.InvalidateEntityAsync("store", "products", productId, cancellationToken);
```

The key includes kind and id. Tags are `domain:store`, `entity:store:products:42`, and `entitykind:store:products`. A domain is a policy group; `entityKind` is required because ids are not unique inside a domain.

- List or snapshot with `.CacheOutputWithDomain("x")` — `GetOrSetAsync(http, factory)`.
- Fusion only — `GetOrSetAsync(http, "x", factory)`.
- One row — `GetOrSetEntityAsync(http, domain, entityKind, resourceId, factory)`.
- One row when Output Cache already set the domain — `GetOrSetEntityAsync(http, entityKind, resourceId, factory)`.

See [domain-profiles.md](domain-profiles.md) and [invalidation.md](invalidation.md).

## When the factory runs uncached

- No domain on the request or metadata — disposition `Unresolved` (Warning + metric `result=unresolved`).
- `FusionCacheEnabled: false` — `Off`.
- Request `no-store` and `FusionCacheRespectNoStore` — `Bypass`.
- Auth bypass would fire **and** `FusionRespectAuthBypass` is `true` (the default) — `Bypass` (Debug: Fusion skipped due to auth bypass). Set `FusionRespectAuthBypass: false` for 2.1-like Fusion-under-Authorization.

## Cache key

`DefaultDomainKeyGenerator` builds a deterministic key (XxHash3).

With entity identity (`GetOrSetEntityAsync`):

```text
{domain}:{versionHex}:id:{entityKind}:{resourceId}:{hash}
```

Without a resource id (URL-shaped):

1. Route pattern and route values (or path)
2. Query string (tracking parameters omitted: `utm_*`, `gclid`, …)
3. `Accept-Encoding` if `FusionCacheVaryOnEncoding`
4. Scheme and host if `FusionCacheVaryOnPublicAddress`

The string includes **domain** and **Version**. Every entry is tagged `domain:{name}`.

Details: [cache-keys.md](cache-keys.md).

### Custom key generator

Implement `IDomainKeyGenerator` when you must vary on something the default ignores (tenant claim, extra header). Register it before `AddCacheOrchestrator` (`TryAddSingleton` will keep yours), or `Replace` it afterwards.

```csharp
using CacheOrchestrator.Vary;

services.AddSingleton<IDomainKeyGenerator, TenantKeyGenerator>();
services.AddCacheOrchestrator(configuration);
```

```csharp
using CacheOrchestrator.Vary;

public sealed class TenantKeyGenerator : IDomainKeyGenerator
{
    private readonly DefaultDomainKeyGenerator _inner;

    public TenantKeyGenerator(CacheVaryMaterializer materializer)
        => _inner = new DefaultDomainKeyGenerator(materializer);

    public string Generate(DomainCacheOptions options, HttpContext httpContext)
    {
        var baseKey = _inner.Generate(options, httpContext);
        var tenantId = httpContext.User.FindFirst("tenant_id")?.Value ?? "anon";
        return $"{baseKey}|t:{tenantId}";
    }
}
```

`new DefaultDomainKeyGenerator()` (no materializer) skips `ICacheVaryContributor`, Accept, and auth-user material. Prefer the DI constructor above, or replace `IDomainKeyGenerator` after `AddCacheOrchestrator`.

Keys must be deterministic, must not contain secrets (they land in Redis and in logs), and should stay short.

## Entry options

| Domain setting | FusionCache |
|----------------|-------------|
| `FusionCacheSoftTtl` | `Duration` (capped by hard TTL if soft is larger) |
| `FusionCacheFailSafe` | `FailSafeMaxDuration` |
| `FusionCacheJitterSeconds` | `JitterMaxDuration` |
| `FusionCacheEagerRefreshRatio` | `EagerRefreshThreshold` |
| Factory soft / hard timeouts | `FactorySoftTimeout` / `FactoryHardTimeout` |
| Background flags | distributed and backplane background work |

Stampede protection and fail-safe stale serve come from FusionCache itself.

## Results (`X-Cache` `data=` and `DataCacheResult`)

| Result | Meaning |
|--------|---------|
| `Hit` | Served from cache |
| `Miss` | Factory ran; value stored |
| `Stale` | Factory failed; fail-safe may serve stale |
| `Bypass` | Skipped (for example `no-store`, or auth bypass when `FusionRespectAuthBypass`) |
| `Off` | Fusion disabled for the domain |
| `Unresolved` | No domain resolved; factory ran uncached |

There is **no** `DataCacheResult.Fail` and **no** `data=fail` on `X-Cache`. A hard factory throw with no fail-safe value is recorded on the meter as `cache_orchestrator.fc.requests` `result=fail` (and `factory.duration`), then the exception propagates.

## Related

- [Guide — concepts](guide/concepts.md)
- [cache-keys.md](cache-keys.md)
- [configuration.md](configuration.md)
- [invalidation.md](invalidation.md)
- [architecture.md](architecture.md)
- [output-cache.md](output-cache.md)
