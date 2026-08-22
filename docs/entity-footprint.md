# Entity footprint examples

> **Reference.** Product overview: [root README](../README.md). Fusion overview: [fusion-cache.md](fusion-cache.md). Catalog: [documentation index](README.md).

Optional **entity identity** lives inside a **domain**. Domains remain the unit of configuration; `entityKind` + id (and related tags) refine per-row keys and invalidation. See [guide — concepts](guide/concepts.md).

This page is the cookbook. The Fusion reference keeps only the happy-path detail example and points here for the rest.

Each section starts with a small **situation** (what is stored, how it relates), then the code, then a short **Invalidation** example. Purging a tag removes **every** cache entry that carries that tag — so invalidating a category refreshes product details that `DependsOn` it; invalidating one tag id refreshes products that listed that tag.

## Model (short)

| Piece | Role |
|-------|------|
| Endpoint metadata | Domain + optional `entityKind` / `resourceRouteKey` (source of truth) |
| `GetOrSetEntityAsync` | Entity-shaped key; primary from the request |
| `GetOrSetEntitySetAsync` | URL-shaped key; member tags from `EntitySet` |
| `EntityCache` / `EntitySet` | Factory wrappers: `Members`, `DependsOn`, `Alias`, `Miss` |
| Tags | `domain:…`, `entity:…`, `entitykind:…` — same for OC (early + late) and Fusion |

Invalidation stays `InvalidateEntityAsync` / `InvalidateEntityKindAsync` / `InvalidateEntitiesAsync`.

**`EntityCache.Miss<T>()`** means: “for **this request’s primary id** (from `resourceRouteKey` / `SetEntityIdentity`), cache that there is **no value**.” The type argument `T` is only the CLR type of the payload (e.g. `Product` or `ProductDetailsDto`). It is **not** a miss of every product / every DTO — other ids keep their own entries. Returning `null` from `Func<CancellationToken, Task<T?>>` does the same for primary-only footprints.

---

## Detail (primary only)

**Situation.** A storefront exposes `GET /api/products/{id}`. The payload is one **Product** row (id, name, price, …). It does not embed other entities’ data that can change independently. When an admin updates product `42`, only that product’s cache entry should miss — not every product and not the whole `store` domain.

**Footprint:** primary `products:42`.

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

**Invalidation**

```csharp
// Admin saved product 42
await inv.InvalidateEntityAsync("store", "products", "42", cancellationToken);
// → only this detail entry misses; product 99 stays cached
```

---

## Negative cache

**Situation.** Same product detail route. Clients (or bots) often request ids that do not exist yet. Without caching the miss, every call hits the database. You still want a later **create** of that id to clear the cached “not found”.

**Footprint:** primary `products:{id}` even when the value is absent (e.g. request for product `42` → tag `entity:…:products:42`, not “all products”).

```csharp
// Assume [CacheDomain("store", "id", "products")] / CacheOutputWithDomain(..., resourceRouteKey: "id", entityKind: "products")
return await cache.GetOrSetEntityAsync(HttpContext, async ct =>
{
    Product? row = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
    // Miss<Product>() = no body for this id; T=Product is the value type only
    return row is null ? EntityCache.Miss<Product>() : EntityCache.Create(row);
}, ct);
```

Equivalent primary-only form without `EntityCache`: `return await cache.GetOrSetEntityAsync(HttpContext, ct => LoadProductAsync(id, ct), ct);` where `LoadProductAsync` returns `Product?` and `null` is the negative cache.

**Invalidation**

```csharp
// Product 42 was created (or you know the negative entry is wrong)
await inv.InvalidateEntityAsync("store", "products", "42", cancellationToken);
// → the cached "not found" for 42 is gone; other missing ids keep their negative entries
```

---

## References / expanded graph

**Situation.** `GET /api/products/{id}` returns a **product detail** screen model, not a bare row:

| Field | Relationship |
|-------|----------------|
| Product itself | Primary entity (`products`) |
| Category name / path | Many products → **one** Category (`CategoryId`) |
| Brand name / logo | Many products → **one** Brand (`BrandId`) |
| Labels (sale, eco, …) | Product → **0..n** Tags (`TagIds` / join table) |

