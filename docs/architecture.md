# Architecture

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — concepts](guide/concepts.md). Catalog: [documentation index](README.md). Packages: [packages.md](packages.md).

How the library is put together.

A **domain** is a named group of data (`products`, `reports`, …) with its own TTLs, flags, and Version. Output Cache, the **data cache** (`IDataCacheProvider`), and client headers all resolve the same `DomainCacheOptions`.

1. **ASP.NET Core Output Caching** — full GET/HEAD responses (AspNetCore package).
2. **Data cache** — objects from your factory via `ICacheOrchestrator` / `IDomainFusionCache` (Fusion or Hybrid as `IDataCacheProvider`; L1 memory, optional L2 / backplane for Fusion).
3. **Client Cache-Control** — browser and CDN headers, including Client Cache Schedule.

## Design principles

- **Configuration over code** — change TTLs and providers without redeploying handlers.
- **One domain model** — Output Cache and the data cache share `DomainCacheOptions`.
- **Pluggable engines** — Core owns portable `DataCache` policy; Fusion / Hybrid packages supply `IDataCacheProvider`.
- **Safe defaults** — fail-safe, stampede protection, and jitter come from Fusion (when that provider is registered) and the domain defaults.
- **Observable** — `X-Cache` (when enabled), meter and activity source `CacheOrchestrator`.

## High-level diagram

```
┌──────────────────────────── Application ────────────────────────────┐
│  Minimal APIs  ·  Controllers  ·  Workers / libraries                 │
└───────────────┬──────────────────────────────┬──────────────────────┘
                │                              │
                ▼                              ▼
     DomainOutputCachePolicy          IDomainFusionCache (HTTP)
     (IOutputCachePolicy)             └─► ICacheOrchestrator (Core)
                │                              │
                ▼                              ▼
     ASP.NET Output Cache              IDataCacheProvider
                                       (FusionDataCacheProvider
                                        or HybridDataCacheProvider)
                │                              │
                └──────────┬───────────────────┘
                           ▼
              IDomainCacheOptionsProvider
              (ICacheOrchestratorFeature + ConcurrentDictionary)
                           │
                           ▼
              CacheOrchestratorOptions (IOptionsMonitor)
              DataCacheInstances + nested domain sections
```

## Source layout (`src/`)

| Project | Responsibility |
|---------|----------------|
| `CacheOrchestrator.Core` | Domains, Version, portable `DataCache` / nested settings, entity footprint, `ICacheOrchestrator`, invalidation and cluster **contracts**, diagnostics |
| `CacheOrchestrator.FusionCache` | ZiggyCreatures Fusion as `IDataCacheProvider`; JSON `FusionCache` knobs |
| `CacheOrchestrator.HybridCache` | Microsoft HybridCache as `IDataCacheProvider` |
| `CacheOrchestrator.AspNetCore` | Output Cache, Client Cache-Control, vary, Admin API, `IDomainFusionCache`, host `AddCacheOrchestrator` |
| `CacheOrchestrator` | Meta NuGet: AspNetCore + FusionCache |
| `CacheOrchestrator.Redis` | Redis OC store + Fusion L2 + backplane |
| `CacheOrchestrator.HttpBus` | HTTP cluster command bus + Static / ServiceDiscovery membership |
| `CacheOrchestrator.EFCore.Invalidation` | SaveChanges interceptor → entity invalidation — [ef-core-invalidation.md](ef-core-invalidation.md) |
| `CacheOrchestrator.AdminConsole` | Admin Console App (operator UI); not a NuGet package |

Dependency rule: arrows point at **Core**. Core never references ASP.NET, Fusion, Hybrid, Redis, HttpBus, or EF. Details: [packages.md](packages.md).

## Public API surface

Prefer **interfaces and DI entry points**. Concrete services are `internal`.

