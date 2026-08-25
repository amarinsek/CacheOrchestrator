# Packages

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md). Copy-paste scenarios: [composition how-to](../how-to/composition.md).

CacheOrchestrator splits **policy** from **engines**. Domain rules and call sites stay stable; NuGet references and DI choose the topology.

- **`CacheOrchestrator.Core`** holds domains, Version, portable `DataCache` policy, entity footprint, `ICacheOrchestrator`, and invalidation/cluster **contracts**.
- Optional packages add ASP.NET Output/Client Cache, Fusion or Hybrid as the data engine, Redis, the HTTP cluster bus, and EF SaveChanges hooks.

Dependency rule: arrows point at **`CacheOrchestrator.Core`**. Core never references ASP.NET, FusionCache, HybridCache, Redis, HttpBus, or EF.

---

## Which package

| Goal | Prefer |
|------|--------|
| Domains, Version, portable `DataCache`, invalidation / cluster contracts | **`CacheOrchestrator.Core`** |
| Fusion as data-cache engine | **`CacheOrchestrator.FusionCache`** |
| HybridCache as data-cache engine | **`CacheOrchestrator.HybridCache`** (replaces the Fusion provider) |
| Output Cache + Client Cache-Control + `IDomainDataCache` + Admin API | **`CacheOrchestrator.AspNetCore`** |
| Typical web app (AspNet + Fusion) | **`CacheOrchestrator` (Meta)** |
| Redis Output Cache **and** Fusion L2 / backplane | **`CacheOrchestrator.Redis` (Meta)** (`AddRedisBackend`) |
| Redis Output Cache only | **`CacheOrchestrator.AspNetCore.Redis`** (`AddRedisOutputCacheBackend`; from **3.0.0-beta.3**) |
| Redis Fusion L2 / backplane only (no ASP.NET) | **`CacheOrchestrator.FusionCache.Redis`** (`AddRedisFusionCacheBackend`; from **3.0.0-beta.3**) |
| Invalidate / Version / TTL on every InMemory node | **`CacheOrchestrator.HttpBus`** |
| Purge after EF `SaveChanges` | **`CacheOrchestrator.EFCore.Invalidation`** |
| Dashboard across instances | Admin API on each app + **Admin Console App** (not a NuGet package) |

| API | Package | Role |
|-----|---------|------|
| `ICacheOrchestrator` | `CacheOrchestrator.Core` | Http-free data get-or-create |
| `CacheDomainContext` | `CacheOrchestrator.Core` | Host-supplied domain (+ optional entity kind) for libraries |
| `IDomainDataCache` | `CacheOrchestrator.AspNetCore` | HTTP projection over `ICacheOrchestrator` |

Layouts (InMemory vs Redis vs bus): [topologies](topologies.md). Production wiring: [deployment](../reference/deployment.md).

---

## Use-case matrix

| # | Host | Data | Output Cache | Typical packages | How-to |
|---|------|------|--------------|------------------|--------|
| **1** | Web | Fusion (InMemory) | yes | `CacheOrchestrator` (Meta) *(or AspNetCore + FusionCache)* | [§1](../how-to/composition.md#scenario-1) |
| **2** | Web | — | yes | `CacheOrchestrator.AspNetCore` only | [§2](../how-to/composition.md#scenario-2) |
| **3** | Web | Fusion | no | `CacheOrchestrator.AspNetCore` + `CacheOrchestrator.FusionCache` | [§3](../how-to/composition.md#scenario-3) |
| **4** | Web | Fusion (Redis L2) | yes | `CacheOrchestrator` (Meta) + `CacheOrchestrator.Redis` (Meta) | [§4](../how-to/composition.md#scenario-4) |
| **5** | Web | Hybrid | yes | `CacheOrchestrator.AspNetCore` + `CacheOrchestrator.HybridCache` | [§5](../how-to/composition.md#scenario-5) |
| **6** | Web | Fusion | yes (dynamic domain) | `CacheOrchestrator` (Meta) | [§6](../how-to/composition.md#scenario-6) |
| **7** | Library + web / worker | Fusion | yes / n/a | `CacheOrchestrator.Core` in library; Meta in host | [§7](../how-to/composition.md#scenario-7) |
| **8** | Web + EF invalidation | Fusion | yes | Meta + `CacheOrchestrator.EFCore.Invalidation` | [§8](../how-to/composition.md#scenario-8) |
| **9** | Library (EF) + web host | Fusion | yes | Core (+ EF) in library; Meta + EF in host | [§9](../how-to/composition.md#scenario-9) |

Each scenario keeps the **same endpoint shape** where possible. Differences are packages, registration, and config.

---

## Nested config (portable vs engine)

| JSON section | Portable? | Meaning |
|--------------|-----------|---------|
| `DataCache` | Yes | Enable, instance, TTL, vary / no-store |
| `OutputCache` | AspNet | HTTP response cache |
| `ClientCache` | AspNet | Browser / CDN `Cache-Control` (+ schedule) |
| `FusionCache` | Fusion only | Hard TTL, fail-safe, factory timeouts, … |

Root engines: `OutputCache` + **`DataCacheInstances`**. Full schema: [configuration](../reference/configuration.md).

---

## Fusion vs Hybrid

| Feature | Fusion | Hybrid |
|---------|--------|--------|
| GetOrCreate + stampede | Yes | Yes |
| Tag invalidation | Yes | Yes (logical) |
| `DataCache.TtlSeconds` | Soft / duration | Expiration |
| Hard TTL / fail-safe / factory timeouts | Yes | Ignored |
| Named `DataCacheInstances` | Yes | Single DI HybridCache |
| Redis L2 + backplane | `CacheOrchestrator.FusionCache.Redis` (or `CacheOrchestrator.Redis` Meta) | Configure Hybrid / `IDistributedCache` separately |

`CacheOrchestrator.Redis.Shared` is a **support** package (connection options / config resolve). It is pulled in transitively — **do not** install it alone; it is not listed as a product install target.

Details: [data cache](../reference/data-cache.md). Hybrid wiring: [composition §5](../how-to/composition.md#scenario-5).

---

## Related

- [Composition how-to](../how-to/composition.md)
- [Getting started](getting-started.md)
- [Topologies](topologies.md)
- [EF Core invalidation](../reference/ef-core-invalidation.md)
- [Admin](../reference/admin.md)
