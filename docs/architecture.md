# Architecture

## Purpose

**CacheOrchestrator** is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

It is a **configuration-driven orchestration layer** on top of:

1. **ASP.NET Core Output Caching** — store full GET/HEAD responses  
2. **ZiggyCreatures FusionCache** — hybrid L1 (memory) + optional L2 (Redis) for application objects (with full support for **multiple named instances** for isolation)  
3. **Client Cache-Control (CC)** — browser/CDN headers (including optional Client Cache Schedule)

Both systems share the idea of a **domain**: a named group of data (`products`, `reports`, …) with its own TTLs, flags, and version stamp.

## Design principles

- **Configuration over code** — change TTLs and providers without redeploying business logic where possible  
- **One domain model** — Output Cache and FusionCache resolve the same `DomainCacheOptions`  
- **Safe defaults** — fail-safe, stampede protection, jitter come from FusionCache + sensible domain defaults  
- **Observable** — optional `X-Cache` header (`EmitDiagnosticsHeaders`), metrics meter `CacheOrchestrator`, activity source `CacheOrchestrator`
- **Library-friendly** — `ConfigureAwait(false)`, sealed types, validated options on start  

## High-level diagram

```
┌──────────────────────────── Application ────────────────────────────┐
│  Minimal APIs  ·  Controllers  ·  Invalidation endpoints              │
└───────────────┬──────────────────────────────┬──────────────────────┘
                │                              │
                ▼                              ▼
     DomainOutputCachePolicy          IDomainFusionCache
     (IOutputCachePolicy)             DomainFusionCacheService
                │                              │
                ▼                              ▼
     ASP.NET Output Cache              FusionCache (L1 ± L2 ± backplane)
                │                              │
                └──────────┬───────────────────┘
                           ▼
              IDomainCacheOptionsProvider
              (HttpContext.Items + ConcurrentDictionary)
                           │
                           ▼
              CacheOrchestratorOptions (IOptionsMonitor)
```

## Source layout (`src/CacheOrchestrator`)

| Folder | Responsibility |
|--------|----------------|
| `Configuration/` | Options, domain resolution, client headers, `X-Cache` |
| `OutputCache/` | Policy, `[CacheDomain]`, Minimal API extensions, MVC convention |
| `FusionCache/` | `IDomainFusionCache`, key generator, service |
| `Backends/` | InMemory / Redis registration strategy (`ICacheBackendRegistrar`) |
| `Invalidation/` | Tag-based eviction across OC + FC |
| `Cluster/` | Command bus contracts, Null bus/membership, InstanceId, handler (HTTP in Bus package) |
| `Admin/` | Local Admin API (feature-flagged) |
| `Diagnostics/` | Metrics, activities, health probes |
| `DependencyInjection/` | `AddCacheOrchestrator`, `UseCacheOrchestrator`, `MapCacheOrchestratorAdmin` |
| `Utilities/` | Domain templates, HTTP helpers |

Companion packages:

| Project | Role |
|---------|------|
| `CacheOrchestrator.Redis` | Redis OC store + Fusion L2 + backplane |
| `CacheOrchestrator.Bus` | HTTP cluster command bus + Static / ServiceDiscovery membership |
| `CacheOrchestrator.EFCore.Invalidation` | SaveChanges interceptor → entity invalidation (not an EF cache) — [ef-core-invalidation.md](ef-core-invalidation.md) |
| `CacheOrchestrator.Admin` | Admin App host (fan-out UI; not a NuGet package) |

Interfaces live **next to** their implementations (no separate `Abstractions` assembly/folder).

## Public API surface (1.0 stability)

Prefer **interfaces + DI entry points** over concrete services. Implementations are `internal` so they can change without a major version bump.

