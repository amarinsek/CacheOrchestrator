<img src="docs/assets/logo.png" height="100" />

# CacheOrchestrator

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-blueviolet.svg?style=flat-square)](https://www.nuget.org/packages/CacheOrchestrator/)
[![Build Status](https://img.shields.io/github/actions/workflow/status/amarinsek/CacheOrchestrator/build.yml?branch=main&style=flat-square)](https://github.com/amarinsek/CacheOrchestrator/actions)


**CacheOrchestrator** unifies the configuration of Output Cache, data cache, and client Cache-Control within a single **domain** model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

<img src="docs/assets/drawing-01.svg" height="350" />

<br>
The picture is the path a request can take:

- **Client Cache-Control (CC)** — prevents unnecessary requests from reaching the server.
- **Output Cache (OC)** — serves the stored HTTP response so the endpoint need not run.
- **Data cache (L1/L2)** — serves the stored object so the factory (your database or service) need not run.

In real-world applications, these layers are often fragmented - for example, an in-memory Output Cache on one end, a Fusion Cache with Redis L2 data cache on the other, all with varying TTLs. When these layers aren't synchronized, they actively work against each other. CacheOrchestrator unifies these layers within a single **domain** model, keeping lifetimes and invalidations perfectly in step. 

Cache **invalidation** presents the exact same coordination problem. Clearing the data cache is useless if the Output Cache or the client's browser is still serving a stale HTTP response. CacheOrchestrator solves this by treating invalidation as a single, unified action—when data changes, it is reliably retired across every layer that holds it. 

---

## Benefits

- **Less code on the endpoint.** Output Cache policies, data-cache options, and Client Cache-Control settings live in the domain model. Your endpoints stay clean and just load data. 
- **Policy and topology in settings.** TTLs, client cacheability, InMemory versus Redis, a second data-cache instance, or a planned cutover are all handled via configuration. You don't need to change the handler.
- **One generation, one invalidation.** A coordinated invalidation or a new generation stamp reliably retires the stale data in every layer that still holds it.

See the code [comparison](docs/guide/comparison.md) to see how much boilerplate CacheOrchestrator actually removes.

---

## Quick start

Install the meta package (AspNetCore + FusionCache):

```bash
dotnet add package CacheOrchestrator
```

Configure a domain in `appsettings.json`:

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 },
        "OutputCache": { "TtlSeconds": 120 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 60 }
      }
    }
  }
}
```

Register the library in your `Program.cs`:

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();
```

Apply the domain to an endpoint. The `IDomainDataCache` automatically inherits the policies defined for the `"catalog"` domain:

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

> **Note for Controllers & Class Libraries:**
> On a traditional controller, use the `[CacheDomain("catalog")]` attribute and inject `IDomainDataCache` in the same way. Class libraries can depend directly on `ICacheOrchestrator` from the Core package instead, using the exact same domain policies, without needing an `HttpContext`.

---

## A one-minute trial

Clone the repository and run the minimal sample:

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
```

In another terminal, test the endpoint:

```bash
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

The first response is an Output Cache miss (the sample waits about 200 ms). The second is a hit. Look at the `X-Cache` header: `oc=miss`, then `oc=hit`.

* **Minimal Sample:** [samples/CacheOrchestrator.Minimal](samples/CacheOrchestrator.Minimal) — Notes for the sample above.
* **Full Playground:** [samples/CacheOrchestrator.Sample](samples/CacheOrchestrator.Sample) — Features TTLs, schedule, Redis, and CRUD.

---

## Playground topology labs

To try **multi-layer layouts** (Admin Console, Prometheus, Redis L2, multiple instances, cluster bus) without wiring Docker yourself, use the playground **topology labs** — one Compose command per stage. 

```bash
docker compose -f samples/CacheOrchestrator.Sample/labs/compose/01-observability.yml up --build
```

Stages climb from a single InMemory playground to a dual Redis + HTTP bus architecture. 

* **Full guide & diagrams:** [samples/CacheOrchestrator.Sample/labs/README.md](samples/CacheOrchestrator.Sample/labs/README.md) — See what each stage teaches and how they evolve.

---

## Why domains

A domain is a named set of cache rules: lifetimes, which layers to use, and where those layers live. Different data requires a different mix. For example:

- **Satellite imagery** changes perhaps once a year. Long Output Cache and client lifetimes are enough; data cache is optional.
- **Map tiles & batched datasets.** Data like satellite imagery or monthly catalog extracts change on a published schedule. Client lifetimes stay extremely long for months to save bandwidth, but the `max-age` is automatically shortened as the cutover approaches so clients refresh exactly on time. Output Cache can stay in-process.
- **Floating car data** ages in minutes. A short lifetime, in-memory Output Cache, and a shared Redis data cache with a backplane keep several instances consistent.
- **Live vehicle positions** age in seconds. FusionCache locking and fail-safe stop a stampede when many callers miss at once; Output Cache stays off or very short.

