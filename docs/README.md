# CacheOrchestrator documentation

Technical reference. The [root README](../README.md) is the product overview. You do not need every page below.

## Start here

1. [Minimal sample](../samples/CacheOrchestrator.Minimal) — a miss, then a hit.
2. [Getting started](getting-started.md) — install, first endpoint, `X-Cache`.
3. [Playground sample](../samples/CacheOrchestrator.Sample) — TTLs, schedule, Redis, CRUD.
4. [FAQ](faq.md) — common mistakes.

## Core ideas

- [Domain profiles](domain-profiles.md) — snapshot datasets versus changing records; Version versus TTL.
- [Client Cache Schedule](client-cache-schedule.md) — client `max-age` before a planned cutover.
- [Comparison](comparison.md) — this library versus hand-rolled Output Cache and FusionCache.

## HTTP and data

- [Configuration](configuration.md) — `appsettings` schema and defaults.
- [Output Cache](output-cache.md) — HTTP policies, authentication flags, Minimal APIs and MVC.
- [FusionCache](fusion-cache.md) — `IDomainFusionCache`, keys, fail-safe, entity identity.
- [Cache keys](cache-keys.md) — how Output Cache and Fusion keys are built.

## Invalidation

- [Invalidation](invalidation.md) — Version, tags, `ICacheOrchestratorInvalidator`.
- [EF Core](ef-core-invalidation.md) — purge after `SaveChanges`.
- [Cluster bus](cluster-bus.md) — commands across instances.

## Operations

- [Backends](backends.md) — InMemory, Redis, custom registrars.
- [Deployment](deployment.md) — several instances, Redis, backplane, bus.
- [Observability](observability.md) — `X-Cache`, metrics, health.
- [Admin](admin.md) — Admin API and Admin App.
- [Admin hints](admin-hints.md) — recommendation feature overview; **how to write rules:** [Admin hints/README](../src/CacheOrchestrator.Admin/hints/README.md).
- [Local Prometheus (Playground sample)](../samples/CacheOrchestrator.Sample/deploy/prometheus/README.md) — optional Docker scrape for Admin Metrics; **not** part of the NuGet packages.

## Internals

- [Architecture](architecture.md) — layers, request flow, public surface.
- [Benchmarks](benchmarks/results.md) — how to run them.

## Repository

- [CHANGELOG](../CHANGELOG.md)
- [Releasing](releasing.md)
- [Contributing](../CONTRIBUTING.md)
- [Security](../SECURITY.md)
- [License](../LICENSE.md)

XML documentation ships with the NuGet packages: [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/), [Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/), [Bus](https://www.nuget.org/packages/CacheOrchestrator.Bus/), [EF Core](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/).
