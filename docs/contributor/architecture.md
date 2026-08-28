# Architecture

> **Contributor.** Product overview: [root README](../../README.md). Orientation: [Guide — concepts](../guide/concepts.md). Catalog: [documentation index](../README.md). Packages: [packages.md](../guide/packages.md).

How the library is put together.

A **domain** is a named group of data (`products`, `reports`, …) with its own TTLs, flags, and Version. Core resolves its HTTP-free identity and Data Cache policy as `DomainCacheOptions`. ASP.NET Core composes that snapshot into `DomainHttpCacheOptions` for Output Cache, Client Cache, authentication, vary, ETag, and HTTP key policy.

1. **ASP.NET Core Output Caching** — full HTTP responses; GET/HEAD + Url without identity bindings, other methods via endpoint [cache identity](../reference/cache-identity.md) (AspNetCore package).
2. **Data Cache** — objects from your factory via `ICacheOrchestrator` / `IDomainDataCache` (Fusion or Hybrid as `IDataCacheProvider`; L1 memory, optional L2 / backplane for Fusion).
3. **Client Cache** — browser and CDN headers, including Client Cache Schedule.

## Design principles

- **Configuration over code** — change TTLs and providers without redeploying handlers.
- **One domain model, package-owned snapshots** — HTTP policy wraps the Core snapshot instead of extending Core with ASP.NET concerns.
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
     DomainOutputCachePolicy          IDomainDataCache (HTTP)
     (IOutputCachePolicy)             └─► ICacheOrchestrator (Core)
                │                              │
                ▼                              ▼
     ASP.NET Output Cache              IDataCacheProvider
                                       (FusionDataCacheProvider
                                        or HybridDataCacheProvider)
                │                              │
                └──────────┬───────────────────┘
                           ▼
              IDomainCacheOptionsProvider (Core)
                           │
                           ▼
                  DomainCacheOptions
                           │
                           ▼
              IRequestDomainCacheOptions (AspNetCore)
              (ICacheOrchestratorFeature + process cache)
                           │
                           ▼
                 DomainHttpCacheOptions
