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
- `X-Cache` may show `fc=unresolved; fa=run` when Output Cache still writes headers

### Entity identity (EntityFootprint)

Endpoint metadata owns domain + primary kind/id. Fusion consumes that identity (no repeated strings on the happy path).

```csharp
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainFusionCache cache, CancellationToken cancellationToken) =>
{
    var product = await cache.GetOrSetEntityAsync(
        http,
        ct => LoadProductAsync(id, ct),
        cancellationToken);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");

await invalidator.InvalidateEntityAsync("store", "products", productId, cancellationToken);
```

Tags for a detail entry: `domain:store`, `entity:store:products:42`, `entitykind:store:products`. Extend with `EntityCache` / `EntitySet` for members, `DependsOn`, and aliases (same tag prefixes).

All of the following use the same footprint model (no separate subsystems). Lookup key is either entity-shaped (detail) or URL-shaped (collections / composites).

| Scenario | Lookup | Footprint |
|----------|--------|-----------|
| Detail / projection | entity | primary |
| Negative cache (null / 404) | entity | primary (`EntityCache.Miss` or null) |
| References / `$expand` | entity | primary + `DependsOn` |
| Aggregate (order + lines) | entity | primary + `Members` + `DependsOn` |
| Nested (`/products/{id}/reviews`) | URL | members + `DependsOn` parent |
| List / search / page | URL | `entitykind` + members |
| Filtered view | URL | members + `DependsOn` filter |
| Batch `?ids=` | URL | members from result (or from the id list) |
| Derived / computed | entity or URL | `DependsOn` inputs |
| Alternate key (SKU ↔ id) | entity | primary + `Alias` |
| Dashboard / composite widget | URL | mixed members / `DependsOn` |
| Snapshot without member tags | URL | domain only (`GetOrSetAsync`) |
| Fusion-only entity | entity | `SetEntityIdentity` then `GetOrSetEntityAsync` |

See [domain-profiles.md](domain-profiles.md) and [invalidation.md](invalidation.md).

#### Footprint examples

**List + filter dependency**

```csharp
[CacheDomain("store", entityKind: "products")]
[HttpGet]
public async Task<ActionResult<IReadOnlyList<Product>>> List(int? categoryId, IDomainFusionCache cache, ...)
{
    var products = await cache.GetOrSetEntitySetAsync(HttpContext, async ct =>
    {
        var rows = await QueryAsync(categoryId, ct);
        var set = EntitySet.Create(rows, p => p.Id.ToString());
        return categoryId is int cid ? set.DependsOn("categories", cid.ToString()) : set;
    }, ct);
    return Ok(products);
}
```

**References**

```csharp
return await cache.GetOrSetEntityAsync(HttpContext, async ct =>
{
    var row = await LoadAsync(ct);
    if (row is null) return EntityCache.Miss<ProductDetailsDto>();
    return EntityCache.Create(row)
        .DependsOn("categories", row.CategoryId.ToString())
        .DependsOn("brands", row.BrandId.ToString());
}, ct);
```

**Aggregate**

```csharp
return await cache.GetOrSetEntityAsync(HttpContext, async ct =>
{
    var order = await LoadOrderAsync(ct);
    if (order is null) return EntityCache.Miss<OrderDto>();
    return EntityCache.Create(OrderDto.From(order))
        .Members("order-lines", order.Lines.Select(l => l.Id.ToString()))
        .DependsOn("customers", order.CustomerId.ToString());
}, ct);
```

**Negative cache** (miss still tagged with primary so a later create can purge it)

```csharp
return await cache.GetOrSetEntityAsync(HttpContext, async ct =>
{
    var row = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    return row is null ? EntityCache.Miss<Product>() : EntityCache.Create(row);
}, ct);
// plain null from Func<Task<T?>> is equivalent for primary-only footprints
```

