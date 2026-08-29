# Packages

> **Guide path:** [Domain profiles](domain-profiles.md) → **Packages** → [Topologies](topologies.md) · [Guide index](README.md)

The getting-started tutorial installed the `CacheOrchestrator` meta package because it is the shortest path for a typical ASP.NET Core application. Production applications can compose the same model from smaller packages.

Choose packages by capability first. Choose InMemory, Redis, and multi-instance coordination on the [Topologies](topologies.md) page next.

## Table of Contents

- [Start with the host](#start-with-the-host)
- [Add the Data Cache engine](#add-the-data-cache-engine)
- [Add Redis only where it is needed](#add-redis-only-where-it-is-needed)
- [Add integrations for specific jobs](#add-integrations-for-specific-jobs)
- [Common compositions](#common-compositions)
- [Know which package owns each setting](#know-which-package-owns-each-setting)
- [Keep dependencies pointing inward](#keep-dependencies-pointing-inward)

## Start with the host

| Application shape | Starting package | What it provides |
|-------------------|------------------|------------------|
| Typical ASP.NET Core app using FusionCache | `CacheOrchestrator` | Meta package: ASP.NET Core host + FusionCache data provider |
| ASP.NET Core app assembling its own data engine | `CacheOrchestrator.AspNetCore` | Output Cache, Client Cache, `IDomainDataCache`, diagnostics, and Admin API |
| Reusable class library | `CacheOrchestrator.Core` | HTTP-free domain models, orchestration, invalidation, and management contracts |
| Standalone worker host | `CacheOrchestrator.Core` + one provider package | HTTP-free orchestrator plus FusionCache or HybridCache |

For most web applications, start here:

```bash
dotnet add package CacheOrchestrator --prerelease
```

It is equivalent to choosing the ASP.NET Core integration and FusionCache provider together. You can still add Redis, EF invalidation, or the HTTP cluster bus later without changing endpoint domain names.

## Add the Data Cache engine

`CacheOrchestrator.Core` defines the Data Cache abstraction. One provider package supplies the engine:

| Engine | Package | Choose it when |
|--------|---------|----------------|
| **FusionCache** | `CacheOrchestrator.FusionCache` | You want named instances, fail-safe, factory timeouts, Redis L2, or a backplane |
| **HybridCache** | `CacheOrchestrator.HybridCache` | Your application is standardized on Microsoft HybridCache and needs its supported subset |

Do not register both as competing default providers. The `CacheOrchestrator` meta package already includes FusionCache; use `CacheOrchestrator.AspNetCore` + `CacheOrchestrator.HybridCache` when HybridCache should replace it.

### FusionCache and HybridCache are not identical

| Capability | FusionCache provider | HybridCache provider |
|------------|----------------------|----------------------|
| Get-or-create and stampede protection | Yes | Yes |
| Tag invalidation | Yes | Yes, using HybridCache semantics |
| Portable `DataCache.TtlSeconds` | Yes | Yes |
| Fusion hard TTL, fail-safe, jitter, and factory timeouts | Yes | No |
| Named `DataCacheInstances` | Yes | One DI HybridCache instance |
| Redis L2/backplane package supplied by this project | Yes | Configure HybridCache and its distributed storage separately |

Keep portable policy under `DataCache`. Put Fusion-only tuning under the nested `FusionCache` section. A HybridCache provider ignores Fusion-only settings because those features do not belong to its engine.

## Add Redis only where it is needed

Output Cache and Data Cache are separate stores. Redis packages reflect that boundary:

| Goal | Package and registration |
|------|--------------------------|
| Redis Output Cache **and** FusionCache Redis L2/backplane | `CacheOrchestrator.Redis` + `AddRedisBackend()` |
| Redis Output Cache only | `CacheOrchestrator.AspNetCore.Redis` + `AddRedisOutputCacheBackend()` |
| FusionCache Redis L2/backplane only | `CacheOrchestrator.FusionCache.Redis` + `AddRedisFusionCacheBackend()` |

The first option is a convenience meta package. The two focused packages let a topology keep Output Cache in memory while sharing data objects, or share HTTP responses without installing the Fusion Redis integration.

`CacheOrchestrator.Redis.Shared` contains shared connection and configuration support. It is pulled in transitively and is not a product package to install by itself.

Setting `"Provider": "Redis"` in configuration is not enough. The matching Redis registrar must also be added during service registration; otherwise startup validation fails.

## Add integrations for specific jobs

| Job | Package | Main entry points |
|-----|---------|-------------------|
| Send invalidation and runtime commands to peer instances | `CacheOrchestrator.HttpBus` | `AddHttpClusterBus`, `MapCacheOrchestratorHttpBus` |
| Invalidate entities after successful EF Core `SaveChanges` | `CacheOrchestrator.EFCore.Invalidation` | `AddCacheOrchestratorEfCoreInvalidation`, `AddCacheOrchestratorInvalidation` |
| Show a fan-out dashboard across applications | CacheOrchestrator Admin Console App | Separate `net10.0` application or container; not a NuGet package |

The HTTP bus does not store or share cache values. It distributes commands. Redis L2, a Redis Output Cache store, and the HTTP bus solve different problems; [Topologies](topologies.md) shows when each belongs in the same deployment.

## Common compositions

| Need | Install | Registration shape |
|------|---------|--------------------|
| One web process, all in memory | `CacheOrchestrator` | `AddCacheOrchestrator(configuration)` |
| Web app with Output Cache only | `CacheOrchestrator.AspNetCore` | `AddCacheOrchestratorAspNetCore(configuration)` |
| Web app with FusionCache but no Output Cache endpoints | `CacheOrchestrator.AspNetCore` + `CacheOrchestrator.FusionCache` | Register both focused packages |
| Web app with HybridCache | `CacheOrchestrator.AspNetCore` + `CacheOrchestrator.HybridCache` | Register ASP.NET Core + HybridCache |
| Web app with Redis Output Cache and Fusion L2 | `CacheOrchestrator` + `CacheOrchestrator.Redis` | `AddCacheOrchestrator(configuration, options => options.AddRedisBackend())` |
| Core library hosted by a web app | `CacheOrchestrator.Core` in the library; host packages in the app | Library uses `ICacheOrchestrator`; host owns providers |
| Core library hosted by a worker | `CacheOrchestrator.Core` in the library and host; FusionCache or HybridCache in the host | Worker calls `AddCacheOrchestratorCore` and one provider registration |
| EF Core writes with automatic invalidation | Host composition + `CacheOrchestrator.EFCore.Invalidation` | Register the EF integration and interceptor |

Exact commands, configuration, and complete code for each combination are in [Package composition](../how-to/composition.md).

## Know which package owns each setting

| Configuration section | Owner | Meaning |
|-----------------------|-------|---------|
| `Cache:Domains:*:DataCache` | Core + ASP.NET Core | Core: enabled, instance, TTL. ASP.NET Core: `RespectNoStore`, `VaryOnEncoding`, `VaryOnPublicAddress` under the same JSON object |
| `Cache:Domains:*:OutputCache` | ASP.NET Core | Server HTTP response policy |
| `Cache:Domains:*:ClientCache` | ASP.NET Core | Client Cache headers and optional Client Cache Schedule |
| `Cache:Domains:*:FusionCache` | FusionCache package | Engine-specific hard TTL, fail-safe, jitter, and factory timeouts |
| `Cache:DataCacheInstances` | Provider packages | Named data engines and their providers |
| `Cache:OutputCache` | ASP.NET Core backend registrars | Root Output Cache provider |

All configuration durations use integer `*Seconds` fields. The complete schema and defaults are in [Configuration](../reference/configuration.md).

## Keep dependencies pointing inward

`CacheOrchestrator.Core` has no ASP.NET Core, FusionCache, HybridCache, Redis, EF Core, or HttpBus dependency. Put Core in reusable libraries and let the host choose infrastructure.

`AddCacheOrchestratorCore(configuration)` registers the HTTP-free default `ICacheOrchestrator`, domain options, invalidation, and cluster contracts. A worker then calls `AddCacheOrchestratorFusionCache(configuration)` or `AddCacheOrchestratorHybridCache(configuration)`. It does not install the ASP.NET Core package or add middleware.

Use these public abstractions at application boundaries:

| API | Use it from |
|-----|-------------|
| `ICacheOrchestrator` + `CacheDomainContext` | Class libraries and workers |
| `ICacheOrchestratorManagement` | Secured worker commands, operational services, and custom management transports |
| `IDomainDataCache` | ASP.NET Core request handlers |
| `ICacheOrchestratorInvalidator` | Write paths and application services |

Default implementations are internal and registered through DI. Application code should not construct or depend on them directly.

Next: choose where each store lives and how instances coordinate in [Topologies](topologies.md).