```

## Source layout (`src/`)

| Project | Responsibility |
|---------|----------------|
| `CacheOrchestrator.Core` | Domains, Version, portable `DataCache` / nested settings, entity footprint, `ICacheOrchestrator`, invalidation and cluster **contracts**, diagnostics |
| `CacheOrchestrator.FusionCache` | ZiggyCreatures Fusion as `IDataCacheProvider`; JSON `FusionCache` knobs |
| `CacheOrchestrator.HybridCache` | Microsoft HybridCache as `IDataCacheProvider` |
| `CacheOrchestrator.AspNetCore` | Output Cache, Client Cache, vary, Admin API, `IDomainDataCache`, host `AddCacheOrchestrator` |
| `CacheOrchestrator` | Meta NuGet: AspNetCore + FusionCache |
| `CacheOrchestrator.Redis` | Redis Output Cache store + Fusion L2 + backplane |
| `CacheOrchestrator.HttpBus` | HTTP cluster command bus + Static / ServiceDiscovery membership |
| `CacheOrchestrator.EFCore.Invalidation` | SaveChanges interceptor → entity invalidation — [ef-core-invalidation.md](../reference/ef-core-invalidation.md) |
| `CacheOrchestrator.AdminConsole` | Admin Console App (operator UI); not a NuGet package |

Dependency rule: arrows point at **Core**. Core never references ASP.NET, Fusion, Hybrid, Redis, HttpBus, or EF. Details: [packages.md](../guide/packages.md).

## Public API surface

Prefer **interfaces and DI entry points**. Concrete services are `internal`.

| Public (stable contract) | Internal (not for app code) |
|--------------------------|-----------------------------|
| `AddCacheOrchestratorCore` / `ICacheOrchestrator` / `IDataCacheProvider` (**Core**) | Core host registration / orchestrator / provider boundary |
| `AddCacheOrchestrator` / `AddCacheOrchestratorAspNetCore` / `UseCacheOrchestrator` / `ICacheOrchestratorBuilder` | ASP.NET Core composition and `DefaultCacheOrchestratorBuilder` |
| `IDomainDataCache`, `IDomainKeyGenerator`, `DefaultDomainKeyGenerator` | `DomainDataCacheService` |
| `IDomainCacheOptionsProvider`, `DomainCacheOptions`, `DomainDataCacheSettings`, `DomainName` (**Core**) | `DomainCacheOptionsProvider`, `CacheOrchestratorOptionsValidator` |
| `IRequestDomainCacheOptions`, `DomainHttpCacheOptions`, HTTP domain setting types, `ICacheOrchestratorFeature` (**AspNetCore**) | `RequestDomainCacheOptionsProvider`, `CacheOrchestratorHttpOptions`, `CacheOrchestratorHttpOptionsValidator`, `CacheOrchestratorFeature` |
| `ICacheOrchestratorInvalidator`, `CacheInvalidationResult`, `ICacheInvalidationObserver`, `CacheTags` | `CacheOrchestratorInvalidator` |
| `IClusterCommandBus`, `IClusterMembership`, `IClusterCommandHandler`, `IInstanceIdProvider`, command records (`InvalidateCommand`, `VersionBumpCommand`, `SettingsPatchCommand`, …) | `DefaultClusterCommandHandler` |
| — | `NullClusterCommandBus`, `NullClusterMembership` |
| `IOutputCacheBackendRegistrar` | `InMemoryCacheBackendRegistrar` |
| Redis: `AddRedisBackend` (**CacheOrchestrator.Redis**) | `RedisCacheBackendRegistrar`, `RedisCacheHealthProbe`, all `Redis.Shared` implementation types |
| HttpBus: `AddHttpClusterBus` / `MapCacheOrchestratorHttpBus` (**CacheOrchestrator.HttpBus**) | `HttpClusterCommandBus`, versioned HTTP wire DTOs, `ClusterEndpointAuth` |
| `ICacheOrchestratorManagement`, management DTOs and host adapter contracts (**Core**) | `CacheOrchestratorManagement`, `CoreAdminDomainConfigProvider` |
| `MapCacheOrchestratorAdmin` (**AspNetCore HTTP adapter**) | `AdminLocalApi`, `HttpAdminDomainConfigProvider`, `InMemoryAdminStatsCollector` |
| `AuthBypassMode`, `ETagMode`, `ClientCacheability`, `DomainAuthEvaluator` (**AspNetCore**) | — |
| `ICacheVaryContributor`, `CacheVaryMaterializer`, `ICacheVaryBuilder` | — |
| `DomainOutputCachePolicy`, `[CacheDomain]`, `CacheOutputWithDomain` / `CacheOutputWithDomainTemplate` / `CacheOutputWithDomainAttribute` | `CacheDomainConvention` |
| Identity: `.WithCacheIdentity` / `.WithContentHashCacheIdentity`, `[CacheIdentity]` / `[ContentHashCacheIdentity]`, `ICacheIdentityContract`, `AddCacheIdentityContract<T>()`, `CacheIdentities.Url` | `CacheIdentityResolutionHostedService`, binding applicators |
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

**Not cached:** methods without an identity binding when identity metadata is present (and non-GET/HEAD when identity metadata is absent), identity material null / content-hash oversize, `Cache-Control: no-store`, authenticated / `Authorization`, disabled domain, non-cacheable status codes, `Set-Cookie` responses. Opt-in non-GET via endpoint cache identity — [cache-identity.md](../reference/cache-identity.md).

## Request flow — Data Cache

1. Code calls `IDomainDataCache.GetOrSetAsync` or Core `ICacheOrchestrator.GetOrCreateAsync`.  
2. If domain still missing or Data Cache disabled → factory runs uncached.
3. Optional respect for request `no-store` / auth bypass.  
4. Key from `IDomainKeyGenerator` (HTTP) or caller-supplied key (orchestrator).  
5. Domain config selects a named **`DataCacheInstances`** entry (`default` by default).  
6. Registered `IDataCacheProvider` (Fusion: L1 → L2 → factory with soft/hard timeouts, fail-safe, jitter; Hybrid: expiration from `DataCache.TtlSeconds`).  
7. Disposition (`Hit` / `Miss` / `Stale` / …) stored for `X-Cache` (`dc=`).  

See [Data Cache](../reference/data-cache.md) for HTTP resolution order and entity identity.

## Backends

| Provider | Package | Output Cache | Data Cache (Fusion path) |
|----------|---------|--------------|--------------------------|
| `InMemory` | Core / host | ASP.NET default store | L1 only |
| `Redis` | **`CacheOrchestrator.Redis`** | StackExchange Redis store | Keyed L2 + Redis backplane per instance |
| *(Custom)* | Your app | Custom | Keyed L2 recommended for multi-instance |

Register Redis with `AddRedisBackend()` (see [backends.md](../reference/backends.md)). Custom backends use `IOutputCacheBackendRegistrar` + `AddBackend`.

Output Cache and Data Cache providers can differ (for example, InMemory Output Cache with Fusion Redis).

**Multi-instance Redis:** each `DataCacheInstances` entry gets a keyed `IConnectionMultiplexer` and keyed `IDistributedCache` under the instance name. Do not share one global `IDistributedCache` across named caches — the last registration would win and mis-route L2 writes.

## Related

- [packages.md](../guide/packages.md)  
- [Guide — concepts](../guide/concepts.md)  
- [cluster-bus.md](../reference/cluster-bus.md)  
- [cache-keys.md](../reference/cache-keys.md)  
- [configuration.md](../reference/configuration.md)  
- [Output Cache](../reference/output-cache.md)
- [Data Cache](../reference/data-cache.md)
- [vary.md](../reference/vary.md)  
- [deployment.md](../reference/deployment.md)  
