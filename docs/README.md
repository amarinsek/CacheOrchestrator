# CacheOrchestrator documentation

CacheOrchestrator configures and coordinates ASP.NET Output Cache, a **data cache** engine (FusionCache or HybridCache), and client Cache-Control under one **domain** model. It is not a cache of its own.

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
3. [Packages](guide/packages.md) — which NuGet to install
4. [Topologies](guide/topologies.md) — InMemory, Redis, HttpBus, mixed
5. [Domain profiles](guide/domain-profiles.md) — snapshot datasets vs changing records
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

### HTTP and data

- [Configuration](reference/configuration.md) — `appsettings` schema and defaults
- [Output Cache](reference/output-cache.md) — HTTP policies, auth flags, Minimal APIs and MVC
- [Data cache](reference/data-cache.md) — Fusion / Hybrid, domain resolution, entity identity
- [Entity footprint](reference/entity-footprint.md) — list, references, aggregate, nested, batch, aliases
- [Cache keys](reference/cache-keys.md) — how OC and data-cache keys are built
- [Vary](reference/vary.md) — Accept, language, headers, cookies, query allowlists, auth modes
- [Client Cache Schedule algorithm](reference/client-cache-schedule-algorithm.md) — exact `max-age` ramp

### Invalidation

- [Invalidation](reference/invalidation.md) — Version, tags, `ICacheOrchestratorInvalidator`
- [EF Core](reference/ef-core-invalidation.md) — purge after `SaveChanges`
- [Cluster bus](reference/cluster-bus.md) — commands across instances

### Operations

- [Backends](reference/backends.md) — InMemory, Redis, custom registrars
- [Deployment](reference/deployment.md) — several instances, Redis, backplane, bus
- [Observability](reference/observability.md) — `X-Cache`, metrics, health
- [Admin](reference/admin.md) — Admin API and Admin Console App
- [Admin Console Docker](../deploy/admin/README.md) — GHCR image, volumes, logs

## Packages

XML docs ship with the NuGets. Choices: [packages](guide/packages.md). Scenarios: [composition](how-to/composition.md).

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
