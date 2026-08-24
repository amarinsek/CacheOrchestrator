# Domain profiles: snapshot and dynamic

> **Guide.** Product overview: [root README](../README.md). Orientation: [Guide](guide/README.md). Catalog: [documentation index](README.md).

How fresh data enters the cache, and how to configure two common worlds:

1. **Snapshot** (map tiles, a monthly extract) — content is frozen until a planned cutover.
2. **Dynamic** (a product detail) — individual records change under the same `Version`.

## Model

A **domain** is a named package of rules (TTLs, client headers, which data-cache instance). Each response or data-cache object has its own **key**. `Version` is a **generation stamp** for the whole package, not the version of one product.

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
    "DataCache": {
      "Ttl": "30.00:00:00"
    },
    "OutputCache": {
      "Ttl": "7.00:00:00",
      "ETagMode": "Version"
    },
    "ClientCache": {
      "Cacheability": "Public",
      "Ttl": "30.00:00:00",
      "TtlMin": "00:15:00",
      "ScheduledUpdateUtc": "2026-09-01T00:00:00Z",
      "MustRevalidateNearUpdate": true
    },
    "FusionCache": {
      "HardTtl": "60.00:00:00",
      "FailSafe": "90.00:00:00"
    }
  }
}
```

### Endpoint

```csharp
app.MapGet("/tiles/{z}/{x}/{y}", async (HttpContext http, IDomainDataCache cache, int z, int x, int y) =>
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
- Use `.CacheOutputWithDomain(domain, resourceRouteKey, entityKind)` (or `[CacheDomain]`) and `GetOrSetEntityAsync(http, factory)` so entries are tagged `entity:{domain}:{entityKind}:{id}`.  
- On admin save: `InvalidateEntityAsync(domain, entityKind, id)` — **same Version**, new body on next request.  
- Prefer shorter client cache (or `Private` / low max-age).  
- `ETagMode: Resource` gives a distinct ETag per product URL/id (still generation-bound; for very short client TTL, `None` is fine).

### Example configuration

```json
"Domains": {
  "store": {
    "Version": "1",
    "DataCache": {
      "Ttl": "00:01:00"
    },
    "OutputCache": {
      "Ttl": "00:00:30",
      "ETagMode": "Resource"
    },
    "ClientCache": {
      "Cacheability": "Public",
      "Ttl": "00:00:15",
      "TtlMin": "00:00:15"
    },
    "FusionCache": {
      "HardTtl": "00:05:00",
      "FailSafe": "00:10:00"
    }
  }
}
```

### Endpoint + invalidation

```csharp
// GET — cache per product id
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var product = await cache.GetOrSetEntityAsync(http, async ct =>
        await db.Products.FindAsync([id], ct));
    return product is null ? Results.NotFound() : Results.Json(product);
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
t0  GET /products/42  → MISS → DB (price 10) → store OC+DC, tags domain + entity:store:products:42
t1  Admin sets price 12, calls InvalidateEntityAsync("store", "products", 42)
t2  GET /products/42  → MISS → DB (price 12)
    GET /products/99  → still HIT (other entity)
```

---

## ETag modes

CacheOrchestrator ETags are **generation-bound** (derived from the domain `Version`), not computed from the response body. This guarantees zero-allocation ETags that survive cache purges and TTL expiries, but it means the ETag does not change when individual rows mutate under a stable version.

| Mode | What it is | Use when |
|------|------------|----------|
| `Version`<br>*(default)* | **Shared Generation Stamp**<br>A single weak ETag (hash of domain `Version`, e.g. `W/"a1b2"`) shared by **all** URLs in the domain. | **Snapshot domains** where content only changes when you bump the domain version (e.g., map tiles, monthly exports). |
| `Resource` | **Namespaced Generation Stamp**<br>A distinct ETag per URL (hash of `Version` + resource ID, e.g. `W/"a1b2-x9y8"`). Still tied to the domain version, but unique per endpoint. | **Mass-updated catalogs** where you bump the `Version` to invalidate everything, but your CDN requires unique ETags per URL. |
| `None` | **Disabled**<br>Removes `ETag` headers generated by the policy. | **CRUD / Dynamic APIs** where you invalidate individual entities. Disabling the static ETag ensures browsers perform a normal `GET` after their TTL expires, avoiding `304 Not Modified` responses for updated content. Alternatively, set a true timestamp-based ETag manually inside your endpoint. |

ETag does **not** drive server Output Cache lookup. OC keys are per URL + vary + `data-version`.  
ETag is for **browser/CDN** conditional requests after client `max-age` expires.

### Custom ETags for CRUD

When using `ETagMode: None`, you can manually set a precise, zero-allocation ETag inside your endpoint using the entity's `UpdatedAt` timestamp. 

> [!TIP]
> Always include the domain `Version` in your custom ETag. If you update your JSON schema and bump the domain version, the ETags must change even if the database timestamps haven't.

```csharp
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var product = await cache.GetOrSetEntityAsync(http, async ct =>
        await db.Products.FindAsync([id], ct));

    if (product is not null)
    {
        // 1. Get the resolved domain options from the request
        if (http.GetDomainCacheOptions() is { } opts)
        {
            // 2. Combine the domain Version with the DB timestamp
            http.Response.Headers.ETag = CacheETagFactory.FromVersionAndResource(
                opts.VersionHex, 
                product.UpdatedAtUtc.Ticks.ToString());
        }

        return Results.Json(product);
    }
    
    return Results.NotFound();
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");
```

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
| Keep default safety | omit flags (`AuthBypassMode` defaults to `AuthenticatedOrAuthorization`) |
| Cache private per-user pages | `AuthBypassMode: Never`, `VaryOutputCacheByUser: true`, `ClientCache.Cacheability: Private` |
| Public assets with API key | `AuthBypassMode: Never`, `VaryOutputCacheByUser: false`, `ClientCache.Cacheability: Public` |

Canonical detail and full examples: [output-cache.md](output-cache.md#authenticated-traffic), [vary.md](vary.md). See [configuration.md](configuration.md) for nested domain sections.

---

## Related

- [Guide — concepts](guide/concepts.md)  
- [invalidation.md](invalidation.md) — Version, domain, entity, custom tags  
- [configuration.md](configuration.md) — full property list  
- [client-cache-schedule.md](client-cache-schedule.md) — cutover-friendly client max-age  
- [fusion-cache.md](fusion-cache.md) — `GetOrSetAsync` overloads  
