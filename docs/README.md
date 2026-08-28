# CacheOrchestrator documentation

CacheOrchestrator configures and coordinates ASP.NET Output Cache, a **Data Cache** engine (FusionCache or HybridCache), and Client Cache under one **domain** model. It is not a cache of its own.

Start with the [root README](../README.md) for the product overview and quick start. Then pick a path below.

## Layers

| Layer | Start here | For |
|-------|------------|-----|
| **Product** | [Root README](../README.md) | What it is, quick start, package table |
| **Guide** | [Guide](guide/README.md) | How to think, which topology, how to operate |
| **How-to** | [Composition](how-to/composition.md) | Copy-paste install / register / config / code |
| **Reference** | Topic pages below | Schema, APIs, keys, deployment wiring |

```
README.md  →  docs/guide/  →  docs/how-to/  →  docs/reference/
```

## Guide

1. [Getting started](guide/getting-started.md) — first endpoint and `X-Cache`
2. [Concepts](guide/concepts.md) — domain, three layers, Version vs TTL vs tags
3. [Domain profiles](guide/domain-profiles.md) — snapshot datasets vs changing records
4. [Packages](guide/packages.md) — which NuGet to install
5. [Topologies](guide/topologies.md) — InMemory, Redis, HttpBus, mixed
6. [Client Cache Schedule](guide/client-cache-schedule.md) — client `max-age` before a cutover
7. [Operations](guide/operations.md) — diagnostics map, Admin API vs Console
8. [Comparison](guide/comparison.md) — hand-rolled stack vs this library
9. [FAQ](guide/faq.md) — common mistakes

### Learn by running

- [Minimal sample](../samples/CacheOrchestrator.Minimal) — a miss, then a hit
- [Playground sample](../samples/CacheOrchestrator.Sample) — TTLs, schedule, Redis, CRUD
- [Topology labs](../samples/CacheOrchestrator.Sample/labs/README.md) — Docker Compose stages (not part of NuGet)

## How-to

- [Package composition](how-to/composition.md) — scenarios 1–9 (typical web, Hybrid, Redis, library, EF, …)

## Reference

The reference describes exact configuration paths, API contracts, key material, and runtime behavior. Read one page for a specific contract, or follow one of the paths below when a concern crosses several parts of the system.

### Configuration and request identity

1. [Configuration](reference/configuration.md) — complete `appsettings` schema, defaults, inheritance, and validation
2. [Endpoint cache identity](reference/cache-identity.md) — method bindings, named contracts, URL identity, and content hashes
3. [Cache keys](reference/cache-keys.md) — namespaces and key material for Output Cache and Data Cache
4. [Domain vary dimensions](reference/vary.md) — query, headers, language, cookies, authentication, and custom contributors

### Cache layers and freshness

- [Core API](reference/core-api.md) — HTTP-free `ICacheOrchestrator`, domain contexts, keys, and entity operations
- [Output Cache](reference/output-cache.md) — policy selection, endpoint metadata, status rules, headers, and authentication
- [Data Cache](reference/data-cache.md) — providers, domain resolution, key generation, results, and entity reads
- [Client Cache Schedule algorithm](reference/client-cache-schedule-algorithm.md) — exact phase and `max-age` calculation
- [Entity footprint](reference/entity-footprint.md) — primary entities, members, dependencies, collections, batches, and aliases
- [Invalidation](reference/invalidation.md) — Version cutovers, tags, results, observers, and multi-instance behavior
- [EF Core invalidation](reference/ef-core-invalidation.md) — entity mapping and purge after `SaveChanges`

### Infrastructure and operations

- [Backends](reference/backends.md) — built-in stores and the three custom storage boundaries
- [Extensibility](reference/extensibility.md) — application, provider, cluster, and host extension points
- [Deployment](reference/deployment.md) — single-instance, Redis, InMemory clusters, and shared configuration
- [Cluster command bus](reference/cluster-bus.md) — membership, commands, delivery, authentication, and partial failure
- [Observability](reference/observability.md) — `X-Cache`, metrics, traces, logs, and health checks
- [Admin](reference/admin.md) — local Admin API, Admin Console App, trust boundaries, and operational endpoints
- [Admin Console Docker](../deploy/admin/README.md) — image, volumes, and logs

### Common technical paths

| Goal | Read in this order |
|------|--------------------|
| Explain why two requests share or do not share an entry | [Endpoint identity](reference/cache-identity.md) → [Vary](reference/vary.md) → [Cache keys](reference/cache-keys.md) |
| Invalidate one changed record and every view that contains it | [Entity footprint](reference/entity-footprint.md) → [Invalidation](reference/invalidation.md) → [EF Core invalidation](reference/ef-core-invalidation.md) |
| Design a multi-instance deployment | [Backends](reference/backends.md) → [Deployment](reference/deployment.md) → [Cluster bus](reference/cluster-bus.md) |
| Diagnose hit rate or factory load | [Observability](reference/observability.md) → [Admin](reference/admin.md) |
| Integrate custom identity, storage, health, or command transport | [Extensibility](reference/extensibility.md) → the linked contract page |

## Packages

XML docs ship with the NuGets. Choices: [packages](guide/packages.md). Scenarios: [composition](how-to/composition.md).

| Package | |
|---------|--|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator) | Meta (AspNetCore + FusionCache) |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core) | Domains, `ICacheOrchestrator` |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore) | Output Cache, Client Cache, Admin |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache) | Fusion Data Cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache) | Hybrid Data Cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis) | Meta Redis (Output Cache + Fusion L2) |
| [CacheOrchestrator.AspNetCore.Redis](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore.Redis) | Redis Output Cache only |
| [CacheOrchestrator.FusionCache.Redis](https://www.nuget.org/packages/CacheOrchestrator.FusionCache.Redis) | Redis Fusion L2 only |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus) | Cluster HTTP bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation) | `SaveChanges` → purge |

Admin Console App (not a NuGet package): [source](../src/CacheOrchestrator.AdminConsole/README.md) · [Docker](../deploy/admin/README.md).

## Contributor

Maintainer material — not required to ship a first domain.

- [Architecture](contributor/architecture.md)
- [Admin hints](contributor/admin-hints.md) · [writing hint rules](../src/CacheOrchestrator.AdminConsole/hints/README.md)
- [Benchmarks](contributor/benchmarks/results.md)
- [Releasing](contributor/releasing.md)
- [Contributing](../CONTRIBUTING.md)
- [Worklog template](contributor/templates/worklog-template.md)
- [CHANGELOG](../CHANGELOG.md)
