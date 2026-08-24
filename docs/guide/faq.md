# FAQ

> **Guide.** Product overview: [root README](../../README.md). Orientation: [Guide](README.md). Catalog: [documentation index](../README.md).

Short answers to common mistakes. Topic pages hold the full story.

## Scope

CacheOrchestrator configures and coordinates ASP.NET Output Cache, a **data cache** engine (FusionCache or HybridCache), and client Cache-Control under one **domain** model. It does not replace those engines or own Redis topology. Custom stores beyond InMemory/Redis are registrars you write — [backends](../reference/backends.md).

---

## First checks

### Why is a route cached when I never set a domain?

It should not be. CacheOrchestrator’s **base** Output Cache policy is **`NoCache`**. Full-response caching applies only with `.CacheOutputWithDomain` / `[CacheDomain]` (or your own explicit Output Cache policy).

If a route still looks cached, check for a host-added `AddBasePolicy` / `.CacheOutput(...)`, a CDN/browser cache, or a domain you forgot.

Details: [output-cache — base policy](../reference/output-cache.md#base-policy-and-endpoints-without-a-domain).

### Data cache runs uncached — why?

`IDomainDataCache.GetOrSetAsync` needs a **domain**:

1. Explicit overload `GetOrSetAsync(http, domain, factory)`
2. Options already on the request (usually from Output Cache policy)
3. Endpoint metadata (`.CacheOutputWithDomain` / `[CacheDomain]`)
4. Else the factory runs **without** data caching

On the unresolved path you get a Warning log, metric `result=unresolved`, and often `dc=unresolved` on `X-Cache`.

**Fix:** put the domain on the endpoint, or use the domain overload / `EnsureDomainOptions`.  
Details: [data cache](../reference/data-cache.md).

### Should I expose `X-Cache` in production?

`X-Cache` is a diagnostic header (domain, hit/miss, schedule phase). Useful locally; it also reveals cache state to any client that sees the response.

| Setting | Default | Recommendation |
|---------|---------|----------------|
| `Cache:EmitDiagnosticsHeaders` | `true` | On for local/staging; consider `false` for public production APIs |

Turning it off does **not** disable metrics, tracing, health checks, or `Cache-Control` / ETag.  
See [observability](../reference/observability.md).

---

## Authentication and vary

### Authenticated requests and API keys

**Default:** authenticated users **or** an `Authorization` header → Output Cache **bypassed**, client cache **blocked** (`AuthBypassMode: AuthenticatedOrAuthorization`).

| Goal | Config |
|------|--------|
| Keep safe default | leave flags as default |
| Cache private per-user responses | `AuthBypassMode: Never`, `VaryOutputCacheByUser: true`, `ClientCache.Cacheability: Private` |
| Public content that sends an API key | `AuthBypassMode: Never`, `VaryOutputCacheByUser: false`, careful `ClientCache.Cacheability` |
| Bypass only cookie/Identity auth | `AuthBypassMode: AuthenticatedIdentityOnly` |
| 2.1-like: data cache under Authorization while OC bypasses | `DataCacheRespectAuthBypass: false` (default `true` = OC↔DC parity) |

Wrong settings can leak one user’s response to another (especially with shared CDNs).  
Details: [vary](../reference/vary.md) · [output-cache](../reference/output-cache.md#authenticated-traffic).

### Why does the data cache skip under Authorization?

Default **`DataCacheRespectAuthBypass: true`**: when Output Cache would auth-bypass, the data cache also runs the factory uncached. Set `false` only if you intentionally want shared data-cache entries under Authorization while OC stays bypassed.

### JSON vs XML on the same URL?

Enable `VaryByAccept: true` (optional `AcceptNormalizationList`). Output Cache and the data cache then partition by normalized `Accept`. See [vary](../reference/vary.md).

### Tenant claim without a custom key generator?

Set `AuthBypassMode: Never`, `VaryOutputCacheByUser: true`, and `VaryByAuthClaims: [ "tenant_id" ]`, or register an `ICacheVaryContributor`.

---

## Freshness and ETags

### ETag = Version — is that a bug?

No. ETags are **generation-bound** (from the domain `Version`), not a hash of the response body.

With default `ETagMode: Version`, every URL in the domain shares the same ETag. After `InvalidateEntityAsync` without a Version bump, browsers that revalidate with `If-None-Match` can still get `304 Not Modified`.

For CRUD APIs where rows mutate under a stable Version, use `ETagMode: None` (or a custom body/timestamp ETag in the endpoint).

| Profile | Typical ETag mode |
|---------|-------------------|
| **Snapshot** (tiles, datasets) | `Version` or `Resource` |
| **Dynamic / CRUD** | `None` |

Details: [ETag modes](domain-profiles.md#etag-modes).

### Client Cache Schedule vs server TTL

`ClientCache.ScheduledUpdateUtc` and client TTL fields change only **browser/CDN** `Cache-Control`. They do **not** change `OutputCache.TtlSeconds` or `DataCache.TtlSeconds` / Fusion hard / fail-safe.

Phases on `X-Cache`: `phase=calm|approaching|hold|n/a`.  
See [Client Cache Schedule](client-cache-schedule.md).

---

## Packages and topology

### Redis package vs core

| Package | Contains |
|---------|----------|
| `CacheOrchestrator` (meta) | AspNetCore + Fusion data provider |
| Core / AspNetCore / FusionCache / HybridCache | Policy, HTTP host, Fusion or Hybrid provider — [packages](packages.md) |
| `CacheOrchestrator.Redis` | Redis registrar, connection options, health probe |
| `CacheOrchestrator.HttpBus` | HTTP cluster command bus |
| `CacheOrchestrator.EFCore.Invalidation` | SaveChanges → entity invalidation |

Without Redis + `AddRedisBackend()`, `"Provider": "Redis"` fails validation. Without HttpBus, multi-instance InMemory invalidation stays process-local. Without the EF package, `SaveChanges` does not purge cache.

### Bus vs Redis backplane — which do I need?

| Goal | Prefer |
|------|--------|
| Shared Fusion L2 + L1 drop on other nodes | **Redis** (L2 + backplane) |
| Multi-instance **InMemory** purge / Version / TTL | **HttpBus** |
| Both installed | Safe; often redundant for Fusion tag purge; HttpBus still useful for OC InMemory + runtime overlays |

HttpBus does **not** share cache payloads. Details: [cluster bus](../reference/cluster-bus.md) · [topologies](topologies.md).

### Multiple Redis clusters / named data-cache instances

Map domains to named `DataCacheInstances`, each with its own Redis connection. Domains select an instance via `DataCache.Instance`.

```json
"DataCacheInstances": {
  "default": { "Provider": "Redis", "Redis": { "Configuration": "global:6379" } },
  "pii":     { "Provider": "Redis", "Redis": { "Configuration": "secure:6379" } }
}
```

Requires `CacheOrchestrator.Redis` + `AddRedisBackend()`.  
Details: [deployment](../reference/deployment.md#using-multiple-datacache-instances).

### Namespace defaults

| Setting | Default behaviour |
|---------|-------------------|
| Root `Namespace` | `app-cache` |
| Output Cache keys | `OutputCache.Namespace` ?? `{Namespace}-oc` |
| Data-cache `default` instance | `{Namespace}-fc` (historical `-fc`; no `-default` suffix) |
| Data-cache named instance `pii` | `{Namespace}-fc-pii` |

### Custom backends (SQL Server, Memcached, …)

Implement `ICacheBackendRegistrar`, register it, set `Provider` under `OutputCache` or `DataCacheInstances`. Example (Fusion L2 on SQL Server): [backends](../reference/backends.md).

---

## Invalidation and EF

### EF Core `ExecuteUpdate` did not invalidate cache

The interceptor only sees `ChangeTracker` entries after a successful `SaveChanges`. Bulk `ExecuteUpdate` / `ExecuteDelete` / `ExecuteInsert` never produce those entries.

Call `InvalidateEntitiesAsync` or `InvalidateEntityKindAsync` yourself. Details: [EF Core invalidation](../reference/ef-core-invalidation.md).

### Tracking query parameters

Known tracking keys are stripped from **cache keys / vary rules** so campaigns do not fragment the cache: `utm_*`, click ids (`gclid`, `fbclid`, …), `_ga` / `_ga_*`, `_gl` / `_gl_*`. They still reach your app on the request. (`_game` is not tracking.)

---

## Admin

### Admin API vs Admin Console App

- **Admin API** — opt-in HTTP on **each** process (`Cache:Admin:Enabled` + `MapCacheOrchestratorAdmin`). Health, config, invalidate, runtime Version/settings. Process-lifetime `GET …/stats` is obsolete for analytics.
- **Admin Console App** — separate process that fans out to those APIs. Traffic UI is **Prometheus-only**. Not a NuGet package. No built-in login — protect the host.

Details: [admin](../reference/admin.md) · [operations](operations.md).

---

## Non-goals (by design)

- No automatic Output Cache for non-GET/HEAD
- No ownership of Redis HA / failover beyond connection options
- No cross-instance consistency when both layers are InMemory without a backplane or bus
- Concrete service types are **internal** — depend on interfaces + DI

---

## Related

- [Guide](README.md)  
- [Packages](packages.md) · [Composition how-to](../how-to/composition.md)  
- [Configuration](../reference/configuration.md)  
- [Comparison](comparison.md)  
