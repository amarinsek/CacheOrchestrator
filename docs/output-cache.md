# Output Cache

Caches **full HTTP responses** for GET/HEAD using ASP.NET Core Output Caching, controlled per **domain**.

## Registration

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
// For Redis Output Cache store: install CacheOrchestrator.Redis and
// AddCacheOrchestrator(config, o => o.AddRedisBackend());
// ...
app.UseCacheOrchestrator(); // UseOutputCache under the hood
```

## Minimal APIs

```csharp
using CacheOrchestrator.OutputCache;

// Fixed domain
app.MapGet("/api/products", () => /* ... */)
   .CacheOutputWithDomain("products");

// Fixed domain + entity tag from route (CRUD purge via InvalidateEntityAsync)
app.MapGet("/api/products/{id}", () => /* ... */)
   .CacheOutputWithDomain("product-detail", resourceRouteKey: "id");

// Per-request domain
app.MapGet("/api/t/{tenant}/items", (string tenant) => /* ... */)
   .CacheOutputWithDomain(http => $"tenant-{http.Request.RouteValues["tenant"]}");

// Template domain (host, route, header, query, custom)
app.MapGet("/api/tiles/{z}/{x}/{y}", () => /* ... */)
   .CacheOutputWithDomainTemplate("maps-{host}-{route:z}");
```

### Domain templates

Supported tokens (see `DomainTemplateCompiler`):

| Token | Meaning |
|-------|---------|
| `{host}` | Host without port |
| `{route:name}` | Route value |
| `{header:Name}` | Request header |
| `{query:key}` | Query parameter |
| `{custom:key}` | From `customProviders` map |

## Controllers / MVC

```csharp
using CacheOrchestrator.OutputCache;

[CacheDomain("products")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(/* ... */);

    [HttpGet("{id}")]
    [CacheDomain("product-detail", resourceRouteKey: "id")] // action overrides controller
    public IActionResult Get(string id) => Ok(/* ... */);
}
```

`AddCacheOrchestrator` adds `CacheDomainConvention`, which attaches `DomainOutputCachePolicy` as a filter when `[CacheDomain]` is present.

For Minimal endpoints that only have the attribute metadata:

```csharp
app.MapGet(...).CacheOutputWithDomainAttribute();
```

## Policy behaviour (`DomainOutputCachePolicy`)

| Condition | Result |
|-----------|--------|
| Not GET/HEAD | No output caching |
| Request `Cache-Control: no-store` | Bypass + client no-store |
| Authenticated / `Authorization` **and** `BypassWhenAuthenticated: true` (default) | Bypass + client blocked |
| Authenticated / `Authorization` **and** `BypassWhenAuthenticated: false` | Cache allowed; optional `auth-user` vary |
| `OutputCacheEnabled: false` | Bypass (client headers still applied) |
| Enabled | Lookup + store, locking, TTL from domain |
| Response status not in `CacheableStatusCodes` | No store |
| Response has `Set-Cookie` / response `Authorization` | No store; client blocked |
| Logged-in user + domain `ClientCacheability: Public` | Client header forced to **private** |

**Vary:** host, query keys (tracking params stripped), optional `Accept-Encoding`, `data-version` from `Version`, and when auth is allowed through with `VaryOutputCacheByUser: true`, **`auth-user`** (name / `sub` / hash of Authorization).  

### Authenticated caching (optional)

By default the policy is **strict**: any signed-in user or `Authorization` header skips Output Cache.  
That is correct for most apps. Two domain flags open safer exceptions:

| Setting | Default | Meaning |
|---------|---------|---------|
| `BypassWhenAuthenticated` | `true` | Skip OC + block client cache for auth traffic |
| `VaryOutputCacheByUser` | `true` | When not bypassing, partition OC by user / API key |

**Example A — private dashboard (per-user server cache)**

```json
"user-dashboard": {
  "BypassWhenAuthenticated": false,
  "VaryOutputCacheByUser": true,
  "ClientCacheability": "Private",
  "ClientTtlSeconds": 60,
  "OutputCacheTtlSeconds": 30
}
```

Alice and Bob both hit `GET /api/me/summary` with cookies. Server stores **two** Output Cache entries (vary `auth-user=u:alice` vs `u:bob`). Browser may cache privately for 60s. Shared CDNs should not treat the response as public.

**Example B — public map tiles with API key (shared cache)**

```json
"osm-tiles": {
  "BypassWhenAuthenticated": false,
  "VaryOutputCacheByUser": false,
  "ClientCacheability": "Public",
  "ClientTtlSeconds": 86400,
  "OutputCacheTtlSeconds": 3600
}
```

Clients send `Authorization: Bearer <map-key>` only for rate-limiting / billing. The **response body is the same for everyone**. With `VaryOutputCacheByUser: false`, one OC entry serves all keys. Do **not** use this pattern for user-specific JSON.

See also [configuration.md](configuration.md) and [domain-profiles.md](domain-profiles.md).

**Tags:** `domain:{normalizedDomain}`; if `resourceRouteKey` resolves a route value, also `entity:{domain}:{id}`.

**ETag:** controlled by domain `ETagMode` (`Version` = generation stamp, `Resource` = per URL/id, `None` = omit). See [domain-profiles.md](domain-profiles.md).

## Headers

On response start the policy sets:

- **`Cache-Control`** — from `ClientCacheHeaderGenerator` (or no-store), including **[Client Cache Schedule](client-cache-schedule.md)** ramp-down when `ScheduledUpdateUtc` is set  
- **`X-Cache`** — domain + client + output (+ data/ms when not OC hit) + version  
  (only when `Cache:EmitDiagnosticsHeaders` is `true`, the default — see [observability.md](observability.md))

## Related

- [cache-keys.md](cache-keys.md) — OC key material, Namespace, domain vs tags  
- [configuration.md](configuration.md)  
- [invalidation.md](invalidation.md)  
- [observability.md](observability.md)  
