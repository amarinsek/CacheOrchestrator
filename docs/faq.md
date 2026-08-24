# FAQ

> **Guide.** Product overview: [root README](../README.md). Orientation: [Guide](guide/README.md). Catalog: [documentation index](README.md).

Short answers. The topic pages hold the full story.

## Scope

The library is for ASP.NET Core HTTP apps: Output Cache, FusionCache, and client headers under one domain model. FusionCache itself stays the object cache; this package configures and scopes it. Storage other than InMemory and Redis is a registrar you write. Redis topology remains yours.

---

## EF Core `ExecuteUpdate` did not invalidate cache

The interceptor only sees `ChangeTracker` (`Added` / `Modified` / `Deleted`) after a successful `SaveChanges`. Bulk `ExecuteUpdate` / `ExecuteDelete` / `ExecuteInsert` never produce those entries.

Call `InvalidateEntitiesAsync` or `InvalidateEntityKindAsync` yourself. Details: [ef-core-invalidation.md](ef-core-invalidation.md).

---

## Why is a route cached when I never set a domain?

It should not be. CacheOrchestrator’s **base** Output Cache policy is **`NoCache`**. Full-response caching applies only with `.CacheOutputWithDomain` / `[CacheDomain]` (or your own explicit Output Cache policy).

If a route still looks cached, check for a host-added `AddBasePolicy` / `.CacheOutput(...)`, a CDN/browser cache, or a domain policy you forgot. Explicit `NoStore` / `NoCache` on Admin/metrics is optional redundancy, not required for plain endpoints.