**Nested resource** (`GET /products/{id}/reviews`)

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
[HttpGet("{id:int}/reviews")]
public async Task<ActionResult<IReadOnlyList<Review>>> Reviews(int id, IDomainFusionCache cache, ...)
{
    var reviews = await cache.GetOrSetEntitySetAsync(HttpContext, async ct =>
    {
        var rows = await db.Reviews.AsNoTracking().Where(r => r.ProductId == id).ToListAsync(ct);
        return EntitySet.Create(rows, "reviews", r => r.Id.ToString())
            .DependsOn("products", id.ToString());
    }, ct);
    return Ok(reviews);
}
```

**Batch `?ids=`**

```csharp
[CacheDomain("store", entityKind: "products")]
[HttpGet("batch")]
public async Task<ActionResult<IReadOnlyList<Product>>> Batch([FromQuery] string[] ids, IDomainFusionCache cache, ...)
{
    var products = await cache.GetOrSetEntitySetAsync(HttpContext, async ct =>
    {
        var rows = await LoadManyAsync(ids, ct);
        return EntitySet.Create(rows, p => p.Id.ToString());
        // members come from the result; unknown ids simply omit a member tag
    }, ct);
    return Ok(products);
}
```

**Derived / computed** (availability from stock + warehouse)

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
public async Task<ActionResult<AvailabilityDto>> Availability(int id, IDomainFusionCache cache, ...)
{
    var dto = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        var a = await LoadAvailabilityAsync(id, ct);
        if (a is null) return EntityCache.Miss<AvailabilityDto>();
        return EntityCache.Create(a)
            .DependsOn("stock", a.StockId.ToString())
            .DependsOn("warehouses", a.WarehouseId.ToString());
    }, ct);
    return dto is null ? NotFound() : Ok(dto);
}
```

**Alternate key (alias)**

```csharp
return await cache.GetOrSetEntityAsync(HttpContext, async ct =>
{
    var product = await LoadByIdAsync(ct);
    if (product is null) return EntityCache.Miss<Product>();
    return EntityCache.Create(product)
        .Alias("products-by-sku", product.Sku);
}, ct);
// InvalidateEntityAsync("store", "products-by-sku", sku) also purges this entry
```

**Dashboard / composite widget**

Use a stable synthetic identity (or a route id) plus `DependsOn` / `Members` for everything the widget embeds:

```csharp
[CacheDomain("store")]
[HttpGet("/api/home/widgets/storefront")]
public async Task<ActionResult<StorefrontWidget>> Storefront(HttpContext http, IDomainFusionCache cache, ...)
{
    cache.SetEntityIdentity(http, "dashboard", "storefront");
    var widget = await cache.GetOrSetEntityAsync(http, async ct =>
    {
        var w = await BuildWidgetAsync(ct);
        return EntityCache.Create(w)
            .Members("products", w.FeaturedProductIds.Select(id => id.ToString()))
            .DependsOn("categories", w.HeroCategoryId.ToString())
            .DependsOn("promotions", w.PromotionIds.Select(id => id.ToString()));
    }, ct);
    return Ok(widget);
}
```

#### Migration (obsolete overloads)

`GetOrSetEntityAsync(http, entityKind, resourceId, …)` and `GetOrSetEntityAsync(http, domain, entityKind, resourceId, …)` are obsolete. Prefer endpoint identity or `SetEntityIdentity`. They remain as thin wrappers until the next major.

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

## Results (`X-Cache` `fc=` and `DataCacheResult`)

| Result | Meaning |
|--------|---------|
| `Hit` | Served from cache |
| `Miss` | Factory ran; value stored |
| `Stale` | Factory failed; fail-safe may serve stale |
| `Bypass` | Skipped (for example `no-store`, or auth bypass when `FusionRespectAuthBypass`) |
| `Off` | Fusion disabled for the domain. The factory still runs and counts as a factory invocation (FA run). |
| `Unresolved` | No domain resolved; factory ran uncached (also a factory invocation). |

There is **no** `DataCacheResult.Fail` and **no** `fc=fail` on `X-Cache`. A hard factory throw with no fail-safe value is recorded on the meter as `cache_orchestrator.fc.requests` `result=fail` (and `factory.duration`), then the exception propagates.

When `fc` is present and is not `hit`, `X-Cache` also includes `fa=run`. That is the same factory-invocation set as Admin FA run (`miss` / `stale` / `bypass` / `off` / `unresolved`). OC `hit` omits `fc` and `fa`.

Admin Console exclusive pipeline mix is **OC hit + FC hit (fresh) + FA run**. FA run is factory-callback share of requests (including `off` / `unresolved` / bypass-with-factory / miss / stale). **FC stale %** is an overlay on requests, not a fourth mix segment. Layer `bypass` remains auth / no-store skip, not “caching disabled”.

## Related

- [Guide — concepts](guide/concepts.md)
- [cache-keys.md](cache-keys.md)
- [configuration.md](configuration.md)
- [invalidation.md](invalidation.md)
- [architecture.md](architecture.md)
- [output-cache.md](output-cache.md)
