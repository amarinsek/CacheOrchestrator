# Concepts

> **Guide path:** [Getting started](getting-started.md) → **Concepts** → [Domain profiles](domain-profiles.md) · [Guide index](README.md)

The getting-started tutorial used one domain to coordinate Client Cache, Output Cache, and Data Cache. This page explains the model behind that example so you can design domains for your own application.

CacheOrchestrator does not replace ASP.NET Core Output Caching, FusionCache, HybridCache, Redis, browsers, or CDNs. It gives those layers one policy model and one invalidation vocabulary.

## A domain keeps policy out of the endpoint

A **domain** is a named group of cache rules such as `promotions`, `catalog`, or `map-tiles`. It defines:

- which cache layers are enabled;
- how long entries remain fresh;
- which Data Cache instance stores objects;
- which Client Cache headers are emitted;
- how requests vary;
- the current domain `Version`.

You apply the domain to an HTTP endpoint with `.CacheOutputWithDomain(...)` or `[CacheDomain]`. The endpoint names the domain, while configuration owns its policy:

```csharp
app.MapGet("/api/promotions", LoadPromotionsAsync)
   .CacheOutputWithDomain("promotions");
```

That separation is the central idea. Endpoint code describes what the endpoint returns; the domain describes how that result is cached. Moving from in-memory stores to Redis, changing TTLs, or turning off one layer does not require a new endpoint implementation.

A domain is not a cache store. Several domains can share one provider, and one application can map different domains to different Data Cache instances.

## A request can stop at three layers

A cacheable request moves from the client toward the data source:

```text
Client / CDN
    │  cached response still fresh? ──► return it; the server sees no request
    ▼
ASP.NET Output Cache
    │  cached HTTP response found?  ──► return it; the endpoint does not run
    ▼
Endpoint + Data Cache
    │  cached object found?         ──► build the HTTP response from that object
    ▼
Factory: database or remote service
```

Each layer stores something different:

| Layer | Stores | Controlled by |
|-------|--------|---------------|
| **Client Cache** | The HTTP response in a browser or CDN | `ClientCache` headers and TTL |
| **Output Cache** | The complete HTTP response in ASP.NET Core | `OutputCache` policy and TTL |
| **Data Cache** | The object returned by your factory | `DataCache` policy and the selected provider |

An Output Cache hit is the shortest server path: the endpoint and Data Cache are not consulted. A Data Cache hit matters after Output Cache misses or is disabled: the endpoint runs, but the database or remote service does not.

Client Cache is different from the server-side layers. Once a browser or CDN has stored a public response, server-side invalidation cannot recall that copy. The client observes the change when its `max-age` ends or when its own cache is purged. This is why dynamic APIs usually use a shorter client TTL than immutable or scheduled datasets.

## One request uses one resolved snapshot

At the start of a domain request, CacheOrchestrator resolves two aligned snapshots. Core's `DomainCacheOptions` contains domain identity, Version, and Data Cache policy. ASP.NET Core wraps it in `DomainHttpCacheOptions`, which adds Output Cache, Client Cache, authentication, vary, ETag, and HTTP key policy. The request pins the HTTP snapshot, so every layer sees one consistent view while Core remains independent of HTTP.

For the common HTTP path, the endpoint metadata is enough:

```csharp
app.MapGet("/api/products/{id:int}", async (
    HttpContext http,
    int id,
    IDomainDataCache cache,
    CancellationToken cancellationToken) =>
{
    Product? product = await cache.GetOrSetEntityAsync(
        http,
        token => LoadProductAsync(id, token),
        cancellationToken);

    return product is null ? Results.NotFound() : Results.Json(product);
})
.CacheOutputWithDomain(
    "catalog",
    entityKind: "products",
    resourceRouteKey: "id");
```

`IDomainDataCache` reads the `catalog` snapshot already attached to the request. You do not repeat the domain name inside `GetOrSetEntityAsync`.

For an endpoint that uses only Data Cache and has no domain metadata, pass the domain explicitly:

```csharp
await cache.GetOrSetAsync(
    http,
    "catalog",
    LoadProductsAsync,
    cancellationToken);
```

Without endpoint metadata, an existing request snapshot, or an explicit domain, `IDomainDataCache` runs the factory uncached and records an unresolved diagnostic. Class libraries and workers use the HTTP-free `ICacheOrchestrator` API with a host-supplied `CacheDomainContext`.

