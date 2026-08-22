# Entity footprint examples

> **Reference.** Product overview: [root README](../README.md). Fusion overview: [fusion-cache.md](fusion-cache.md). Catalog: [documentation index](README.md).

Optional **entity identity** lives inside a **domain**. Domains remain the unit of configuration; `entityKind` + id (and related tags) refine per-row keys and invalidation. See [guide — concepts](guide/concepts.md).

This page is the cookbook. The Fusion reference keeps only the happy-path detail example and points here for the rest.

## Model (short)

| Piece | Role |
|-------|------|
| Endpoint metadata | Domain + optional `entityKind` / `resourceRouteKey` (source of truth) |
| `GetOrSetEntityAsync` | Entity-shaped key; primary from the request |
| `GetOrSetEntitySetAsync` | URL-shaped key; member tags from `EntitySet` |
| `EntityCache` / `EntitySet` | Factory wrappers: `Members`, `DependsOn`, `Alias`, `Miss` |
| Tags | `domain:…`, `entity:…`, `entitykind:…` — same for OC (early + late) and Fusion |

Invalidation stays `InvalidateEntityAsync` / `InvalidateEntityKindAsync` / `InvalidateEntitiesAsync`.

---

## Detail (primary only)

**Use case:** One product (or any row) by id. When that row changes, only that cache entry should miss — not the whole domain.

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
```

MVC: `[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]` and the same `GetOrSetEntityAsync(HttpContext, factory)` call.

A thin projection / DTO is the same pattern — still one primary identity.

---

## Negative cache

**Use case:** Callers repeatedly ask for a missing id. You want to cache the “not found” outcome briefly, and still purge it when that id is created later.

```csharp
return await cache.GetOrSetEntityAsync(HttpContext, async ct =>
{
    var row = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    return row is null ? EntityCache.Miss<Product>() : EntityCache.Create(row);
}, ct);
```

Returning `null` from `Func<CancellationToken, Task<T?>>` is equivalent when the footprint is primary-only. The entry stays tagged with the primary entity so create/update invalidation can evict it.

---

## References / expanded graph

**Use case:** Product detail embeds category or brand names. When the category is renamed, the product detail cache must miss even if the product row did not change.

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
public async Task<ActionResult<ProductDetailsDto>> GetDetails(...)
{
    var details = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        var row = await LoadAsync(ct);
        if (row is null) return EntityCache.Miss<ProductDetailsDto>();
        return EntityCache.Create(row)
            .DependsOn("categories", row.CategoryId.ToString())
            .DependsOn("brands", row.BrandId.ToString())
            .DependsOn("tags", row.TagIds.Select(t => t.ToString()));
    }, ct);
    return details is null ? NotFound() : Ok(details);
}
```

---

## Aggregate (root + children)

**Use case:** Order header plus lines is one cached payload. Changing a line, the customer, or a referenced product should invalidate that order entry.

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "orders")]
public async Task<ActionResult<OrderDto>> GetOrder(...)
{
    var order = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        var entity = await LoadOrderAsync(ct);
        if (entity is null) return EntityCache.Miss<OrderDto>();
        return EntityCache.Create(OrderDto.From(entity))
            .Members("order-lines", entity.Lines.Select(l => l.Id.ToString()))
            .DependsOn("customers", entity.CustomerId.ToString())
            .DependsOn("products", entity.Lines.Select(l => l.ProductId.ToString()).Distinct());
    }, ct);
    return order is null ? NotFound() : Ok(order);
}
```

`Members` and `DependsOn` produce the same on-wire tags; the names document whether the ref is part of the aggregate or a related input.

---

## List / search / page

**Use case:** A paged or sorted product list. When any product on that page changes, the list entry should miss (not wait for list TTL alone).

```csharp
[CacheDomain("store", entityKind: "products")]
[HttpGet]
public async Task<ActionResult<IReadOnlyList<Product>>> List(IDomainFusionCache cache, ...)
{
    var products = await cache.GetOrSetEntitySetAsync(HttpContext, async ct =>
    {
        var rows = await QueryPageAsync(ct);
        return EntitySet.Create(rows, p => p.Id.ToString());
    }, ct);
    return Ok(products);
}
```

Lookup key is URL/query-shaped. Tags include `entitykind:store:products` plus each member id.

---

## Filtered view

**Use case:** `GET /products?categoryId=7`. Changing category 7’s metadata (or membership) should invalidate this filtered list, as should edits to products that appear on it.

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

---

## Nested resource

**Use case:** `GET /products/{id}/reviews` — reviews for one product. Updating the product or any review on the page should refresh this collection.

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

`GetOrSetEntitySetAsync` uses a URL-shaped key (parent route id does not force the entity key shape).

---

## Batch `?ids=`

**Use case:** Client asks for several known ids in one call. Any of those products changing should invalidate this batch response.

```csharp
[CacheDomain("store", entityKind: "products")]
[HttpGet("batch")]
public async Task<ActionResult<IReadOnlyList<Product>>> Batch([FromQuery] string[] ids, IDomainFusionCache cache, ...)
{
    var products = await cache.GetOrSetEntitySetAsync(HttpContext, async ct =>
    {
        var rows = await LoadManyAsync(ids, ct);
        return EntitySet.Create(rows, p => p.Id.ToString());
    }, ct);
    return Ok(products);
}
```

Member tags come from the factory result. Unknown ids simply omit a member tag. (There is no automatic early tagging from the query string; late OC tags still apply after the Fusion factory runs.)

---

## Derived / computed

**Use case:** Availability is computed from stock and warehouse rows. Changing stock should invalidate availability even if the product row is unchanged.

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

---

## Alternate key (alias)

**Use case:** Cache and invalidate by internal id, but also allow purge by SKU when the warehouse system only knows SKUs.

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
public async Task<ActionResult<Product>> Get(...)
{
    var product = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        var row = await LoadByIdAsync(ct);
        if (row is null) return EntityCache.Miss<Product>();
        return EntityCache.Create(row)
            .Alias("products-by-sku", row.Sku);
    }, ct);
    return product is null ? NotFound() : Ok(product);
}
```

`InvalidateEntityAsync("store", "products-by-sku", sku)` purges the same entry as invalidating the primary id.

---

## Dashboard / composite widget

**Use case:** Home “storefront” widget embeds featured products, a hero category, and promotions. Any of those changing should refresh the widget without a domain-wide Version bump.

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

Synthetic identity (`dashboard` / `storefront`) gives a stable entity key when the route has no natural resource id.

---

## Fusion-only (no Output Cache entity metadata)

**Use case:** Background or non-OC endpoint that still wants entity-shaped Fusion entries and tag purge.

```csharp
domains.EnsureDomainOptions(http, "store");
cache.SetEntityIdentity(http, "products", id);
var product = await cache.GetOrSetEntityAsync(http, ct => LoadAsync(ct), ct);
```

---

## Snapshot list without member tags

**Use case:** Catalog snapshot or tiles where you refresh by Version or domain TTL, not per-row purge.

```csharp
.CacheOutputWithDomain("catalog");
var data = await cache.GetOrSetAsync(http, LoadCatalogAsync, ct);
```

No `entityKind` — only `domain:` tags.

---

## Related

- [fusion-cache.md](fusion-cache.md) — API overview and obsolete overload migration
- [invalidation.md](invalidation.md) — tag purge wiring
- [cache-keys.md](cache-keys.md) — entity vs URL key shapes
- [ef-core-invalidation.md](ef-core-invalidation.md) — SaveChanges → same tags
- [domain-profiles.md](domain-profiles.md) — snapshot vs CRUD domains
