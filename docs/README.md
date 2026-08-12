# CacheOrchestrator documentation

Domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

Technical reference for consumers and maintainers.  
**New here?** Start with the path below — you do not need to read every page.

## Start here

| Step | Doc / sample | Why |
|------|----------------|-----|
| 1 | [Minimal sample](../samples/CacheOrchestrator.Minimal) | Run → MISS then HIT in one minute |
| 2 | [getting-started.md](getting-started.md) | Install, mental model, first endpoint |
| 3 | [Playground sample](../samples/CacheOrchestrator.Sample) | TTL, schedule, Redis, CRUD UI |
| 4 | [faq.md](faq.md) | Common gotchas and limitations |

Root overview (also lists every advanced feature): [../README.md](../README.md)

## Core ideas (when you need them)

| Document | Description |
|----------|-------------|
| [domain-profiles.md](domain-profiles.md) | Snapshot (OSM) vs dynamic (CRUD); Version vs TTL |
| [client-cache-schedule.md](client-cache-schedule.md) | Client `max-age` ramp before cutover |
| [comparison.md](comparison.md) | vs manual Output Cache + Fusion; vs Redis OC only |

## Reference

| Document | Description |
|----------|-------------|
| [configuration.md](configuration.md) | Full `appsettings` schema and defaults |
| [output-cache.md](output-cache.md) | HTTP policies, auth flags, Minimal API & MVC |
| [fusion-cache.md](fusion-cache.md) | `IDomainFusionCache`, keys, fail-safe, resource id |
| [cache-keys.md](cache-keys.md) | FC/OC key identity, Namespace, why domain differs |
| [invalidation.md](invalidation.md) | Version, domain/entity tags, invalidator API, multi-instance strategies |
| [cluster-bus.md](cluster-bus.md) | Optional `CacheOrchestrator.Bus` — HTTP command bus, membership, Admin distribute |
| [backends.md](backends.md) | InMemory, Redis package, custom registrars |
| [observability.md](observability.md) | `X-Cache`, `EmitDiagnosticsHeaders`, metrics, health |
| [admin.md](admin.md) | Local Admin API + Admin App fan-out SPA |
| [admin-hints.md](admin-hints.md) | Recommendation hints: formulas, catalogue, how to add |
| [deployment.md](deployment.md) | Multi-instance, Redis, backplane, optional Bus |
| [architecture.md](architecture.md) | Layers, request flow, public API surface |
| [benchmarks/results.md](benchmarks/results.md) | How to run BDN + hot-path notes |

## Repo guides (root)

| Document | Description |
|----------|-------------|
| [../CHANGELOG.md](../CHANGELOG.md) | Release history |
| [releasing.md](releasing.md) | MinVer tags, NuGet publish, optional signing |
| [../CONTRIBUTING.md](../CONTRIBUTING.md) | Build, test, coding conventions, PRs |
| [../SECURITY.md](../SECURITY.md) | How to report vulnerabilities |
| [../LICENSE.md](../LICENSE.md) | MIT License |

## Library entry points

| API | Namespace |
|-----|-----------|
| `AddCacheOrchestrator` / `UseCacheOrchestrator` | `CacheOrchestrator.DependencyInjection` |
| `CacheOutputWithDomain*` | `CacheOrchestrator.OutputCache` |
| `[CacheDomain]` | `CacheOrchestrator.OutputCache` |
| `IDomainFusionCache` | `CacheOrchestrator.FusionCache` |
| `IDomainCacheOptionsProvider` / `DomainName` | `CacheOrchestrator.Configuration` |
| `ICacheOrchestratorInvalidator` / `ICacheInvalidationObserver` | `CacheOrchestrator.Invalidation` |
| `IClusterCommandBus` / `IClusterMembership` / `IInstanceIdProvider` | `CacheOrchestrator.Cluster` |
| `AddHttpClusterBus` / `MapCacheOrchestratorHttpBus` | `CacheOrchestrator.Bus` |
| `AddCacheOrchestrator` (health) | `CacheOrchestrator.Diagnostics` |

Concrete service classes are **internal** (see [architecture.md — Public API surface](architecture.md#public-api-surface-10-stability)).

## API reference

XML documentation is included in the NuGet packages. Browse
[CacheOrchestrator on nuget.org](https://www.nuget.org/packages/CacheOrchestrator/)
for package docs. A dedicated DocFX / GitHub Pages API site is planned after 1.0.
