# Output Cache

> **Reference.** Product overview: [root README](../../README.md). Orientation: [concepts](../guide/concepts.md). Catalog: [documentation index](../README.md).

Output Cache stores the **full HTTP response**. By default that means **GET** and **HEAD** with Url identity (path, query, host, domain vary). Other methods are not cached by Output Cache unless the endpoint opts in with **[endpoint cache identity](cache-identity.md)**. CacheOrchestrator applies ASP.NET Core Output Caching per **domain**: TTL, tags, vary rules, `Cache-Control`, and ETag all come from that domain.

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
| `.CacheOutputWithDomain("…")` / `[CacheDomain("…")]` | **Yes** — domain policy (TTL, tags, Client Cache headers, diagnostics; encoding vary from domain settings) |
| No domain metadata | **No** — base `NoCache` |
| Explicit `.CacheOutput(p => p.NoCache())` / `OutputCacheAttribute { NoStore = true }` | **No** — redundant with the base policy, still fine on Admin / metrics / ops routes |

**Without `.CacheOutputWithDomain` / `[CacheDomain]`, there is no Output Cache entry.** You do not need a separate `.NoCache()` / `NoStore` for that. Built-in Admin and sample `/metrics` may still set `NoStore` explicitly; that is harmless.

Data Cache is separate: `IDomainDataCache` / `ICacheOrchestrator` still need a domain (endpoint metadata, explicit overload, or `CacheDomainContext`) or the factory runs uncached — see [FAQ](../guide/faq.md#data-cache-runs-uncached--why).

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

`resourceRouteKey` and `entityKind` tag the Output Cache entry so `InvalidateEntityAsync` can purge that row. The delegate overload also accepts those two arguments for a dynamic domain with entity tags. `CacheOutputWithDomainTemplate` has **no** entity overload; it only resolves the domain string.

Templates expand these tokens:

- `{host}` — host without port
- `{route:name}` — route value
- `{header:Name}` — request header
- `{query:key}` — query parameter
- `{custom:key}` — value from the `customProviders` map

Custom providers are fixed when the template is compiled and receive the current `HttpContext`:

```csharp
var domainTokens = new Dictionary<string, Func<HttpContext, string?>>
{
    ["tenant"] = http => http.User.FindFirst("tenant_id")?.Value
};

app.MapGet("/api/tenant/products", () => /* ... */)
   .CacheOutputWithDomainTemplate("catalog-{custom:tenant}", domainTokens);
```

Use templates only for bounded, trusted domain material. The resolved value is normalized as a domain name. An unconfigured result falls back to `DomainDefaults` and logs a warning; a missing token appends nothing, which can collapse distinct requests onto the same domain. Validate required tenant or routing state before caching. Template endpoints cannot declare entity identity, so use the delegate overload of `CacheOutputWithDomain` when `resourceRouteKey` and `entityKind` are also required.

## Controllers

```csharp
using CacheOrchestrator.OutputCache;

[CacheDomain("products")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(/* ... */);

    [HttpGet("{id:int}")]
    [CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
    public IActionResult Get(int id) => Ok(/* ... */);
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
| No identity metadata and not GET/HEAD | No output caching |
| Identity metadata present, method has no binding | No output caching |
| Identity contract / content-hash returns null (or body oversize) | Bypass this request |
| Request `Cache-Control: no-store` | Bypass; client `no-store` |
| Auth signal matches `AuthBypassMode` (default `AuthenticatedOrAuthorization`) | Bypass; client blocked |
| `AuthBypassMode: Never` | Cache allowed; optional `auth-user` vary |
| `OutputCache.Enabled: false` | Off (`oc=off`); Client Cache headers still apply. This is not the same as request-level bypass (auth or `no-store`). |
| Enabled | Lookup and store, locking, TTL from the domain |
| Status not in `CacheableStatusCodes` | No store |
| `Set-Cookie` or response `Authorization` | No store; client blocked |
| Signed-in user and `ClientCache.Cacheability: Public` while `ClientCache.ForcePrivateWhenAuthenticated` is `true` | Client header forced to **private** |

**Vary:** host, query keys (tracking omitted; optional allow/deny lists), `Accept-Encoding`, `Accept` by default, optional `Accept-Language` / headers / cookies, and `data-version` from `Version`. When authenticated traffic is cached and `VaryOutputCacheByUser` is `true`, the policy also adds **`auth-user`**. Endpoint identity material is folded into Output Cache `VaryByValues` as `co-id:*`; see [cache keys](cache-keys.md) and [endpoint cache identity](cache-identity.md). The complete domain vary matrix is in [domain vary dimensions](vary.md).

**Tags:** `domain:{name}`. If `resourceRouteKey` and `entityKind` resolve, also `entity:{domain}:{entityKind}:{id}` and `entitykind:{domain}:{entityKind}`.

**ETag:** domain `ETagMode` — `Version` (generation), `Resource` (per URL or id), `None`. See [domain-profiles.md](../guide/domain-profiles.md).

## Endpoint cache identity

Without identity bindings, Output Cache applies to **GET/HEAD** with Url identity. For read-only POST (search / GraphQL), custom GET keys, or Url identity on POST, bind identity per method.

Full concept, rules, cheat sheet, and mixed Minimal API / MVC DX examples: **[cache-identity.md](cache-identity.md)**.

```csharp
using CacheOrchestrator.Identity;

app.MapPost("/graphql", ...)
   .CacheOutputWithDomain("graphql")
   .WithContentHashCacheIdentity(["POST"], maxBodyBytes: 65_536);
```

## Authenticated traffic

By default any signed-in user or `Authorization` header skips Output Cache (`AuthBypassMode: AuthenticatedOrAuthorization`). That is the safe setting for mixed public and private APIs.

- **AuthBypassMode** — preferred control (`Never`, `AuthenticatedIdentityOnly`, `AuthorizationHeaderOnly`, `AuthenticatedOrAuthorization`).
- **VaryOutputCacheByUser** (default `true`) — when you allow authenticated caching, partition entries by user, selected claims, or an Authorization hash.
- **DataCacheRespectAuthBypass** (default `true`) — Data Cache also skips when Output Cache would bypass. Set it to `false` only when the object cached by Data Cache is shared between callers.

The following examples are domain entries placed under `Cache:Domains`.

**Private dashboard (per-user server cache):**

```json
{
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
}
```

Alice and Bob both call `GET /api/me/summary`. The server stores two entries (`auth-user=u:alice` and `u:bob`). The browser may cache privately for 60 seconds. A shared CDN must not treat the response as public.

**Public tiles with an API key (one shared entry):**

```json
{
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
}
```

Clients send `Authorization: Bearer <map-key>` for rate limits or billing. The body is the same for everyone. Use this only when the payload does not depend on the caller.

See [vary.md](vary.md), [configuration.md](configuration.md), and [domain-profiles.md](../guide/domain-profiles.md).

## Headers

On response start the policy sets:

- **Cache-Control** — from `ClientCacheHeaderGenerator`, including the [Client Cache Schedule](../guide/client-cache-schedule.md) ramp when `ScheduledUpdateUtc` is set.
- **X-Cache** — domain, client, output (and data / ms when Output Cache missed). Written when `Cache:EmitDiagnosticsHeaders` is `true` (the default). See [observability.md](observability.md).

## Related

- [concepts](../guide/concepts.md)
- [vary.md](vary.md)
- [cache-keys.md](cache-keys.md)
- [configuration.md](configuration.md)
- [invalidation.md](invalidation.md)
- [observability.md](observability.md)
