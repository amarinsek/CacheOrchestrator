# Output Cache

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — concepts](guide/concepts.md). Catalog: [documentation index](README.md).

Output Cache stores the **full HTTP response** for GET and HEAD. CacheOrchestrator applies ASP.NET Core Output Caching per **domain**: TTL, tags, vary rules, `Cache-Control`, and ETag all come from that domain.

## Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
app.UseCacheOrchestrator();
```

`UseCacheOrchestrator` calls `UseOutputCache`. For a Redis store, install `CacheOrchestrator.Redis` and call `AddRedisBackend()` — see [backends.md](backends.md).

## Base policy and endpoints without a domain

ASP.NET Core Output Caching is **policy-driven**. CacheOrchestrator registers a **base policy of `NoCache`**: responses are **not** cached unless an endpoint opts in.

| Endpoint | Output Cache |
|----------|----------------|
| `.CacheOutputWithDomain("…")` / `[CacheDomain("…")]` | **Yes** — domain policy (TTL, tags, client headers, diagnostics; encoding vary from domain settings) |
| No domain metadata | **No** — base `NoCache` |
| Explicit `.CacheOutput(p => p.NoCache())` / `OutputCacheAttribute { NoStore = true }` | **No** — redundant with the base policy, still fine on Admin / metrics / ops routes |

**Without `.CacheOutputWithDomain` / `[CacheDomain]`, there is no Output Cache entry.** You do not need a separate `.NoCache()` / `NoStore` for that. Built-in Admin and sample `/metrics` may still set `NoStore` explicitly; that is harmless.

Data cache is separate: `IDomainDataCache` / `ICacheOrchestrator` still need a domain (endpoint metadata, explicit overload, or `CacheDomainContext`) or the factory runs uncached — see [FAQ](faq.md#fusion-runs-uncached--why).

## Minimal APIs

```csharp
using CacheOrchestrator.OutputCache;

app.MapGet("/api/products", () => /* ... */)
   .CacheOutputWithDomain("products");

app.MapGet("/api/products/{id}", () => /* ... */)
   .CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");

app.MapGet("/api/t/{tenant}/items", (string tenant) => /* ... */)
   .CacheOutputWithDomain(http => $"tenant-{http.Request.RouteValues["tenant"]}");

app.MapGet("/api/t/{tenant}/items/{id}", (string tenant, string id) => /* ... */)
   .CacheOutputWithDomain(
       http => $"tenant-{http.Request.RouteValues["tenant"]}",
       resourceRouteKey: "id",
       entityKind: "items");

app.MapGet("/tiles/{z}/{x}/{y}", () => /* ... */)
   .CacheOutputWithDomainTemplate("maps-{host}-{route:z}");
```

`resourceRouteKey` and `entityKind` tag the OC entry so `InvalidateEntityAsync` can purge that row. The func overload also accepts those two arguments (dynamic domain + entity tags). `CacheOutputWithDomainTemplate` has **no** entity overload — it only resolves the domain string.

Templates expand these tokens:

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
| Auth signal matches `AuthBypassMode` (default `AuthenticatedOrAuthorization`) | Bypass; client blocked |
| `AuthBypassMode: Never` (or legacy `BypassWhenAuthenticated: false`) | Cache allowed; optional `auth-user` vary |
| `OutputCacheEnabled: false` | Off (`oc=off`); client headers still applied. Not the same as request-level bypass (auth / no-store). |
| Enabled | Lookup and store, locking, TTL from the domain |
| Status not in `CacheableStatusCodes` | No store |
| `Set-Cookie` or response `Authorization` | No store; client blocked |
| Signed-in user and `ClientCache.Cacheability: Public` (when `ClientForcePrivateWhenAuthenticated`) | Client header forced to **private** |

**Vary:** host, query keys (tracking omitted; optional allow/deny lists), `Accept-Encoding`, optional `Accept` / `Accept-Language` / headers / cookies, `data-version` from `Version`. When authenticated traffic is cached and `VaryOutputCacheByUser` is true, also **`auth-user`**. Full matrix: [vary.md](vary.md).

**Tags:** `domain:{name}`. If `resourceRouteKey` and `entityKind` resolve, also `entity:{domain}:{entityKind}:{id}` and `entitykind:{domain}:{entityKind}`.

**ETag:** domain `ETagMode` — `Version` (generation), `Resource` (per URL or id), `None`. See [domain-profiles.md](domain-profiles.md).

### Authenticated traffic

By default any signed-in user or `Authorization` header skips Output Cache (`AuthBypassMode: AuthenticatedOrAuthorization`). That is the safe setting for mixed public and private APIs.

- **AuthBypassMode** — preferred control (`Never`, `AuthenticatedIdentityOnly`, `AuthorizationHeaderOnly`, `AuthenticatedOrAuthorization`).
- **BypassWhenAuthenticated** — **obsolete**; still binds for compatibility (`true`/`false` map to `AuthenticatedOrAuthorization` / `Never` when `AuthBypassMode` is unset).
- **VaryOutputCacheByUser** (default `true`) — when you allow caching, partition by user, claims, or API-key hash.
- **DataCacheRespectAuthBypass** (default `true`) — data cache also skips when OC would auth-bypass; set `false` for 2.1-like data-cache-under-Authorization.

**Private dashboard (per-user server cache):**

```json
"user-dashboard": {
  "AuthBypassMode": "Never",
  "VaryOutputCacheByUser": true,
  "ClientCache": {
    "Cacheability": "Private",
    "TtlSeconds": 60
  },
  "OutputCache": {
    "TtlSeconds": 30
  }
}
```

Alice and Bob both call `GET /api/me/summary`. The server stores two entries (`auth-user=u:alice` and `u:bob`). The browser may cache privately for 60 seconds. A shared CDN must not treat the response as public.

**Public tiles with an API key (one shared entry):**

```json
"osm-tiles": {
  "AuthBypassMode": "Never",
  "VaryOutputCacheByUser": false,
  "TreatAuthorizationAsAuthSignal": false,
  "ClientCache": {
    "Cacheability": "Public",
    "TtlSeconds": 86400
  },
  "OutputCache": {
    "TtlSeconds": 3600
  }
}
```

Clients send `Authorization: Bearer <map-key>` for rate limits or billing. The body is the same for everyone. Use this only when the payload does not depend on the caller.

See [vary.md](vary.md), [configuration.md](configuration.md), and [domain-profiles.md](domain-profiles.md).

## Headers

On response start the policy sets:

- **Cache-Control** — from `ClientCacheHeaderGenerator`, including the [Client Cache Schedule](client-cache-schedule.md) ramp when `ScheduledUpdateUtc` is set.
- **X-Cache** — domain, client, output (and data / ms when Output Cache missed). Written when `Cache:EmitDiagnosticsHeaders` is `true` (the default). See [observability.md](observability.md).

## Related

- [Guide — concepts](guide/concepts.md)
- [vary.md](vary.md)
- [cache-keys.md](cache-keys.md)
- [configuration.md](configuration.md)
- [invalidation.md](invalidation.md)
- [observability.md](observability.md)