| Public (stable contract) | Internal (not for app code) |
|--------------------------|-----------------------------|
| `AddCacheOrchestrator` / `UseCacheOrchestrator` / `ICacheOrchestratorBuilder` | `DefaultCacheOrchestratorBuilder` |
| `IDomainFusionCache`, `IDomainKeyGenerator`, `DefaultDomainKeyGenerator` | `DomainFusionCacheService` |
| `IDomainCacheOptionsProvider`, `DomainCacheOptions`, `DomainName`, options types | `DomainCacheOptionsProvider`, `CacheOrchestratorOptionsValidator` |
| `ICacheOrchestratorInvalidator`, `CacheInvalidationResult`, `ICacheInvalidationObserver`, `CacheTags` | `CacheOrchestratorInvalidator` |
| `IClusterCommandBus`, `IClusterMembership`, `IClusterCommandHandler`, `IInstanceIdProvider`, command records | Null implementations / `DefaultClusterCommandHandler` |
| `ICacheBackendRegistrar`, `InMemoryCacheBackendRegistrar` | — |
| Redis: `AddRedisBackend` / `RedisCacheBackendRegistrar` (**CacheOrchestrator.Redis**) | `RedisCacheHealthProbe` |
| Bus: `AddHttpClusterBus` / `MapCacheOrchestratorHttpBus` / `HttpClusterCommandBus` (**CacheOrchestrator.Bus**) | `ClusterEndpointAuth` |
| `MapCacheOrchestratorAdmin` / Local Admin DTOs (when Admin enabled) | `InMemoryAdminStatsCollector`, `AdminLocalApi`, … |
| `DomainOutputCachePolicy`, `[CacheDomain]`, endpoint extensions | `CacheDomainConvention` |
| Health: `AddCacheOrchestrator()`, `ICacheOrchestratorHealthProbe` | `CacheOrchestratorHealthCheck` |
| Meter/activity **names** (`CacheOrchestrator`) | `CacheOrchestratorMetrics.Record*` |

`HttpContext.Items` keys live on **`CacheOrchestratorKeys`** (`DomainOptionsKey`, `ResourceIdKey`, `EntityKindKey`, `DispositionKey`).

## Request flow — Output Cache

1. Request hits an endpoint with domain metadata (`.CacheOutputWithDomain(...)` or `[CacheDomain]`).  
2. `DomainOutputCachePolicy.CacheRequestAsync` runs.  
3. Domain is resolved (fixed string, func, or template).  
4. `EnsureDomainOptions` loads effective config (request L1 → process L2 → bind from options).  
5. Policy enables/disables lookup/storage, sets vary rules, tags `domain:{name}`, TTL.  
6. On hit → `ServeFromCacheAsync` marks disposition.  
7. On response start → client `Cache-Control` + `X-Cache` headers.

**Not cached:** non-GET/HEAD, `Cache-Control: no-store`, authenticated / `Authorization`, disabled domain, non-cacheable status codes, `Set-Cookie` responses.

## Request flow — FusionCache

1. Code calls `IDomainFusionCache.GetOrSetAsync` (domain usually already on the request from the Output Cache policy, or resolved from endpoint metadata / explicit domain overload).  
2. If domain still missing or Fusion disabled → factory runs uncached.  
3. Optional respect for request `no-store`.  
4. Key from `IDomainKeyGenerator` (route/query/encoding/host + domain + version).  
5. Domain config determines which **named FusionCache instance** to use (`default` by default).
6. FusionCache L1 → L2 → factory with soft/hard factory timeouts, fail-safe, jitter.  
7. Disposition (`Hit` / `Miss` / `Stale` / …) stored for `X-Cache`.  

See [fusion-cache.md](fusion-cache.md) for resolution order and the Fusion-only scenario.  

## Backends

| Provider | Package | Output Cache | FusionCache |
|----------|---------|--------------|-------------|
| `InMemory` | **Core** | ASP.NET default store | L1 only |
| `Redis` | **`CacheOrchestrator.Redis`** | StackExchange Redis store | Keyed L2 + Redis backplane per instance |
| *(Custom)* | Your app | Custom | Keyed L2 recommended for multi-instance |

Register Redis with `AddRedisBackend()` (see [backends.md](backends.md)). Custom backends use `ICacheBackendRegistrar` + `AddBackend`.

Output and Fusion providers can differ (e.g. OC in-memory, FC Redis).

**Multi-instance Redis:** each `FusionCacheInstances` entry gets a keyed `IConnectionMultiplexer` and keyed `IDistributedCache` under the instance name. Do not share one global `IDistributedCache` across named caches — the last registration would win and mis-route L2 writes.

## Related

- [cluster-bus.md](cluster-bus.md)  
- [cache-keys.md](cache-keys.md)  
- [configuration.md](configuration.md)  
- [output-cache.md](output-cache.md)  
- [fusion-cache.md](fusion-cache.md)  
- [deployment.md](deployment.md)  
