![CacheOrchestrator Logo](docs/assets/logo.png)

# CacheOrchestrator

[![NuGet](https://img.shields.io/nuget/v/CacheOrchestrator.svg?style=flat-square)](https://www.nuget.org/packages/CacheOrchestrator/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](https://opensource.org/licenses/MIT)
[![Build Status](https://img.shields.io/github/actions/workflow/status/amarinsek/CacheOrchestrator/build.yml?branch=main&style=flat-square)](https://github.com/amarinsek/CacheOrchestrator/actions)

**CacheOrchestrator** is domain-based caching for ASP.NET Core: define rules once per domain in configuration, then apply them on endpoints with a single attribute or extension. It orchestrates Output Cache (OC), FusionCache (L1/L2), and client Cache-Control (CC) under the same model.

![Cache topology](docs/assets/drawing01.png)

Understanding the role of each layer in this pipeline is crucial for maximum performance and system health:
- Proper CC management prevents unnecessary requests to the backend.
- Proper OC management enables lightning-fast responses from the backend.
- Proper L1/L2 management avoids costly factory runs.

In practice, real-world applications require diverse combinations of these layers. An architecture might range from a simple in-memory OC to complex distributed topologies combining OC with L1/L2 data caches and Redis backplanes. Furthermore, managing varying Time-To-Live (TTL) and data lifecycle policies across all these layers for different service/data needs adds significant complexity.

**CacheOrchestrator**  simplifies this. It provides a domain-based approach that allows you to define your entire multi-layer cache structure and its specific properties purely through configuration. You can then effortlessly apply these sophisticated caching strategies directly to your ASP.NET Core endpoints or controllers.

| | |
|--|--|
| **Target** | .NET 8 and .NET 10 |
| **Try now** | [`samples/CacheOrchestrator.Minimal`](samples/CacheOrchestrator.Minimal) — zero boilerplate, InMemory only |
| **Explore** | [`samples/CacheOrchestrator.Sample`](samples/CacheOrchestrator.Sample) — interactive playground |

---

## Why domains?

| Domain example | How often it changes | Cache style & Topology |
| --- | --- | --- |
| Satellite imagery | ~ Yearly | Very long server + client TTL (Output Cache + L1 InMemory) |
| OSM map tiles | ~ Monthly | Long TTL + scheduled client ramp-down before cutover (Output Cache + L1 InMemory)|
| Floating Car Data (FCD) | ~ Minutes | Short TTL + L1 InMemory + Distributed L2 Redis + Backplane (multi-instance sync across replicas) |
| Live vehicle tracking | ~ Seconds | Short TTL + FusionCache L1 with Lock & Fail-Safe (anti-stampede / request collapsing) |


Declare a domain once, apply it with `.CacheOutputWithDomain("…")` or `[CacheDomain("…")]`.

---

## Try it in one minute

Nothing to type into a new project — run the sample:

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
```

Then (second terminal):

```bash
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

| Request | What you should see |
|---------|---------------------|
| 1st | `X-Cache: … output=miss` (~200 ms simulated work) |
| 2nd | `X-Cache: … output=hit` (served from Output Cache) |


→ Details: [samples/CacheOrchestrator.Minimal/README.md](samples/CacheOrchestrator.Minimal/README.md)

---

## Install

```bash
dotnet add package CacheOrchestrator

# Optional — Redis Output Cache store + FusionCache L2 / backplane:
dotnet add package CacheOrchestrator.Redis

# Optional — multi-instance command bus (invalidate / Version / TTL across InMemory nodes):
dotnet add package CacheOrchestrator.Bus

# Optional — invalidate after EF Core SaveChanges (not an EF cache provider):
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

---

## Minimal setup (copy into your app)

InMemory only — no Redis required.

### 1. `appsettings.json`

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

### 2. `Program.cs`

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator(); // after routing middleware is configured

app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, async ct =>
    {
        // Load from DB / service
        return await LoadProductsAsync(ct);
    });
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");

app.Run();
```

That is the happy path: **domain in config + one endpoint decoration + `GetOrSetAsync`**.

**MVC / controllers:** put `[CacheDomain("catalog")]` on the controller or action and inject `IDomainFusionCache` the same way.

**Redis later:** install `CacheOrchestrator.Redis`, call `o.AddRedisBackend()`, set `"Provider": "Redis"` — see [docs/backends.md](docs/backends.md).

**Multi-instance InMemory later:** install `CacheOrchestrator.Bus`, call `o.AddHttpClusterBus()` + `MapCacheOrchestratorHttpBus()` — see [docs/cluster-bus.md](docs/cluster-bus.md).

**Ops later:** enable Local Admin (`Cache:Admin:Enabled` + `MapCacheOrchestratorAdmin`); for multi-node UI run `src/CacheOrchestrator.Admin` — see [docs/admin.md](docs/admin.md).

**More walkthrough:** [docs/getting-started.md](docs/getting-started.md)

---

## What you get (feature overview)

Everything below is available without opening other pages first. Links go deeper when you need them.

### Core

| Capability | What it does |
|------------|----------------|
| **Domains** | Named packages of rules (TTL, Version, client headers, Fusion instance). Applied via `.CacheOutputWithDomain` / `[CacheDomain]`. [output-cache](docs/output-cache.md) |
| **Output Cache (OC)** | Full HTTP GET/HEAD response caching (ASP.NET Core). |
| **FusionCache (L1/L2)** | Application object cache via `IDomainFusionCache` — memory ± optional distributed. [fusion-cache](docs/fusion-cache.md) |
| **Client `Cache-Control`** | Browser/CDN `max-age` / public / private / no-store from domain settings. |
| **Version stamp** | Change `Version` in config → new key space; old entries age out. [invalidation](docs/invalidation.md) |
| **Invalidation API** | `ICacheOrchestratorInvalidator` — domain, entity, or tags. Structured results + optional observers. |
| **`X-Cache` header** | Diagnostic header (`domain`, `output`, `data`, `phase`, …). Toggle with `EmitDiagnosticsHeaders` (default on). [observability](docs/observability.md) |
| **Metrics & tracing** | Meter / activity source `CacheOrchestrator` (independent of response headers). |
| **Health checks** | `AddHealthChecks().AddCacheOrchestrator()` — backend probes (e.g. Redis ping). |

### Advanced

| Capability | What it does |
|------------|----------------|
| **Client Cache Schedule** | Near a planned cutover (`ScheduledUpdateUtc`), client `max-age` ramps from long → short (Calm / Approaching / Hold). Server TTLs unchanged. [client-cache-schedule](docs/client-cache-schedule.md) |
| **Snapshot vs dynamic domains** | OSM-style generation stamps vs CRUD + per-entity invalidation. [domain-profiles](docs/domain-profiles.md) |
| **ETag modes** | `Version` (generation), `Resource` (per URL/id), or `None`. |
| **Entity / resource id** | `GetOrSetEntityAsync(http, domain, entityKind, resourceId, …)` + `InvalidateEntityAsync(domain, entityKind, id)`. |
| **EF Core SaveChanges** | `CacheOrchestrator.EFCore.Invalidation` — interceptor maps CLR types in code and purges entity tags after a successful save. [ef-core-invalidation](docs/ef-core-invalidation.md) |
| **Auth controls** | Default: skip Output Cache for authenticated / `Authorization`. Opt-in with `BypassWhenAuthenticated` + `VaryOutputCacheByUser`. |
| **Named Fusion instances** | Map domains to separate Redis clusters (e.g. PII vs catalog). [deployment](docs/deployment.md) |
| **Redis package** | `CacheOrchestrator.Redis` — OC store + keyed L2 + backplane. Not in core. |
| **Cluster command bus** | `CacheOrchestrator.Bus` — optional HTTP fan-out of invalidate / Version / TTL commands across instances (InMemory multi-node). Zero effect if unused. [cluster-bus](docs/cluster-bus.md) |
| **Local Admin API** | Opt-in on each app (`Cache:Admin:Enabled` + `MapCacheOrchestratorAdmin`) — live stats, health, invalidate, runtime Version/TTL. Off by default (zero cost). [admin](docs/admin.md) |
| **Admin App** | Separate ops host (`src/CacheOrchestrator.Admin`, not a NuGet package) — multi-instance SPA + fan-out / bus-distribute. [admin](docs/admin.md) · [Admin README](src/CacheOrchestrator.Admin/README.md) |
| **Custom backends** | `ICacheBackendRegistrar` / `AddBackend` — not a drop-in `"Provider": "SqlServer"` without your registrar. [backends](docs/backends.md) · [comparison](docs/comparison.md) |
| **Fail-safe / soft-hard TTL** | Fusion fail-safe, soft/hard duration, jitter, eager refresh, factory timeouts — domain-configured. |
| **Tracking query strip** | `utm_*`, `gclid`, … ignored in cache keys so campaigns do not fragment the cache. |
| **Multi-instance deployment** | InMemory vs Redis topologies, mixed backends, backplane, optional Bus. [deployment](docs/deployment.md) · [cluster-bus](docs/cluster-bus.md) |
| **Pluggable invalidation observers** | Hook audit/webhooks on successful invalidations (not a substitute for Bus fan-out). |

---

## Documentation

Start here, then go deep only when you need to:

| | Doc |
|--|-----|
| **Start** | [docs/getting-started.md](docs/getting-started.md) · [docs/README.md](docs/README.md) (full index) |
| **Try** | [Minimal sample](samples/CacheOrchestrator.Minimal) · [Playground sample](samples/CacheOrchestrator.Sample) |
| **Gotchas** | [docs/faq.md](docs/faq.md) · [docs/comparison.md](docs/comparison.md) |

| Topic | Doc |
|-------|-----|
| Domain profiles (snapshot / CRUD) | [docs/domain-profiles.md](docs/domain-profiles.md) |
| Client Cache Schedule | [docs/client-cache-schedule.md](docs/client-cache-schedule.md) |
| Configuration reference | [docs/configuration.md](docs/configuration.md) |
| Output Cache | [docs/output-cache.md](docs/output-cache.md) |
| FusionCache | [docs/fusion-cache.md](docs/fusion-cache.md) |
| Cache keys | [docs/cache-keys.md](docs/cache-keys.md) |
| Invalidation | [docs/invalidation.md](docs/invalidation.md) |
| EF Core SaveChanges invalidation | [docs/ef-core-invalidation.md](docs/ef-core-invalidation.md) · [package README](src/CacheOrchestrator.EFCore.Invalidation/README.md) |
| Cluster bus (multi-instance commands) | [docs/cluster-bus.md](docs/cluster-bus.md) · [src/CacheOrchestrator.Bus/README.md](src/CacheOrchestrator.Bus/README.md) |
| Backends | [docs/backends.md](docs/backends.md) |
| Observability | [docs/observability.md](docs/observability.md) |
| Admin (Local API + fan-out UI) | [docs/admin.md](docs/admin.md) · [src/CacheOrchestrator.Admin/README.md](src/CacheOrchestrator.Admin/README.md) |
| Deployment | [docs/deployment.md](docs/deployment.md) |
| Architecture | [docs/architecture.md](docs/architecture.md) |
| Benchmarks | [docs/benchmarks/results.md](docs/benchmarks/results.md) |

**API reference:** XML docs ship with the NuGet packages ([CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/), [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/), [CacheOrchestrator.Bus](https://www.nuget.org/packages/CacheOrchestrator.Bus/), [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/)). DocFX site planned post-1.0.

| Project | |
|---------|--|
| [CHANGELOG.md](CHANGELOG.md) | Releases |
| [docs/releasing.md](docs/releasing.md) | Version tags (MinVer), NuGet publish |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Build, test, PRs, community expectations |
| [SECURITY.md](SECURITY.md) | Vulnerability reporting |
| [LICENSE.md](LICENSE.md) | MIT |