Details: [output-cache.md — Base policy](output-cache.md#base-policy-and-endpoints-without-a-domain).

---

## Fusion runs uncached — why?

`IDomainDataCache.GetOrSetAsync` needs a **domain**. Resolution order:

1. Explicit overload `GetOrSetAsync(http, domain, factory)` (replaces a different snapshot already on the request)
2. Options already on the request (usually set by Output Cache policy)
3. Endpoint metadata (`.CacheOutputWithDomain` / `[CacheDomain]`)
4. Else: factory runs **without** Fusion caching

On the unresolved path the library:

- Logs a **Warning** (`FusionCache skipped: no domain resolved…`)
- Records metric `result=unresolved` with `domain=_`
- Sets disposition `dc=unresolved` (`fa=run`) when headers are still written

**Fix:** put domain on the endpoint, or use the domain overload / `EnsureDomainOptions`.  
Details: [fusion-cache.md](fusion-cache.md).

---

## Authenticated requests and API keys

**Default:** authenticated users **or** an `Authorization` header → Output Cache **bypassed**, client cache **blocked** (`AuthBypassMode: AuthenticatedOrAuthorization`).

| Goal | Config |
|------|--------|
| Keep safe default | leave flags as default |
| Cache private per-user responses | `AuthBypassMode: Never`, `VaryOutputCacheByUser: true`, `ClientCache.Cacheability: Private` |
| Public content that happens to send an API key | `AuthBypassMode: Never`, `VaryOutputCacheByUser: false`, careful `ClientCache.Cacheability` |
| Bypass only cookie/Identity auth | `AuthBypassMode: AuthenticatedIdentityOnly` |
| Keep 2.1-like data cache under Authorization while OC bypasses | `DataCacheRespectAuthBypass: false` (default is `true` = parity) |

Wrong settings can leak one user’s response to another (especially with shared CDNs).  
Details: [vary.md](vary.md), [output-cache.md](output-cache.md#authenticated-traffic), [domain-profiles.md](domain-profiles.md#authenticated-traffic-auth-bypass).

---

## JSON vs XML on the same URL?

Enable `VaryByAccept: true` (optional `AcceptNormalizationList`). Output Cache and Fusion then partition by normalized `Accept`. See [vary.md](vary.md).

---

## Tenant claim without a custom key generator?

Set `AuthBypassMode: Never`, `VaryOutputCacheByUser: true`, and `VaryByAuthClaims: [ "tenant_id" ]`, or register an `ICacheVaryContributor`. Full `IDomainKeyGenerator` replace remains supported.

---

## Why does the data cache skip under Authorization now?

Default is **`DataCacheRespectAuthBypass: true`**: when Output Cache would auth-bypass, the data cache runs the factory uncached (OC↔DC parity). That fixes the old inconsistency where OC bypassed but Fusion still stored data.

For **2.1-like** behaviour (data cache still caches under Authorization while OC bypasses), set `"DataCacheRespectAuthBypass": false`.

---

## ETag = Version — is that a bug?

No. CacheOrchestrator ETags are **generation-bound** (derived from the domain `Version`), not computed from the response body. 

Default `ETagMode: Version` means the HTTP `ETag` is a shared domain generation stamp. Every URL in the domain gets the exact same ETag (e.g., `W/"hash_of_version"`). 

Because the ETag does not hash the body, if you update an entity and purge the cache (`InvalidateEntityAsync`) without bumping the domain `Version`, the ETag will not change. Browsers revalidating with `If-None-Match` will continue to receive a `304 Not Modified` response. 

If you are building a CRUD API where rows mutate under a stable version, use `ETagMode: None` to disable static ETags, ensuring clients fetch fresh data after their TTL expires.

| Profile | Typical ETag mode |
|---------|-------------------|
| **Snapshot** (map tiles, datasets) | `Version` (shared stamp) or `Resource` (namespaced stamp) |
| **Dynamic / CRUD** (mutating rows) | `None` (disables static ETags; forces fresh `GET` after TTL) |

For complete details on each mode, see [ETag modes in domain-profiles.md](domain-profiles.md#etag-modes).

---

## Multiple Redis clusters / named data-cache instances {#multiple-redis-clusters--named-data-cache-instances}

Supported: map domains to named `DataCacheInstances`, each with its own Redis connection. Domains select an instance via `DataCache.Instance`.

```json
"DataCacheInstances": {
  "default": { "Provider": "Redis", "Redis": { "Configuration": "global:6379" } },
  "pii":     { "Provider": "Redis", "Redis": { "Configuration": "secure:6379" } }
}
```

Each Fusion-backed instance gets a **keyed** `IDistributedCache` and multiplexer — the last registration must not overwrite L2 for others.

**Requires:** package `CacheOrchestrator.Redis` + `AddRedisBackend()`.  
Details: [deployment.md](deployment.md#using-multiple-datacache-instances).

---

## Namespace defaults

| Setting | Default behaviour |
|---------|-------------------|
| Root `Namespace` | `app-cache` |
| Output Cache keys | `OutputCache.Namespace` ?? `{Namespace}-oc` |
| Data-cache `default` instance | `{Namespace}-fc` (**no** `-default` suffix; historical `-fc`) |
| Data-cache named instance `pii` | `{Namespace}-fc-pii` |

---

## Custom backends (SQL Server, Memcached, …)

`ICacheBackendRegistrar` is how you add a store the library does not ship. Register the registrar, then set `Provider` under `OutputCache` or `DataCacheInstances`. [backends.md](backends.md) has a full example of Fusion L2 on SQL Server (Output Cache stays InMemory or Redis).

---

## Client Cache Schedule vs server TTL

`ClientCache.ScheduledUpdateUtc` + client TTL fields change only **browser/CDN** `Cache-Control` (`max-age` ramp).  
They do **not** change `OutputCache.TtlSeconds` or `DataCache.TtlSeconds` / Fusion hard / fail-safe.

Phases appear on `X-Cache` as `phase=calm|approaching|hold|n/a`.  
See [client-cache-schedule.md](client-cache-schedule.md).

---

## Redis package vs core

| Package | Contains |
|---------|----------|
| `CacheOrchestrator` (meta) | AspNetCore + Fusion data provider |
| `CacheOrchestrator.Core` / `.AspNetCore` / `.FusionCache` / `.HybridCache` | Policy, HTTP host, Fusion or Hybrid `IDataCacheProvider` — [packages.md](packages.md) |
| `CacheOrchestrator.Redis` | Redis registrar, connection options, Redis health probe |
| `CacheOrchestrator.HttpBus` | HTTP cluster command bus, Static / ServiceDiscovery membership |
| `CacheOrchestrator.EFCore.Invalidation` | SaveChanges interceptor → entity invalidation |

Without Redis package + `AddRedisBackend()`, `"Provider": "Redis"` fails validation.  
Without HttpBus package, multi-instance InMemory invalidation stays process-local (unless you build your own fan-out).  
Without the EF package, `SaveChanges` does not purge cache; call the invalidator yourself.

---

## Admin API vs Admin Console App

- **Admin API** — opt-in HTTP on **each** process (`Cache:Admin:Enabled` + `MapCacheOrchestratorAdmin`). Health, config, invalidate, runtime Version and settings overlays. Process-lifetime `GET …/stats` is **obsolete for analytics**. Ships in the core package; off by default.
- **Admin Console App** — a separate process (`src/CacheOrchestrator.AdminConsole`) that fans out to those APIs. Traffic UI is **Prometheus-only** (`GET /api/stats/window`); Live (`#/live`) is a fixed 1m lookback. It is not a NuGet package. The Console `/api/*` routes have **no application-level login** — protect the host.

These surfaces are for operators. Protect them with an API key and a private network. Details: [admin.md](admin.md). Orientation: [Guide — operations](guide/operations.md).

---

## Bus vs Redis backplane — which do I need?

| Goal | Prefer |
|------|--------|
| Shared Fusion L2 + automatic L1 drop on other nodes | **Redis** package (L2 + backplane) |
| Multi-instance **InMemory**, purge / Version / TTL on all nodes | **HttpBus** package |
| Both installed | Safe but often redundant for Fusion tag purge; HttpBus still useful for OC InMemory + runtime overlays |

HttpBus does **not** share cache payloads. Details: [cluster-bus.md](cluster-bus.md).

---

## Tracking query parameters

Known tracking keys are stripped from **cache keys / vary rules** so campaigns do not fragment the cache:
`utm_*`, click ids (`gclid`, `fbclid`, …), `_ga` / `_ga_*`, `_gl` / `_gl_*`.
They still reach your app on the request. (`_game` is not tracking.)

---

## Should I expose `X-Cache` in production?

`X-Cache` is a **diagnostic** header (domain, hit/miss, schedule phase, optional timing). It helps debugging and the sample playground; it also reveals internal cache state to any client that can see the response.

| Setting | Default | Recommendation |
|---------|---------|----------------|
| `Cache:EmitDiagnosticsHeaders` | `true` | Keep on for local/staging; consider `false` for public production APIs |

```json
"Cache": { "EmitDiagnosticsHeaders": false }
```

Turning it off does **not** disable metrics, tracing, health checks, or `Cache-Control` / ETag.  
See [observability.md](observability.md).

---

## Non-goals (by design)

- No automatic caching of non-GET/HEAD for Output Cache
- No ownership of Redis HA / failover topology beyond connection options
- No guarantee of cross-instance consistency when both layers are InMemory without a backplane
- Concrete service types are **internal** — depend on interfaces + DI

---

## Related

- [Guide](guide/README.md) — concepts, topologies, operations  
- [cache-keys.md](cache-keys.md) — FC/OC keys, Namespace, domain in key  
- [comparison.md](comparison.md) — when to use this vs hand-rolled cache  
- [architecture.md](architecture.md) — layers and public API surface  
- [configuration.md](configuration.md) — full settings reference  
