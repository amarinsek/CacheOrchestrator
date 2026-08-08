# Comparison: CacheOrchestrator vs alternatives

**CacheOrchestrator** is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

When to use this library versus hand-rolled ASP.NET Core Output Cache, raw FusionCache, or Redis-only response caching.

---

## At a glance

| Approach | Best for | You still own |
|----------|----------|---------------|
| **CacheOrchestrator** | Many domains with different TTLs, client schedule, shared config, multi-tier invalidation | Domain modelling, Version/cutover process, Redis ops (if used) |
| **Manual Output Cache + FusionCache** | One or two endpoints, full control, no domain model needed | Every policy, key, tag, header, and wiring |
| **Redis Output Cache only** | Shared full-response store across instances; no app-level object cache | Client headers, app data cache, domain versioning, Fusion fail-safe |

---

## vs hand-rolled Output Cache + FusionCache

**Without CacheOrchestrator** you typically:

```csharp
// Scattered TTLs, headers, tags, and key rules
builder.Services.AddOutputCache(o => { /* global policies */ });
builder.Services.AddFusionCache().With...();

app.MapGet("/tiles/...", async (IFusionCache fc) => { /* manual keys */ })
   .CacheOutput("tiles-policy");
// + manual Cache-Control, ETag, auth bypass, invalidation helpers…
```

**With CacheOrchestrator** you:

1. Declare domains in `appsettings` (TTLs, Version, client schedule, auth flags, Fusion instance)
2. Tag endpoints: `.CacheOutputWithDomain("osm-tiles")` / `[CacheDomain("…")]`
3. Call `IDomainFusionCache.GetOrSetAsync` (domain often already on the request)
4. Invalidate with `ICacheOrchestratorInvalidator` (domain / entity / tags)

| Concern | Manual | CacheOrchestrator |
|---------|--------|-------------------|
| Per-domain TTLs | Many named policies or magic numbers | One domain entry in config |
| Client `max-age` near cutover | Hand-written or forgotten | **Client Cache Schedule** |
| Fusion + OC same domain | Duplicate config | Shared `DomainCacheOptions` |
| Auth bypass / per-user vary | Easy to get wrong | Defaults + explicit flags |
| Tags `domain:` / `entity:` | Manual string conventions | Built-in |
| Multi Redis for PII vs catalog | Easy to mis-wire L2 | Named instances + keyed L2 |
| `X-Cache` diagnostics | Optional DIY | Built-in |

**Choose manual** if you have a single cache profile and want zero abstractions.

**Choose CacheOrchestrator** if domains multiply (maps, catalog, live, PII) and config-driven cutovers matter.

---

## vs Redis Output Cache alone

ASP.NET Core can store Output Cache entries in Redis (shared HTTP responses across instances). That is **only L0**.

| Need | Redis OC alone | CacheOrchestrator |
|------|----------------|-------------------|
| Shared full HTTP responses | Yes | Yes (`OutputCache.Provider: Redis` via Redis package) |
| In-process object cache (L1) | No | FusionCache L1 |
| Shared object cache (L2) + fail-safe | No | Fusion L2 + backplane |
| Domain Version bulk abandon | Manual key design | `Version` stamp in keys / vary |
| Browser schedule to cutover | Manual headers | Client Cache Schedule |
| Entity-level purge after CRUD | Manual tags | `InvalidateEntityAsync` |

Hybrid is common: **Output Cache InMemory** (fast L0) + **Fusion Redis** (shared objects + backplane). See [deployment.md](deployment.md).

---

## vs “just use FusionCache”

FusionCache is excellent for **application data**. CacheOrchestrator **uses** it; it does not replace it.

You still need CacheOrchestrator when you want:

- Output Cache policies aligned with the same domains
- Client Cache Schedule
- Unified invalidation API across OC tags + Fusion tags
- Config-bound multi-domain operations without repeating entry options

If you only need `GetOrSet` in a worker with no HTTP, use FusionCache directly.

---

## Custom backends vs first-party Redis

| | First-party Redis package | Custom `ICacheBackendRegistrar` |
|--|---------------------------|----------------------------------|
| Install | `CacheOrchestrator.Redis` + `AddRedisBackend()` | Your package + `AddBackend(...)` |
| Config | `Provider: Redis` + `Cache:Redis` | `Provider: YourName` + your config section |
| Output Cache | Redis OC store supported | You wire `AddStackExchangeRedisOutputCache` / SQL / etc. |
| Fusion L2 | Keyed distributed cache + backplane | You call Fusion builder APIs yourself |

**Important:** `"Provider": "SqlServer"` (or Memcached, Cosmos, …) is **not** a built-in drop-in. It only works after you register a registrar that implements that provider name. See [backends.md](backends.md).

---

## Decision cheat sheet

```text
Need multi-domain HTTP + object cache + client schedule?
  └─ Yes → CacheOrchestrator
Need only one GetOrSet in a background service?
  └─ Yes → FusionCache alone
Need only shared full HTTP responses, no object cache?
  └─ Yes → ASP.NET Output Cache + Redis store (or CO with Fusion off)
Need SQL / custom L2?
  └─ Implement ICacheBackendRegistrar (Redis package is the reference)
```

## Related

- [faq.md](faq.md) — limitations and gotchas  
- [architecture.md](architecture.md)  
- [backends.md](backends.md)  
