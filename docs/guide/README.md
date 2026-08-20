# CacheOrchestrator guide

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

This layer sits between the product README and the technical reference. It answers how to think about domains, which package and topology to use, and how to operate the cache — without the full `appsettings` schema.

You do not need every reference page to ship a first domain.

## Who you are

| You | Start here | Then |
|-----|------------|------|
| App developer, first endpoint | [Getting started](../getting-started.md) | [Concepts](concepts.md) |
| Choosing InMemory vs Redis vs Bus | [Topologies](topologies.md) | [Deployment](../deployment.md) or [labs](../../samples/CacheOrchestrator.Sample/labs/README.md) |
| Snapshot dataset vs CRUD | [Domain profiles](../domain-profiles.md) | [Invalidation](../invalidation.md) |
| Operator (stats, invalidate, Docker) | [Operations](operations.md) | [admin.md](../admin.md) |
| Something looks wrong | [FAQ](../faq.md) | The topic page the FAQ links |

## Ten-minute path

1. [Root README](../../README.md) — is this the right library.
2. [Minimal sample](../../samples/CacheOrchestrator.Minimal) or [Getting started](../getting-started.md) — a miss, then a hit.
3. [Concepts](concepts.md) — domain, three layers, Version vs TTL vs tags.
4. [Topologies](topologies.md) — which package to install.
5. [Domain profiles](../domain-profiles.md) — snapshot vs CRUD.
6. A reference page when you need the schema or API: [configuration](../configuration.md), [output-cache](../output-cache.md), [fusion-cache](../fusion-cache.md).

## Guide pages

| Page | What it is for |
|------|----------------|
| [Getting started](../getting-started.md) | Install, first endpoint, `X-Cache` |
| [Concepts](concepts.md) | Mental model |
| [Topologies](topologies.md) | Packages and layouts |
| [Operations](operations.md) | Diagnostics, Admin, Prometheus |
| [Domain profiles](../domain-profiles.md) | Snapshot vs dynamic |
| [Client Cache Schedule](../client-cache-schedule.md) | Client `max-age` before a cutover |
| [Comparison](../comparison.md) | Hand-rolled Output Cache + FusionCache vs this library |
| [FAQ](../faq.md) | Common mistakes |

## Learn by running

| Sample | What you get |
|--------|----------------|
| [Minimal](../../samples/CacheOrchestrator.Minimal) | One endpoint, in-memory, miss then hit |
| [Playground](../../samples/CacheOrchestrator.Sample) | TTLs, schedule, Redis, CRUD UI |
| [Topology labs](../../samples/CacheOrchestrator.Sample/labs/README.md) | Docker stages 01–05 (Prometheus, Redis, multi-instance, bus) |

Labs are a teaching environment, not a production blueprint. Production layouts: [deployment.md](../deployment.md).

## When you need the schema

The [documentation index](../README.md) lists every reference page. Typical jumps:

- Settings: [configuration.md](../configuration.md)
- HTTP policy: [output-cache.md](../output-cache.md)
- Fusion API: [fusion-cache.md](../fusion-cache.md)
- Keys and Namespace: [cache-keys.md](../cache-keys.md)
- Vary and auth: [vary.md](../vary.md)
