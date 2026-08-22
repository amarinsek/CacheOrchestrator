![CacheOrchestrator Logo](docs/assets/logo.png)

# CacheOrchestrator

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![Build Status](https://img.shields.io/github/actions/workflow/status/amarinsek/CacheOrchestrator/build.yml?branch=main&style=flat-square)](https://github.com/amarinsek/CacheOrchestrator/actions)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-blueviolet.svg?style=flat-square)](https://www.nuget.org/packages/CacheOrchestrator/)

**CacheOrchestrator** configures and coordinates three existing layers in ASP.NET Core — Output Cache (OC), FusionCache (L1/L2), and client Cache-Control (CC) — under one **domain** model. Define the rules once in configuration, then apply them on endpoints with a single attribute or extension. It does not replace those systems or own a store: ASP.NET still holds the HTTP response, FusionCache still holds the object, and the browser or CDN still honours `Cache-Control`.

<img src="docs/assets/drawing-01.svg" height="350" />

The picture is the path a request can take:

- **Client Cache-Control (CC)** — prevents unnecessary requests from reaching the server.
- **Output Cache (OC)** — serves the stored HTTP response so the endpoint need not run.
- **FusionCache (L1/L2)** — serves the stored object so the factory (your database or service) need not run.

Applications mix these layers: in-memory Output Cache on one service, Redis FusionCache and a backplane on another, different TTLs for different data. If the layers disagree, one undoes the other.

Invalidation is the same coordination problem. Clearing FusionCache while Output Cache or the Client still holds the old response leaves callers on stale bytes. A coordinated invalidation retires that generation in every layer that still has it.

CacheOrchestrator holds the mix in one place, the **domain**, so lifetimes and invalidation stay in step.

---

## Benefits

- **Less code on the endpoint.** Output Cache policy, Fusion options, Client `Cache-Control` and other settings live on the domain. The route just loads data. 
- **Policy and topology in settings.** TTLs, client cacheability, InMemory versus Redis, a second Fusion instance, or a planned cutover are configuration. You do not change the handler.
- **One generation, one invalidation.** A coordinated invalidation or a new generation stamp retires the old bytes in every layer that still has them.

For a line-count comparison of the same endpoint both ways, see [comparison](docs/comparison.md).

---

## Quick start

Install the core package:

```bash
dotnet add package CacheOrchestrator
```


Configure a domain in `appsettings.json`:

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "Domains": {
      "catalog": {
        "Version": "1",
        "ClientCacheability": "Public",
        "ClientTtlSeconds": 60,
        "OutputCacheTtlSeconds": 120,
        "FusionCacheSoftTtlSeconds": 300
      }
    }
  }
}
```
Register the library:

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();
```

Apply the domain to an endpoint:

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

On a controller, use `[CacheDomain("catalog")]` and inject `IDomainFusionCache` in the same way.

A longer walkthrough is in [docs/getting-started.md](docs/getting-started.md). For the same endpoint written with Output Cache and FusionCache by hand, see [comparison](docs/comparison.md).

---

## A one-minute trial

Clone the repository and run the minimal sample:

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
```

In another terminal:

```bash
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

The first response is an Output Cache miss (the sample waits about 200 ms). The second is a hit. Look at the `X-Cache` header: `oc=miss`, then `oc=hit`.

Notes for the sample: [samples/CacheOrchestrator.Minimal](samples/CacheOrchestrator.Minimal). For a playground with TTLs, schedule, Redis, and CRUD, see [samples/CacheOrchestrator.Sample](samples/CacheOrchestrator.Sample).

---

## Playground topology labs

To try **multi-layer layouts** (Admin Console, Prometheus, Redis L2, multiple instances, cluster bus) without wiring Docker yourself, use the playground **topology labs** — one Compose command per stage. 

```bash
docker compose -f samples/CacheOrchestrator.Sample/labs/compose/01-observability.yml up --build
```

Stages climb from a single InMemory playground to dual Redis + HTTP bus. Full guide, diagrams, and what each stage teaches: [samples/CacheOrchestrator.Sample/labs/README.md](samples/CacheOrchestrator.Sample/labs/README.md).

---

## Why domains

A domain is a named set of cache rules: lifetimes, which layers to use, and where those layers live. Different data wants a different mix. For example:

