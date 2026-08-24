# CacheOrchestrator guide

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

Between the product README and the technical reference: how to think about domains, which package and topology to use, and how to operate the three layers — without the full `appsettings` schema.

## Who you are

| You | Start here | Then |
|-----|------------|------|
| First endpoint | [Getting started](getting-started.md) | [Concepts](concepts.md) |
| InMemory vs Redis vs HttpBus | [Topologies](topologies.md) | [Packages](packages.md), [deployment](../reference/deployment.md), or [labs](../../samples/CacheOrchestrator.Sample/labs/README.md) |
| Which NuGet / how to wire | [Packages](packages.md) | [Composition](../how-to/composition.md) |
| Snapshot tiles vs CRUD | [Domain profiles](domain-profiles.md) | [Invalidation](../reference/invalidation.md) |
| Operator | [Operations](operations.md) | [Admin](../reference/admin.md) |
| Something looks wrong | [FAQ](faq.md) | The topic page the FAQ links |

## Ten-minute path

1. [Root README](../../README.md) — is this the right library?
2. [Minimal sample](../../samples/CacheOrchestrator.Minimal) or [Getting started](getting-started.md) — a miss, then a hit
3. [Concepts](concepts.md) — domain, three layers, Version vs TTL vs tags
4. [Packages](packages.md) and [Topologies](topologies.md)
5. [Domain profiles](domain-profiles.md) — snapshot vs CRUD
6. Wiring or schema when you need it: [composition](../how-to/composition.md), [configuration](../reference/configuration.md), [data cache](../reference/data-cache.md)

## Guide pages

| Page | What it is for |
|------|----------------|
| [Getting started](getting-started.md) | Install, first endpoint, `X-Cache` |
| [Concepts](concepts.md) | Mental model |
| [Packages](packages.md) | Which NuGet to install |
| [Topologies](topologies.md) | Layouts (InMemory, Redis, HttpBus) |
| [Domain profiles](domain-profiles.md) | Snapshot vs dynamic |
| [Client Cache Schedule](client-cache-schedule.md) | Client `max-age` before a cutover |
| [Operations](operations.md) | Diagnostics map, Admin, Prometheus |
| [Comparison](comparison.md) | Hand-rolled stack vs this library |
| [FAQ](faq.md) | Common mistakes |

## Learn by running

| Sample | What you get |
|--------|----------------|
| [Minimal](../../samples/CacheOrchestrator.Minimal) | One endpoint, miss then hit |
| [Playground](../../samples/CacheOrchestrator.Sample) | TTLs, schedule, Redis, CRUD UI |
| [Topology labs](../../samples/CacheOrchestrator.Sample/labs/README.md) | Docker stages 01–05 |

Labs teach layers; they are not a production blueprint. Production: [deployment](../reference/deployment.md).