The JSON denormalizes category, brand, and tag labels into the response. If marketing renames a category, or a tag is removed from the product, the cached detail is stale even though the `Product` row’s price never changed.

**Footprint:** primary `products:{id}` + `DependsOn` each related id (`categories`, `brands`, and every `tags:{tagId}`).

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
[HttpGet("{id:int}")]
public async Task<ActionResult<ProductDetailsDto>> GetDetails(
    int id,
    IDomainFusionCache cache,
    CancellationToken cancellationToken)
{
    var details = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        ProductDetailsDto? row = await LoadProductDetailsAsync(id, ct);
        if (row is null)
            return EntityCache.Miss<ProductDetailsDto>(); // no DTO for this product id

        return EntityCache.Create(row)
            .DependsOn("categories", row.CategoryId.ToString())
            .DependsOn("brands", row.BrandId.ToString())
            .DependsOn("tags", row.TagIds.Select(t => t.ToString()));
    }, cancellationToken);
    return details is null ? NotFound() : Ok(details);
}
```

**Invalidation**

```csharp
// Category 7 renamed → every product detail that DependsOn categories:7 misses
await inv.InvalidateEntityAsync("store", "categories", "7", cancellationToken);

// Tag 3 updated → product details that listed tag 3 miss (others unaffected)
await inv.InvalidateEntityAsync("store", "tags", "3", cancellationToken);

// Brand 9 updated
await inv.InvalidateEntityAsync("store", "brands", "9", cancellationToken);
```

---

## Aggregate (root + children)

**Situation.** `GET /api/orders/{id}` returns one **Order** aggregate for checkout/history:

| Piece | Relationship |
|-------|----------------|
| Order header | Primary (`orders`) — status, totals, timestamps |
| Customer | Order → **one** Customer (`CustomerId`); name may be copied into the DTO |
| Lines | Order → **1..n** OrderLine children (line id, qty, unit price) |
| Products on lines | Each line → **one** Product (`ProductId`); title/SKU often denormalized onto the line |

The cached payload is the whole aggregate. Changing line `100`, reassigning the customer, or updating a product title shown on a line must invalidate this order entry — not only edits to the order header row.

**Footprint:** primary `orders:{id}` + `Members` each `order-lines:{lineId}` + `DependsOn` `customers:{id}` and each distinct `products:{id}` from the lines.

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "orders")]
[HttpGet("{id:int}")]
public async Task<ActionResult<OrderDto>> GetOrder(
    int id,
    IDomainFusionCache cache,
    CancellationToken cancellationToken)
{
    var order = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        Order? entity = await LoadOrderByIdAsync(id, ct);
        if (entity is null)
            return EntityCache.Miss<OrderDto>(); // no order aggregate for this id

        return EntityCache.Create(OrderDto.From(entity))
            .Members("order-lines", entity.Lines.Select(l => l.Id.ToString()))
            .DependsOn("customers", entity.CustomerId.ToString())
            .DependsOn("products", entity.Lines.Select(l => l.ProductId.ToString()).Distinct());
    }, cancellationToken);
    return order is null ? NotFound() : Ok(order);
}
```

`Members` and `DependsOn` produce the same on-wire tags; the names document whether the ref is part of the aggregate or a related input.

**Invalidation**

```csharp
// Line 100 edited → order aggregates that Members order-lines:100 miss
await inv.InvalidateEntityAsync("store", "order-lines", "100", cancellationToken);

// Customer 9 renamed → orders that DependsOn customers:9 miss
await inv.InvalidateEntityAsync("store", "customers", "9", cancellationToken);

// Product 42 title changed → orders whose lines DependsOn products:42 miss
await inv.InvalidateEntityAsync("store", "products", "42", cancellationToken);
```

---

## List / search / page

**Situation.** `GET /api/products?page=2&pageSize=20` returns a **page of Product summaries** (ids currently on that page). There is no single primary product id for the response. If product `15` on that page is renamed, this page’s cache entry should miss; product `99` on another page should not force this page to reload.

**Footprint:** URL-shaped key + `entitykind:products` + `Members` for each product id on the page.

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