- **Satellite imagery** changes perhaps once a year. Long Output Cache and client lifetimes are enough; FusionCache is optional.
- **Map tiles** change on a published schedule. Lifetimes stay long, then client `max-age` is shortened as the cutover approaches. Output Cache can stay in-process.
- **Floating car data** ages in minutes. A short lifetime, in-memory Output Cache, and FusionCache on shared Redis with a backplane keep several instances consistent.
- **Live vehicle positions** age in seconds. FusionCache locking and fail-safe stop a stampede when many callers miss at once; Output Cache stays off or very short.

The endpoint code is the same shape in every case. The domain is what differs.

Domains are the unit of configuration. Within a domain you can optionally use **entity identity** (`entityKind` + id, and related footprints) so per-row keys and invalidation stay precise. That refines caching **inside** a domain; it is not a second configuration root. ORM packages such as EF Core invalidation only map writes onto the same entity tags.

---

## Also included

- **Coordinated policies.** One domain owns all client and backend cache policies. [Output Cache](docs/output-cache.md) · [FusionCache](docs/fusion-cache.md)

- **Coordinated invalidation.** Domain, kind, or a single id invalidation is coordinated across Output Cache and FusionCache. [Invalidation](docs/invalidation.md)

- **Variety of cache topologies.** InMemory only; InMemory Output Cache with Redis Fusion L2; Redis for both plus a backplane; or InMemory nodes with Cluster bus. [Backends](docs/backends.md) · [Deployment](docs/deployment.md) · [Cluster bus](docs/cluster-bus.md)

- **A planned cutover.** A Version bump starts a new generation, or [Client Cache Schedule](docs/client-cache-schedule.md) eases clients into the cutover.

- **Multiple instances.** Shared Fusion data uses Redis L2 and the backplane. When Output Cache stays per process, the [cluster bus](docs/cluster-bus.md) carries invalidation and runtime Version / TTL.

- **Diagnostics.** `X-Cache` on the response (domain, hit or miss, schedule phase), a meter, an activity source, and a health check. [Observability](docs/observability.md)

- **Admin API** and a separate **Admin Console App** for monitoring and administrating instances. [Admin](docs/admin.md)

---

## Optional packages

| Package | Purpose |
|---------|---------|
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis for Output Cache and for FusionCache L2 / backplane. |
| [CacheOrchestrator.Bus](https://www.nuget.org/packages/CacheOrchestrator.Bus/) | Invalidate, Version, and TTL commands delivered to every instance. |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Invalidate entity after a successful EF Core `SaveChanges`. |

---

## Applications

| Application | Purpose |
|---------|---------|
| [CacheOrchestrator.AdminConsole](src/CacheOrchestrator.AdminConsole/) | Admin Console App: live stats, domain settings, invalidation, Version and TTL. Docker: `ghcr.io/amarinsek/cacheorchestrator-admin-console` — [deploy/admin](deploy/admin/README.md). |

---

> [!NOTE]
> ## **Versioning Info**
> 
> Currently, **CacheOrchestrator** is undergoing a significant architectural refactoring, which will culminate in the upcoming **v3.0.0** release. 
> 
> * **v1.0.0 & v2.1.x (Legacy):** These versions are published on NuGet strictly to ensure continuity for existing environments that are already integrated with them. Please note that these versions contain some known issues and are no longer receiving active feature development. 
> * **v3.0.0 (Active Development):** This upcoming major release brings a modernized codebase, resolves previous issues, and introduces substantial architectural improvements. 
> 
> If you are evaluating **CacheOrchestrator** or planning a new integration, we strongly recommend waiting for the v3.0.0 release rather than adopting the legacy 1.x or 2.x versions. 
> 
> If you want an early look at the new architecture, wish to test the latest improvements, or want to contribute to the upcoming v3, you are welcome to clone the `main` branch. Please be aware that `main` is currently under heavy development and may be subject to breaking changes before the final release.

---

## Documentation

- [Getting started](docs/getting-started.md) — first endpoint, `X-Cache`, what to read next
- [Guide](docs/guide/README.md) — concepts, topologies, operations
- [Documentation index](docs/README.md) — configuration, keys, deployment, architecture
- [FAQ](docs/faq.md) — common mistakes and limits
- [Comparison](docs/comparison.md) — the usual stack versus CacheOrchestrator

- [CHANGELOG](CHANGELOG.md)
- [Contributing](CONTRIBUTING.md)
- [Security](SECURITY.md)
- [License](LICENSE.md) (MIT)

