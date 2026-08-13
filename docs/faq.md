# FAQ and limitations

Short answers to common “gotchas” when adopting CacheOrchestrator.  
Deep dives stay in the topic docs; this page is the **limitations map**.

---

## What this library is (and is not)

| It is | It is not |
|-------|-----------|
| Domain-based caching for ASP.NET Core (Output Cache + FusionCache + client Cache-Control) | A generic cache façade for non-HTTP apps without `HttpContext` |
| Configuration and scoping around FusionCache | A replacement for FusionCache features |
| Pluggable storage via `ICacheBackendRegistrar` | An ops product that owns Redis topology for you |
| One domain model across HTTP responses, app data, and client headers | Drop-in SQL Output Cache “just set Provider” without a registrar |

---

## EF Core `ExecuteUpdate` did not invalidate cache

The interceptor only sees `ChangeTracker` (`Added` / `Modified` / `Deleted`) after a successful `SaveChanges`. Bulk `ExecuteUpdate` / `ExecuteDelete` / `ExecuteInsert` never produce those entries.

Call `InvalidateEntitiesAsync` or `InvalidateEntityKindAsync` yourself. Details: [ef-core-invalidation.md](ef-core-invalidation.md).

---

## Fusion runs uncached — why?

`IDomainFusionCache.GetOrSetAsync` needs a **domain**. Resolution order:

1. Options already on the request (usually set by Output Cache policy)
2. Explicit overload `GetOrSetAsync(http, domain, factory)`
3. Endpoint metadata (`.CacheOutputWithDomain` / `[CacheDomain]`)
4. Else: factory runs **without** Fusion caching

On the unresolved path the library:

- Logs a **Warning** (`FusionCache skipped: no domain resolved…`)
- Records metric `result=unresolved` with `domain=_`
- Sets disposition `data=unresolved` when headers are still written

**Fix:** put domain on the endpoint, or use the domain overload / `EnsureDomainOptions`.  
Details: [fusion-cache.md](fusion-cache.md).

---

## Authenticated requests and API keys

**Default:** authenticated users **or** an `Authorization` header → Output Cache **bypassed**, client cache **blocked** (`BypassWhenAuthenticated: true`).

| Goal | Config |
|------|--------|
| Keep safe default | leave flags as default |
| Cache private per-user responses | `BypassWhenAuthenticated: false`, `VaryOutputCacheByUser: true`, `ClientCacheability: Private` |
| Public content that happens to send an API key | `BypassWhenAuthenticated: false`, `VaryOutputCacheByUser: false`, careful `ClientCacheability` |

Wrong settings can leak one user’s response to another (especially with shared CDNs).  
Details: [output-cache.md](output-cache.md#authenticated-caching-optional), [domain-profiles.md](domain-profiles.md#authenticated-traffic-auth-bypass).

---

## ETag = Version — is that a bug?

No. Default `ETagMode: Version` means the HTTP `ETag` is a **domain generation stamp** (from `Version`), not a hash of the response body.

| Profile | Typical ETag mode |
|---------|-------------------|
| Snapshot datasets (OSM tiles, monthly maps) | `Version` — all tiles share one generation validator |
| Dynamic CRUD under a stable Version | `Resource` (per URL/id) or `None` with short client TTL |

If product #42 changes and you only bump content under the same `Version` without invalidation, caches **correctly** keep serving the old body until TTL or tag purge.  
See [domain-profiles.md](domain-profiles.md).

---

## Multiple Redis clusters / named Fusion instances

Supported: map domains to named `FusionCacheInstances`, each with its own Redis connection.

```json
"FusionCacheInstances": {
  "default": { "Provider": "Redis", "Redis": { "Configuration": "global:6379" } },
  "pii":     { "Provider": "Redis", "Redis": { "Configuration": "secure:6379" } }
}
```

Each instance gets a **keyed** `IDistributedCache` and multiplexer — the last registration must not overwrite L2 for others.

**Requires:** package `CacheOrchestrator.Redis` + `AddRedisBackend()`.  
Details: [deployment.md](deployment.md#using-multiple-fusioncache-instances).

---

## Namespace defaults

| Setting | Default behaviour |
|---------|-------------------|
| Root `Namespace` | `app-cache` |
| Output Cache keys | `OutputCache.Namespace` ?? `{Namespace}-oc` |
| Fusion `default` instance | `{Namespace}-fc` (**no** `-default` suffix) |
| Fusion named instance `pii` | `{Namespace}-fc-pii` |

---

## Custom backends (SQL Server, Memcached, …)

`ICacheBackendRegistrar` lets you plug **your** Output Cache store and/or Fusion L2.

**Not** a drop-in:

- Setting `"Provider": "SqlServer"` alone does nothing until you register a registrar that implements that name
- You must wire the correct ASP.NET / FusionCache extensions yourself (connection, serializer, etc.)
- There is no first-party SQL Output Cache package in this repo

See [backends.md](backends.md) and [comparison.md](comparison.md).

---

## Client Cache Schedule vs server TTL

`ScheduledUpdateUtc` + client TTL fields change only **browser/CDN** `Cache-Control` (`max-age` ramp).  
They do **not** change Output Cache or Fusion soft/hard TTLs.

Phases appear on `X-Cache` as `phase=calm|approaching|hold|n/a`.  
See [client-cache-schedule.md](client-cache-schedule.md).

---

## Redis package vs core

| Package | Contains |
|---------|----------|
| `CacheOrchestrator` | Policy, InMemory, domain APIs, Null cluster bus |
| `CacheOrchestrator.Redis` | Redis registrar, connection options, Redis health probe |
| `CacheOrchestrator.Bus` | HTTP cluster command bus, Static / ServiceDiscovery membership |

Without Redis package + `AddRedisBackend()`, `"Provider": "Redis"` fails validation.  
Without Bus package, multi-instance InMemory invalidation stays process-local (unless you build your own fan-out).

---

## Local Admin vs Admin App

| Piece | What it is |
|-------|------------|
| **Local Admin API** | Opt-in HTTP on **each** app process (`Cache:Admin:Enabled` + `MapCacheOrchestratorAdmin`). Stats, health, invalidate, runtime Version/TTL. Ships in core NuGet; **off by default**. |
| **Admin App** | Separate process (`src/CacheOrchestrator.Admin`) — SPA + fan-out (or bus-distribute) to Local Admins. **Not** a NuGet package. |

Admin is for **operators**, not end-user traffic. Protect with API key + private network. Guide: [admin.md](admin.md).

---

## Bus vs Redis backplane — which do I need?

| Goal | Prefer |
|------|--------|
| Shared Fusion L2 + automatic L1 drop on other nodes | **Redis** package (L2 + backplane) |
| Multi-instance **InMemory**, purge / Version / TTL on all nodes | **Bus** package |
| Both installed | Safe but often redundant for Fusion tag purge; Bus still useful for OC InMemory + runtime overlays |

Bus does **not** share cache payloads. Details: [cluster-bus.md](cluster-bus.md).

---

## Tracking query parameters

Known tracking prefixes (`utm_*`, `gclid`, `fbclid`, …) are stripped from **cache keys / vary rules** so campaigns do not fragment the cache.  
They still reach your app on the request.

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

- [cache-keys.md](cache-keys.md) — FC/OC keys, Namespace, domain in key  
- [comparison.md](comparison.md) — when to use this vs hand-rolled cache  
- [architecture.md](architecture.md) — layers and public API surface  
- [configuration.md](configuration.md) — full settings reference  
