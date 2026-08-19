# Domain profiles: snapshot and dynamic

How fresh data enters the cache, and how to configure two common worlds:

1. **Snapshot** (map tiles, a monthly extract) — content is frozen until a planned cutover.
2. **Dynamic** (a product detail) — individual records change under the same `Version`.

## Model

A **domain** is a named package of rules (TTLs, client headers, which Fusion instance). Each response or Fusion object has its own **key**. `Version` is a **generation stamp** for the whole package, not the version of one product.

- **Domain** — endpoints that share rules (`maps-osm`, `product-detail`).
- **Version** — generation (`2026-08`, `v1`). Changing it opens a new key space.
- **TTL** — how long one entry may live before the factory runs again.
- **Tag invalidation** — an explicit delete: a domain, a kind, or one id (`entity:store:products:42`).
- **ETag** — hint for browsers and CDNs. See [ETag modes](#etag-modes).

### Three ways a request becomes a MISS (fresh data from the DB)

```text
Same Version, same URL
        │
        ├─► TTL expired?           → MISS → factory/DB
        ├─► Tag purged?            → MISS → factory/DB
        └─► Version bumped?        → new key → MISS → factory/DB
```

If a product changes under the same Version and you neither wait for TTL nor invalidate, the cache keeps serving the old body. That is caching working as designed.

---

## Snapshot profile (OSM tiles, batch datasets)

**Rules**

- Within one `Version`, content **must not** change.  
- Cutover = bump `Version` (+ often [Client Cache Schedule](client-cache-schedule.md)).  
- Long server and client TTLs are desirable.  
- ETag = domain generation (`ETagMode: Version`) is intentional: every tile shares the same generation validator.

### Example configuration

```json
"Domains": {
  "osm-tiles": {
    "Version": "2026-08",
    "ETagMode": "Version",
    "ClientCacheability": "Public",
    "ClientTtlSeconds": 2592000,
    "ClientTtlMinSeconds": 900,
    "ScheduledUpdateUtc": "2026-09-01T00:00:00Z",
    "ClientMustRevalidateNearUpdate": true,
    "OutputCacheTtlSeconds": 604800,
    "FusionCacheSoftTtlSeconds": 2592000,
    "FusionCacheHardTtlSeconds": 5184000,
    "FusionCacheFailSafeSeconds": 7776000
  }
}
```

### Endpoint

```csharp
app.MapGet("/tiles/{z}/{x}/{y}", async (HttpContext http, IDomainFusionCache cache, int z, int x, int y) =>
{
    var tile = await cache.GetOrSetAsync(http, async ct => await LoadTileAsync(z, x, y, ct));
    return Results.Bytes(tile, "image/png");
})
.CacheOutputWithDomain("osm-tiles");
```

At cutover: set `Version` to `2026-09`, deploy data, set next `ScheduledUpdateUtc`.  
No per-tile invalidation is required.

---

## Dynamic / CRUD profile (product detail)

**Rules**

- `Version` stays stable (`"1"`) most of the time.  
- Individual rows change → **short TTLs** and/or **entity invalidation**.  
- Use `GetOrSetEntityAsync(http, domain, entityKind, resourceId, factory)` so entries are tagged `entity:{domain}:{entityKind}:{id}`.  
- On admin save: `InvalidateEntityAsync(domain, entityKind, id)` — **same Version**, new body on next request.  
- Prefer shorter client cache (or `Private` / low max-age).  
- `ETagMode: Resource` gives a distinct ETag per product URL/id (still generation-bound; for very short client TTL, `None` is fine).

### Example configuration

```json
"Domains": {
  "product-detail": {
    "Version": "1",
    "ETagMode": "Resource",
    "ClientCacheability": "Public",
    "ClientTtlSeconds": 15,
    "ClientTtlMinSeconds": 15,
    "OutputCacheTtlSeconds": 30,
    "FusionCacheSoftTtlSeconds": 60,
    "FusionCacheHardTtlSeconds": 300,
    "FusionCacheFailSafeSeconds": 600
  }
}
```

### Endpoint + invalidation

```csharp
// GET — cache per product id
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainFusionCache cache) =>
{
    var product = await cache.GetOrSetEntityAsync(http, "store", "products", id, async ct =>
        await db.Products.FindAsync([id], ct));
    return Results.Json(product);
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");

// PUT — write then purge only this product (Version stays "1")
app.MapPut("/api/products/{id}", async (string id, ProductDto dto, ICacheOrchestratorInvalidator inv) =>
{
    await db.SaveAsync(id, dto);
    await inv.InvalidateEntityAsync("store", "products", id);
    return Results.NoContent();
});
```

Flow:

```text
t0  GET /products/42  → MISS → DB (price 10) → store OC+FC, tags domain + entity:store:products:42
t1  Admin sets price 12, calls InvalidateEntityAsync("store", "products", "42")
t2  GET /products/42  → MISS → DB (price 12)
    GET /products/99  → still HIT (other entity)
```

---

## ETag modes

| Mode | Header | Use when |
|------|--------|----------|
| `Version` (default) | One weak ETag from domain `Version` for all URLs | Snapshot / tiles |
| `Resource` | Weak ETag from `Version` + resource id (or path) | CRUD with distinct validators per URL |
| `None` | No `ETag` | Short TTL APIs; avoid client revalidation surprises |

ETag does **not** drive server Output Cache lookup. OC keys are per URL + vary + `data-version`.  
ETag is for **browser/CDN** conditional requests after client `max-age` expires.

---

## Choosing a profile

| Question | Snapshot | Dynamic |
|----------|----------|---------|
| Can one URL’s body change without a release? | No | Yes |
| Primary freshness tool | Bump `Version` | TTL + `InvalidateEntityAsync` |
| Client `max-age` | Long + schedule | Short |
| `GetOrSetAsync` resource id | Optional | Recommended |
| `resourceRouteKey` on OC | Optional | Recommended for entity OC purge |

You can mix both profiles in one app (different domains).

---

## Authenticated traffic (auth bypass)

Default: **any** authenticated user or `Authorization` header → Output Cache **off**, client cache **blocked** (`AuthBypassMode: AuthenticatedOrAuthorization`). Prefer `AuthBypassMode` over obsolete `BypassWhenAuthenticated`. Full matrix: [vary.md](vary.md).
Safe default for mixed public/private APIs.

| Goal | Settings |
|------|----------|
| Keep default safety | omit flags (`BypassWhenAuthenticated: true`) |
| Cache private per-user pages | `BypassWhenAuthenticated: false`, `VaryOutputCacheByUser: true`, `ClientCacheability: Private` |
| Public assets with API key | `BypassWhenAuthenticated: false`, `VaryOutputCacheByUser: false`, `ClientCacheability: Public` |

Full examples: [output-cache.md](output-cache.md#authenticated-caching-optional).

---

## Related

- [invalidation.md](invalidation.md) — Version, domain, entity, custom tags  
- [configuration.md](configuration.md) — full property list  
- [client-cache-schedule.md](client-cache-schedule.md) — cutover-friendly client max-age  
- [fusion-cache.md](fusion-cache.md) — `GetOrSetAsync` overloads  