The endpoint code is the same shape in every case. The domain is what differs.

Domains are the unit of configuration. Within a domain you can optionally use **entity identity** (`entityKind` + id, and related footprints) so per-row keys and invalidation are possible.

---

## Also included

- **Coordinated policies.** A single domain governs both client and backend cache policies. [Output Cache](docs/reference/output-cache.md) · [Data cache](docs/reference/data-cache.md) · [Packages](docs/guide/packages.md)

- **Coordinated invalidation.** Invalidation by domain, entity kind, or specific ID is seamlessly coordinated across Output Cache and data cache. [Invalidation](docs/reference/invalidation.md)

- **Variety of cache topologies.** InMemory only; InMemory Output Cache with Redis L2 data cache; Redis for both plus a backplane; or InMemory nodes synchronized via the HTTP cluster bus. [Backends](docs/reference/backends.md) · [Deployment](docs/reference/deployment.md) · [Cluster bus](docs/reference/cluster-bus.md)

- **Planned cutovers.** A Version bump starts a new generation, or a [Client Cache Schedule](docs/guide/client-cache-schedule.md) eases clients perfectly into the cutover.

- **Multiple instances.** Shared data-cache objects use Redis L2 and the FusionCache backplane. When Output Cache stays per-process, the [cluster bus](docs/reference/cluster-bus.md) carries invalidation commands and runtime Version/settings across instances.

- **Diagnostics.** Insights via the `X-Cache` response header (domain, `oc`/`dc` status, schedule phase), plus OpenTelemetry metrics, activity sources, and health checks. [Observability](docs/reference/observability.md)

- **Admin API & Console.** An embedded Admin API and a standalone **Admin Console App** for monitoring and managing your cache instances. [Admin](docs/reference/admin.md)

---

## Packages

The library is **modular**. The Core package provides the foundational policies and the ICacheOrchestrator interface. From there, you can opt into specific packages to match your stack: FusionCache or HybridCache for the data engine, ASP.NET Output and Client Cache, Redis, an HTTP cluster bus, and EF Core for automatic invalidation. See the [Packages and composition](docs/guide/packages.md) guide to learn how to wire them together.

| Package | Purpose |
|---------|---------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta package: AspNetCore + FusionCache (for typical web apps). |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Domain models, `ICacheOrchestrator`, and invalidation contracts (no ASP.NET dependency). |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | Output Cache, Client Cache-Control, HTTP helpers, and embedded Local Admin. |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | ZiggyCreatures FusionCache data-cache provider. |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Microsoft HybridCache data-cache provider. |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis integration for Output Cache and L2 / backplane support. |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Syncs invalidations, versions, and settings across all instances via HTTP cluster bus. |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Automatic cache invalidation after a successful Entity Framework Core `SaveChanges`. |


---

## Applications

| Application | Purpose |
|---------|---------|
| [CacheOrchestrator.AdminConsole](src/CacheOrchestrator.AdminConsole/) | Standalone Admin Console for live stats, domain configuration, triggering invalidations, and adjusting Versions or TTLs on the fly. Available as a Docker image: `ghcr.io/amarinsek/cacheorchestrator-admin-console` — see [Admin Console](src/CacheOrchestrator.AdminConsole/) · [Deploy Admin](deploy/admin/README.md). |

---

<br>

> [!NOTE]
> **Important Versioning Notice**
> 
> **CacheOrchestrator** is currently undergoing a **full redesign**, culminating in the upcoming **v3.0.0** release.
> 
> * **v1.0.0 & v2.1.x (Legacy):** Published on NuGet strictly to ensure continuity for existing environments. These versions are no longer receiving active feature development. 
> * **v3.0.0 (Active Development):** This major release is a completely new architectural surface. It brings a modernized, highly modular codebase, but **does not preserve API compatibility** with 1.x/2.x.
> 
> **For new projects:** If you are evaluating CacheOrchestrator or planning a new integration, we strongly recommend waiting for the v3.0.0 release, as there will be no direct migration path from v2 to v3. 
> 
> **Early access & contributing:** If you want an early look at the new architecture or wish to contribute to v3, you are welcome to clone the `main` branch. Please be aware that `main` is under heavy development and subject to breaking changes.


## Documentation

- [Getting started](docs/guide/getting-started.md) — first endpoint, `X-Cache`, what to read next
- [Guide](docs/guide/README.md) — concepts, topologies, operations
- [Documentation index](docs/README.md) — configuration, keys, deployment, architecture
- [FAQ](docs/guide/faq.md) — common mistakes and limits
- [Comparison](docs/guide/comparison.md) — the usual stack versus CacheOrchestrator

- [CHANGELOG](CHANGELOG.md)
- [Contributing](CONTRIBUTING.md)
- [Security](SECURITY.md)
- [License](LICENSE.md) (MIT)