| Public (stable contract) | Internal (not for app code) |
|--------------------------|-----------------------------|
| `AddCacheOrchestrator` / `UseCacheOrchestrator` / `ICacheOrchestratorBuilder` | `DefaultCacheOrchestratorBuilder` |
| `ICacheOrchestrator`, `IDataCacheProvider` (**Core**) | Orchestrator / provider implementations |
| `IDomainFusionCache`, `IDomainKeyGenerator`, `DefaultDomainKeyGenerator` | `DomainFusionCacheService` |
| `IDomainCacheOptionsProvider`, `DomainCacheOptions`, `DomainName`, `ICacheOrchestratorFeature`, options types | `DomainCacheOptionsProvider`, `CacheOrchestratorOptionsValidator`, `CacheOrchestratorFeature` |
| `ICacheOrchestratorInvalidator`, `CacheInvalidationResult`, `ICacheInvalidationObserver`, `CacheTags` | `CacheOrchestratorInvalidator` |
| `IClusterCommandBus`, `IClusterMembership`, `IClusterCommandHandler`, `IInstanceIdProvider`, command records (`InvalidateCommand`, `VersionBumpCommand`, `TtlPatchCommand`, `SettingsPatchCommand`, …) | `DefaultClusterCommandHandler` |
| `NullClusterCommandBus`, `NullClusterMembership` | — |
| `ICacheBackendRegistrar`, `InMemoryCacheBackendRegistrar` | — |
| Redis: `AddRedisBackend` / `RedisCacheBackendRegistrar` (**CacheOrchestrator.Redis**) | `RedisCacheHealthProbe` |
| HttpBus: `AddHttpClusterBus` / `MapCacheOrchestratorHttpBus` / `HttpClusterCommandBus` (**CacheOrchestrator.HttpBus**) | `ClusterEndpointAuth` |
| `MapCacheOrchestratorAdmin`, `AdminLocalApi`, Admin API DTOs | `InMemoryAdminStatsCollector` |
| `AuthBypassMode`, `DomainAuthEvaluator` | — |
| `ICacheVaryContributor`, `CacheVaryMaterializer`, `ICacheVaryBuilder` | — |
| `DomainOutputCachePolicy`, `[CacheDomain]`, `CacheOutputWithDomain` / `CacheOutputWithDomainTemplate` / `CacheOutputWithDomainAttribute` | `CacheDomainConvention` |
| Health: `AddCacheOrchestrator()`, `ICacheOrchestratorHealthProbe` | `CacheOrchestratorHealthCheck` |
| Meter/activity **names** (`CacheOrchestrator`) | `CacheOrchestratorMetrics.Record*` |

Request state lives on **`ICacheOrchestratorFeature`** via `HttpContext.Features` (domain options, entity identity, disposition, pending footprint). Prefer `http.GetDomainCacheOptions()` for the resolved snapshot. The old `CacheOrchestratorKeys` / `HttpContext.Items` slots were removed.

## Request flow — Output Cache

1. Request hits an endpoint with domain metadata (`.CacheOutputWithDomain(...)` or `[CacheDomain]`).  
2. `DomainOutputCachePolicy.CacheRequestAsync` runs.  
3. Domain is resolved (fixed string, func, or template).  
4. `EnsureDomainOptions` loads effective config (request L1 → process L2 → bind from options).  
5. Policy enables/disables lookup/storage, sets vary rules, tags `domain:{name}`, TTL.  
6. On hit → `ServeFromCacheAsync` marks disposition.  
7. On response start → client `Cache-Control` + `X-Cache` headers.

**Not cached:** non-GET/HEAD, `Cache-Control: no-store`, authenticated / `Authorization`, disabled domain, non-cacheable status codes, `Set-Cookie` responses.

## Request flow — data cache

1. Code calls `IDomainFusionCache.GetOrSetAsync` or Core `ICacheOrchestrator.GetOrCreateAsync`.  
2. If domain still missing or data cache disabled → factory runs uncached.  
3. Optional respect for request `no-store` / auth bypass.  
4. Key from `IDomainKeyGenerator` (HTTP) or caller-supplied key (orchestrator).  
5. Domain config selects a named **`DataCacheInstances`** entry (`default` by default).  
6. Registered `IDataCacheProvider` (Fusion: L1 → L2 → factory with soft/hard timeouts, fail-safe, jitter; Hybrid: expiration from `DataCache.Ttl`).  
7. Disposition (`Hit` / `Miss` / `Stale` / …) stored for `X-Cache` (`dc=`).  

See [fusion-cache.md](fusion-cache.md) for HTTP resolution order and entity identity.  

## Backends

| Provider | Package | Output Cache | Data cache (Fusion path) |
|----------|---------|--------------|--------------------------|
| `InMemory` | Core / host | ASP.NET default store | L1 only |
| `Redis` | **`CacheOrchestrator.Redis`** | StackExchange Redis store | Keyed L2 + Redis backplane per instance |
| *(Custom)* | Your app | Custom | Keyed L2 recommended for multi-instance |

Register Redis with `AddRedisBackend()` (see [backends.md](backends.md)). Custom backends use `ICacheBackendRegistrar` + `AddBackend`.

Output and data-cache providers can differ (e.g. OC in-memory, Fusion Redis).

**Multi-instance Redis:** each `DataCacheInstances` entry gets a keyed `IConnectionMultiplexer` and keyed `IDistributedCache` under the instance name. Do not share one global `IDistributedCache` across named caches — the last registration would win and mis-route L2 writes.

## Related

- [packages.md](packages.md)  
- [Guide — concepts](guide/concepts.md)  
- [cluster-bus.md](cluster-bus.md)  
- [cache-keys.md](cache-keys.md)  
- [configuration.md](configuration.md)  
- [output-cache.md](output-cache.md)  
- [fusion-cache.md](fusion-cache.md)  
- [vary.md](vary.md)  
- [deployment.md](deployment.md)  