The complete resolution rules live in [Data Cache](../reference/data-cache.md).

## Freshness has three controls

TTL, invalidation, and `Version` solve different freshness problems:

| Control | Effect | Best fit |
|---------|--------|----------|
| **TTL** | An entry expires and the next request recreates it | A safety bound or naturally short-lived data |
| **Tag invalidation** | Matching entries are removed now | A known CRUD change, such as product `42` |
| **Version** | Requests move to a new generation of cache keys | A coordinated snapshot or schema cutover |

### TTL is the fallback clock

Every layer has its own TTL because it stores a different artifact. A five-minute Data Cache TTL does not force a two-minute Output Cache entry to live for five minutes, and neither value controls a response already stored by a client.

### Invalidation removes known server entries

When product `42` changes, targeted invalidation removes entries tagged for that logical entity from Output Cache and the Data Cache:

```csharp
await invalidator.InvalidateEntityAsync(
    "catalog",
    "products",
    42,
    cancellationToken);
```

Other products remain cached. Domain- and entity-kind-level invalidation are available when the change is broader.

### Version starts a new generation

`Version` is a stamp for the whole domain, not the revision number of one entity. Changing `"2030-08"` to `"2030-09"` changes the cache identity for every request in that domain. Old entries are no longer found and expire according to their store policy.

Use a version change for snapshot cutovers, large coordinated releases, or representation changes. Do not bump the whole domain for an ordinary single-row update when entity invalidation is available.

The next page turns these choices into two practical designs: [snapshot and dynamic domain profiles](domain-profiles.md).

## Entity identity enables targeted invalidation

A domain can contain many logical entity kinds. The pair `entityKind` + resource id identifies one entity inside that domain:

```text
domain: catalog
entity kind: products
resource id: 42
```

For a detail endpoint, declare that identity once:

```csharp
.CacheOutputWithDomain(
    "catalog",
    entityKind: "products",
    resourceRouteKey: "id")
```

`resourceRouteKey` tells CacheOrchestrator which route value contains the id. `GetOrSetEntityAsync` consumes the same identity for the Data Cache key and tags. `InvalidateEntityAsync` then addresses the same domain, kind, and id.

Entity identity is optional. Snapshot endpoints and simple URL-shaped data can use `GetOrSetAsync`. Collections and related objects can extend the footprint with members, dependencies, and aliases; see [Entity footprint](../reference/entity-footprint.md).

## Request identity prevents accidental sharing

Caching is safe only when requests that can produce different responses have different cache identities.

Without explicit identity metadata, Output Cache supports `GET` and `HEAD` using URL identity. CacheOrchestrator also materializes configured vary dimensions for Output Cache and Data Cache, including query parameters, accepted media types, host, and user information when enabled.

Non-GET methods require an explicit binding such as `.WithCacheIdentity(...)` or `.WithContentHashCacheIdentity(...)`. Merely applying a domain does not make a `POST` response cacheable.

Authenticated traffic uses a conservative default: an authenticated user or `Authorization` header bypasses Output Cache, blocks the Client Cache, and—by default—also bypasses the Data Cache. Opting into shared or per-user caching requires deliberate auth and vary settings.

Details: [Vary](../reference/vary.md) · [Endpoint cache identity](../reference/cache-identity.md).

## Stores and namespaces belong below the domain

Root configuration selects the physical providers. Domains select policy and, for Data Cache, a named instance.

The root `Namespace` prefixes store keys so applications sharing Redis do not collide. It is application infrastructure, not a domain name and not an endpoint setting.

This separation lets the same domain model run in several layouts:

- one process with in-memory Output Cache and Data Cache;
- local Output Cache with FusionCache backed by Redis;
- shared Redis Output Cache and Data Cache across several instances;
- in-memory nodes coordinated by the optional HTTP cluster bus.

Choose packages after you know which policies and topology you need. The guide continues with [Domain profiles](domain-profiles.md), then [Packages](packages.md) and [Topologies](topologies.md).

## Keep this mental model

- A **domain** is a named policy group, not a store.
- Client Cache, Output Cache, and Data Cache store different things but use one resolved domain snapshot.
- **TTL** waits, **invalidation** removes known entries, and **Version** starts a new generation.
- Entity identity connects a route, its cached object, and targeted invalidation.
- Providers and Redis topology can change without changing the domain applied by the endpoint.

Next: choose between a [snapshot or dynamic domain profile](domain-profiles.md).
