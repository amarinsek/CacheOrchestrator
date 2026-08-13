# Output Cache

Output Cache stores the **full HTTP response** for GET and HEAD. CacheOrchestrator applies ASP.NET Core Output Caching per **domain**: TTL, tags, vary rules, `Cache-Control`, and ETag all come from that domain.

## Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
app.UseCacheOrchestrator();
```

`UseCacheOrchestrator` calls `UseOutputCache`. For a Redis store, install `CacheOrchestrator.Redis` and call `AddRedisBackend()` — see [backends.md](backends.md).

## Minimal APIs

```csharp
using CacheOrchestrator.OutputCache;

app.MapGet("/api/products", () => /* ... */)
   .CacheOutputWithDomain("products");

app.MapGet("/api/products/{id}", () => /* ... */)
   .CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");

app.MapGet("/api/t/{tenant}/items", (string tenant) => /* ... */)
   .CacheOutputWithDomain(http => $"tenant-{http.Request.RouteValues["tenant"]}");

app.MapGet("/tiles/{z}/{x}/{y}", () => /* ... */)
   .CacheOutputWithDomainTemplate("maps-{host}-{route:z}");
```

`resourceRouteKey` and `entityKind` tag the entry so `InvalidateEntityAsync` can purge that row. Templates expand these tokens:

- `{host}` — host without port
- `{route:name}` — route value
- `{header:Name}` — request header
- `{query:key}` — query parameter
- `{custom:key}` — value from the `customProviders` map

## Controllers

```csharp
using CacheOrchestrator.OutputCache;

[CacheDomain("products")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(/* ... */);

    [HttpGet("{id}")]
    [CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
    public IActionResult Get(string id) => Ok(/* ... */);
}
```

The action attribute overrides the controller. `AddCacheOrchestrator` registers `CacheDomainConvention`, which attaches `DomainOutputCachePolicy` when `[CacheDomain]` is present.

If a Minimal endpoint only has the attribute in metadata:

```csharp
app.MapGet(...).CacheOutputWithDomainAttribute();
```

## Policy

`DomainOutputCachePolicy` decides lookup and store:

| Condition | Result |
|-----------|--------|
| Not GET/HEAD | No output caching |
| Request `Cache-Control: no-store` | Bypass; client `no-store` |
| Authenticated / `Authorization` and `BypassWhenAuthenticated: true` (default) | Bypass; client blocked |
| Authenticated / `Authorization` and `BypassWhenAuthenticated: false` | Cache allowed; optional `auth-user` vary |
| `OutputCacheEnabled: false` | Bypass; client headers still applied |
| Enabled | Lookup and store, locking, TTL from the domain |
| Status not in `CacheableStatusCodes` | No store |
| `Set-Cookie` or response `Authorization` | No store; client blocked |
| Signed-in user and `ClientCacheability: Public` | Client header forced to **private** |

**Vary:** host, query keys (tracking parameters omitted), optional `Accept-Encoding`, `data-version` from `Version`. When authenticated traffic is cached and `VaryOutputCacheByUser` is true, also **`auth-user`** (name / `sub` / hash of `Authorization`).

**Tags:** `domain:{name}`. If `resourceRouteKey` and `entityKind` resolve, also `entity:{domain}:{entityKind}:{id}` and `entitykind:{domain}:{entityKind}`.

**ETag:** domain `ETagMode` — `Version` (generation), `Resource` (per URL or id), `None`. See [domain-profiles.md](domain-profiles.md).

### Authenticated traffic

By default any signed-in user or `Authorization` header skips Output Cache. That is the safe setting for mixed public and private APIs.

- **BypassWhenAuthenticated** (default `true`) — skip Output Cache and block client cache for authenticated traffic.
- **VaryOutputCacheByUser** (default `true`) — when you allow caching, partition Output Cache by user or API key.

**Private dashboard (per-user server cache):**

```json
"user-dashboard": {
  "BypassWhenAuthenticated": false,
  "VaryOutputCacheByUser": true,
  "ClientCacheability": "Private",
  "ClientTtlSeconds": 60,
  "OutputCacheTtlSeconds": 30
}
```

Alice and Bob both call `GET /api/me/summary`. The server stores two entries (`auth-user=u:alice` and `u:bob`). The browser may cache privately for 60 seconds. A shared CDN must not treat the response as public.

**Public tiles with an API key (one shared entry):**

```json
"osm-tiles": {
  "BypassWhenAuthenticated": false,
  "VaryOutputCacheByUser": false,
  "ClientCacheability": "Public",
  "ClientTtlSeconds": 86400,
  "OutputCacheTtlSeconds": 3600
}
```

Clients send `Authorization: Bearer <map-key>` for rate limits or billing. The body is the same for everyone. Use this only when the payload does not depend on the caller.

See [configuration.md](configuration.md) and [domain-profiles.md](domain-profiles.md).

## Headers

On response start the policy sets:

- **Cache-Control** — from `ClientCacheHeaderGenerator`, including the [Client Cache Schedule](client-cache-schedule.md) ramp when `ScheduledUpdateUtc` is set.
- **X-Cache** — domain, client, output (and data / ms when Output Cache missed). Written when `Cache:EmitDiagnosticsHeaders` is `true` (the default). See [observability.md](observability.md).

## Related

- [cache-keys.md](cache-keys.md)
- [configuration.md](configuration.md)
- [invalidation.md](invalidation.md)
- [observability.md](observability.md)