**Invalidation**

```csharp
// Product 15 on page 2 was updated → that page’s list entry misses
await inv.InvalidateEntityAsync("store", "products", "15", cancellationToken);
// Product 99 (not on this page) does not evict this entry
```

---

## Filtered view

**Situation.** Same product list, but filtered: `GET /api/products?categoryId=7` means “products in category 7”. Two things can stale the response:

1. Any **product** currently in the result set changes.
2. The **category** itself changes (name used in a header), or products are moved in/out of category 7 (membership). Tagging the filter category covers (2) when you also invalidate that category; member tags cover (1).

**Footprint:** URL key + members for each product on the page + `DependsOn` `categories:7` when the filter is present.

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

**Invalidation**

```csharp
// Product on the page changed
await inv.InvalidateEntityAsync("store", "products", "15", cancellationToken);

// Category 7 metadata / membership changed → filtered lists that DependsOn categories:7 miss
await inv.InvalidateEntityAsync("store", "categories", "7", cancellationToken);
```

---

## Nested resource

**Situation.** Reviews are their own entity, nested under a product route: `GET /api/products/{id}/reviews`.

| Entity | Role |
|--------|------|
| Product `{id}` | Parent in the URL; reviews belong to it |
| Review | Many rows (`ReviewId`, body, rating) returned as the list |

The response is a **collection of reviews**, not the product document. Updating review `501` or deleting the parent product should refresh this list; another product’s reviews should not.

**Footprint:** URL-shaped key (path includes product id) + `Members` each `reviews:{id}` + `DependsOn` `products:{id}`.

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

**Invalidation**

```csharp
// Review 501 edited → nested lists that Members reviews:501 miss
await inv.InvalidateEntityAsync("store", "reviews", "501", cancellationToken);

// Parent product 42 deleted/updated → lists that DependsOn products:42 miss
await inv.InvalidateEntityAsync("store", "products", "42", cancellationToken);
```

---

## Batch `?ids=`

**Situation.** A BFF or mobile client already knows several product ids and calls `GET /api/products/batch?ids=1&ids=2&ids=5` to hydrate cards in one round-trip. The response is that set of products (whatever exists). Changing product `2` should invalidate this batch entry; product `9` (not in the response) should not.

**Footprint:** URL/query key + `Members` for each product actually returned.

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

**Invalidation**

```csharp
// Product 2 in the batch changed → this batch response misses
await inv.InvalidateEntityAsync("store", "products", "2", cancellationToken);
```

---

## Derived / computed

**Situation.** `GET /api/products/{id}/availability` does not return the Product row. It returns a **computed** `AvailabilityDto` (in stock?, eta, warehouse label) built from:

| Input | Role |
|-------|------|
| Product `{id}` | Route identity / primary for the endpoint |
| Stock row | Quantity / reservation state (`StockId`) |
| Warehouse row | Location / cutoff times (`WarehouseId`) |

Editors often change stock or warehouse data without touching the product master. The availability cache must still miss.

**Footprint:** primary `products:{id}` + `DependsOn` `stock:{id}` and `warehouses:{id}`.

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
[HttpGet("{id:int}/availability")]
public async Task<ActionResult<AvailabilityDto>> Availability(
    int id,
    IDomainFusionCache cache,
    CancellationToken cancellationToken)
{
    var dto = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        AvailabilityDto? a = await LoadProductAvailabilityAsync(id, ct);
        if (a is null)
            return EntityCache.Miss<AvailabilityDto>(); // no availability payload for this product id

        return EntityCache.Create(a)
            .DependsOn("stock", a.StockId.ToString())
            .DependsOn("warehouses", a.WarehouseId.ToString());
    }, cancellationToken);
    return dto is null ? NotFound() : Ok(dto);
}
```

**Invalidation**

```csharp
// Stock row changed → availability entries that DependsOn that stock miss
await inv.InvalidateEntityAsync("store", "stock", stockId, cancellationToken);

