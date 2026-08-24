# CacheOrchestrator documentation

CacheOrchestrator is a configuration and coordination layer over ASP.NET Output Cache, a **data cache** engine (FusionCache or HybridCache), and client Cache-Control. It is not a cache of its own. The [root README](../README.md) is the product overview. How packages compose: [packages.md](packages.md).

Human docs in three layers. You do not need every page below.

## Three layers

| Layer | Start here | For |
|-------|------------|-----|
| **Product** | [Root README](../README.md) | What the library is, quick start, packages |
| **Guide** | [Guide](guide/README.md) | How to think, which topology, how to operate |
| **Reference** | Topic pages in this index | Schema, APIs, keys, deployment wiring |

```
README.md  →  docs/guide/  →  docs/<topic>.md
```

## Guide

Orientation after the product README. Summaries plus links into reference — not the `appsettings` schema.

1. [Getting started](getting-started.md) — install, first endpoint, `X-Cache`.
2. [Packages and composition](packages.md) — NuGet map, endpoint/library/EF scenarios (registration + config + code).
3. [Concepts](guide/concepts.md) — domain, three layers, Version vs TTL vs tags.
4. [Topologies](guide/topologies.md) — InMemory, Redis, HttpBus, mixed; which package.
5. [Operations](guide/operations.md) — `X-Cache`, meter, Admin API vs Console vs Docker.
6. [Domain profiles](domain-profiles.md) — snapshot datasets versus changing records.
7. [Client Cache Schedule](client-cache-schedule.md) — client `max-age` before a planned cutover.
8. [Comparison](comparison.md) — this library versus hand-rolled Output Cache and FusionCache.
9. [FAQ](faq.md) — common mistakes.

### Learn by running

- [Minimal sample](../samples/CacheOrchestrator.Minimal) — a miss, then a hit.
- [Playground sample](../samples/CacheOrchestrator.Sample) — TTLs, schedule, Redis, CRUD.
- [Playground topology labs](../samples/CacheOrchestrator.Sample/labs/README.md) — Docker Compose stages (Prometheus, Redis, multi-instance, bus); **not** part of the NuGet packages.

## Reference

### HTTP and data

- [Configuration](configuration.md) — `appsettings` schema and defaults.
- [Output Cache](output-cache.md) — HTTP policies, authentication flags, Minimal APIs and MVC.
- [FusionCache](fusion-cache.md) — Fusion as `IDataCacheProvider`, fail-safe, entity identity overview.
- [Entity footprint](entity-footprint.md) — use-case cookbook (list, references, aggregate, nested, batch, aliases, …).
- [Cache keys](cache-keys.md) — how Output Cache and data-cache keys are built.
- [Vary](vary.md) — Accept / language / headers / cookies / query allowlists, auth modes, contributors.

### Invalidation

- [Invalidation](invalidation.md) — Version, tags, `ICacheOrchestratorInvalidator`.
- [EF Core](ef-core-invalidation.md) — purge after `SaveChanges`.
- [Cluster bus](cluster-bus.md) — commands across instances (`CacheOrchestrator.HttpBus`).

### Operations

- [Backends](backends.md) — InMemory, Redis, custom registrars.
- [Deployment](deployment.md) — several instances, Redis, backplane, bus.
- [Observability](observability.md) — `X-Cache`, metrics, health.
- [Admin](admin.md) — Admin API and Admin Console App.
- [Admin hints](admin-hints.md) — recommendation feature overview; **how to write rules:** [Admin hints/README](../src/CacheOrchestrator.AdminConsole/hints/README.md).
- [Admin Console Docker](../deploy/admin/README.md) — GHCR image, config mount, custom hints volume, logs.

### Internals

- [Architecture](architecture.md) — packages, request flow, public surface.
- [Benchmarks](benchmarks/results.md) — how to run them.

## Packages and apps

XML documentation ships with the NuGet packages. Composition and use cases: [packages.md](packages.md).

| Package | |
|---------|--|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta (AspNetCore + FusionCache) |
| [Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Domains, `ICacheOrchestrator` |
| [AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | OC, Client Cache, Admin |
| [FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | Fusion data provider |
| [HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Hybrid data provider |
| [Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis backends |
| [HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Cluster HTTP bus |
| [EF Core Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | SaveChanges → purge |

Admin Console App (not a NuGet package): [source README](../src/CacheOrchestrator.AdminConsole/README.md) · [Docker](../deploy/admin/README.md).

## Repository

- [CHANGELOG](../CHANGELOG.md)
- [Releasing](releasing.md)
- [Contributing](../CONTRIBUTING.md) — build, tests, samples, labs, worklog
- [Worklog template](templates/worklog-template.md) — copy when opening a branch
