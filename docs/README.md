# CacheOrchestrator documentation

CacheOrchestrator is a configuration and coordination layer over ASP.NET Output Cache, FusionCache, and client Cache-Control. It is not a cache of its own. The [root README](../README.md) is the product overview.

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
2. [Concepts](guide/concepts.md) — domain, three layers, Version vs TTL vs tags.
3. [Topologies](guide/topologies.md) — InMemory, Redis, Bus, mixed; which package.
4. [Operations](guide/operations.md) — `X-Cache`, meter, Admin API vs Console vs Docker.
5. [Domain profiles](domain-profiles.md) — snapshot datasets versus changing records.
6. [Client Cache Schedule](client-cache-schedule.md) — client `max-age` before a planned cutover.
7. [Comparison](comparison.md) — this library versus hand-rolled Output Cache and FusionCache.
8. [FAQ](faq.md) — common mistakes.

### Learn by running

- [Minimal sample](../samples/CacheOrchestrator.Minimal) — a miss, then a hit.
- [Playground sample](../samples/CacheOrchestrator.Sample) — TTLs, schedule, Redis, CRUD.
- [Playground topology labs](../samples/CacheOrchestrator.Sample/labs/README.md) — Docker Compose stages (Prometheus, Redis, multi-instance, bus); **not** part of the NuGet packages.

## Reference

### HTTP and data

- [Configuration](configuration.md) — `appsettings` schema and defaults.
- [Output Cache](output-cache.md) — HTTP policies, authentication flags, Minimal APIs and MVC.
- [FusionCache](fusion-cache.md) — `IDomainFusionCache`, keys, fail-safe, entity identity.
- [Cache keys](cache-keys.md) — how Output Cache and Fusion keys are built.
- [Vary](vary.md) — Accept / language / headers / cookies / query allowlists, auth modes, contributors.

### Invalidation

- [Invalidation](invalidation.md) — Version, tags, `ICacheOrchestratorInvalidator`.
- [EF Core](ef-core-invalidation.md) — purge after `SaveChanges`.
- [Cluster bus](cluster-bus.md) — commands across instances.

### Operations

- [Backends](backends.md) — InMemory, Redis, custom registrars.
- [Deployment](deployment.md) — several instances, Redis, backplane, bus.
- [Observability](observability.md) — `X-Cache`, metrics, health.
- [Admin](admin.md) — Admin API and Admin Console App.
- [Admin hints](admin-hints.md) — recommendation feature overview; **how to write rules:** [Admin hints/README](../src/CacheOrchestrator.AdminConsole/hints/README.md).
- [Admin Console Docker](../deploy/admin/README.md) — GHCR image, config mount, custom hints volume, logs.

### Internals

- [Architecture](architecture.md) — layers, request flow, public surface.
- [Benchmarks](benchmarks/results.md) — how to run them.

## Packages and apps

XML documentation ships with the NuGet packages: [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/), [Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/), [Bus](https://www.nuget.org/packages/CacheOrchestrator.Bus/), [EF Core](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/).

Admin Console App (not a NuGet package): [source README](../src/CacheOrchestrator.AdminConsole/README.md) · [Docker](../deploy/admin/README.md).

## Repository

- [CHANGELOG](../CHANGELOG.md)
- [Releasing](releasing.md)
- [Contributing](../CONTRIBUTING.md) — build, tests, samples, labs, worklog
- [Worklog template](templates/worklog-template.md) — copy when opening a branch
- [Security](../SECURITY.md)
- [License](../LICENSE.md)