// Warehouse cutoff changed
await inv.InvalidateEntityAsync("store", "warehouses", warehouseId, cancellationToken);
```

---

## Alternate key (alias)

**Situation.** Internally every Product has a numeric id (`42`). Warehouse / ERP systems only send **SKU** strings (`ABC-42`). HTTP still uses `/products/{id}`, but invalidation from the warehouse pipeline arrives as “SKU changed”. You want one cache entry reachable by both identities.

**Footprint:** primary `products:42` + `Alias` `products-by-sku:ABC-42` (same entry, extra tag).

```csharp
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
[HttpGet("{id:int}")]
public async Task<ActionResult<Product>> Get(
    int id,
    IDomainFusionCache cache,
    CancellationToken cancellationToken)
{
    var product = await cache.GetOrSetEntityAsync(HttpContext, async ct =>
    {
        Product? row = await LoadProductByIdAsync(id, ct);
        if (row is null)
            return EntityCache.Miss<Product>(); // no product for this id

        return EntityCache.Create(row)
            .Alias("products-by-sku", row.Sku);
    }, cancellationToken);
    return product is null ? NotFound() : Ok(product);
}
```

**Invalidation**

```csharp
// By internal id (HTTP / admin UI)
await inv.InvalidateEntityAsync("store", "products", "42", cancellationToken);

// By SKU (warehouse pipeline) — same cache entry, via Alias tag
await inv.InvalidateEntityAsync("store", "products-by-sku", "ABC-42", cancellationToken);
```

---

## Dashboard / composite widget

**Situation.** The home page loads a **StorefrontWidget** that is not one database table. It composes:

| Slice | Source |
|-------|--------|
| Featured product cards | Several Product ids |
| Hero banner category | One Category id (title/image) |
| Promo ribbons | 0..n Promotion ids |

Any of those underlying rows changing should refresh the widget without bumping `Version` for the entire `store` domain. The route has no natural `{id}`, so you invent a stable synthetic identity (`dashboard` / `storefront`).

**Footprint:** primary `dashboard:storefront` + `Members` featured products + `DependsOn` hero category and each promotion.

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

**Invalidation**

```csharp
await inv.InvalidateEntityAsync("store", "products", featuredProductId, cancellationToken);
await inv.InvalidateEntityAsync("store", "categories", heroCategoryId, cancellationToken);
await inv.InvalidateEntityAsync("store", "promotions", promoId, cancellationToken);
// any of the above refreshes the storefront widget entry
```

---

## Fusion-only (no Output Cache entity metadata)

**Situation.** A worker, gRPC handler, or Minimal endpoint without `.CacheOutputWithDomain(..., resourceRouteKey, entityKind)` still wants Fusion entries shaped and tagged like product `42`, so `InvalidateEntityAsync` works the same way.

**Footprint:** same as detail — you stamp identity yourself, then call `GetOrSetEntityAsync`.

```csharp
domains.EnsureDomainOptions(http, "store");
cache.SetEntityIdentity(http, "products", id);
var product = await cache.GetOrSetEntityAsync(http, ct => LoadProductByIdAsync(id, ct), ct);
```

**Invalidation** — same as detail:

```csharp
await inv.InvalidateEntityAsync("store", "products", id, cancellationToken);
```

---

## Snapshot list without member tags

**Situation.** A monthly **catalog dump** or map-tile set is versioned as a whole. Individual row edits are not invalidated one-by-one; you bump domain `Version` or wait for TTL. Tagging every product id would be noise.

**Footprint:** `domain:catalog` only (no entity tags).

```csharp
.CacheOutputWithDomain("catalog");
var data = await cache.GetOrSetAsync(http, LoadCatalogAsync, ct);
```

No `entityKind` — only `domain:` tags.

**Invalidation** — not per row; refresh the generation or the whole domain:

```csharp
// bump Version in config / Admin, or:
await inv.InvalidateDomainAsync("catalog", cancellationToken);
```

---

## Related

- [fusion-cache.md](fusion-cache.md) — API overview and obsolete overload migration
- [invalidation.md](invalidation.md) — tag purge wiring
- [cache-keys.md](cache-keys.md) — entity vs URL key shapes
- [ef-core-invalidation.md](ef-core-invalidation.md) — SaveChanges → same tags
- [domain-profiles.md](domain-profiles.md) — snapshot vs CRUD domains
