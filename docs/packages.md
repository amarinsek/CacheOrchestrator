# Packages and composition

> **Reference.** Product overview: [root README](../README.md). Catalog: [documentation index](README.md). Quick path: [getting-started](getting-started.md).

CacheOrchestrator is split so **policy and orchestration** live in Core, while **engines and HTTP** are optional packages. The application chooses topology with NuGet references and DI — domain rules and call sites stay stable.

Dependency rule: arrows point at **Core**. Core never references ASP.NET, FusionCache, HybridCache, Redis, HttpBus, or EF.

---

## Package map

| Package | Role |
|---------|------|
| [**CacheOrchestrator.Core**](../src/CacheOrchestrator.Core/README.md) | Domains, Version, portable `DataCache` policy, entity footprint/tags, `ICacheOrchestrator`, `CacheDomainContext`, invalidation and cluster **contracts** |
| [**CacheOrchestrator.FusionCache**](../src/CacheOrchestrator.FusionCache/README.md) | ZiggyCreatures FusionCache as `IDataCacheProvider`; owns JSON `FusionCache` knobs |
| [**CacheOrchestrator.HybridCache**](../src/CacheOrchestrator.HybridCache/README.md) | Microsoft HybridCache as `IDataCacheProvider` |
| [**CacheOrchestrator.AspNetCore**](../src/CacheOrchestrator.AspNetCore/README.md) | Output Cache, Client Cache-Control, HTTP `IDomainDataCache`, Admin API |
| [**CacheOrchestrator**](../src/CacheOrchestrator/README.md) (meta) | AspNetCore + FusionCache; `AddCacheOrchestrator` wires both |
| [**CacheOrchestrator.Redis**](../src/CacheOrchestrator.Redis/README.md) | Redis Output Cache store and Fusion L2 / backplane |
| [**CacheOrchestrator.HttpBus**](../src/CacheOrchestrator.HttpBus/README.md) | HTTP cluster command bus |
| [**CacheOrchestrator.EFCore.Invalidation**](../src/CacheOrchestrator.EFCore.Invalidation/README.md) | After `SaveChanges`, purge via the invalidator |

**Admin Console App** is a separate host, not a NuGet package. [admin.md](admin.md).

| API | Package | Role |
|-----|---------|------|
| `ICacheOrchestrator` | Core | Http-free data get-or-create |
| `CacheDomainContext` | Core | Host-supplied domain (+ optional entity kind) for libraries |
| `IDomainDataCache` | AspNetCore | HTTP projection over `ICacheOrchestrator` |

---

## Use-case matrix

| # | Host | Data | Output Cache | Typical packages |
|---|------|------|--------------|------------------|
| **1** | Web | Fusion (InMemory) | yes | Meta *(or AspNetCore + Fusion)* |
| **2** | Web | — | yes | AspNetCore only |
| **3** | Web | Fusion | no | AspNetCore + FusionCache |
| **4** | Web | Fusion (Redis L2) | yes | Meta + Redis |
| **5** | Web | Hybrid | yes | AspNetCore + HybridCache |
| **6** | Web | Fusion | yes (dynamic domain) | Meta *(or AspNetCore + Fusion)* |
| **7** | Library + web / worker | Fusion | yes / n/a | Core in library; Meta (or Fusion) in host |

Each scenario below uses the **same product endpoint shape** where possible. Differences are in **registration** and **config**. Base Output Cache policy is `NoCache` — without `.CacheOutputWithDomain` there is no OC entry.

---

## 1. Typical web — OC + data cache + client headers (InMemory Fusion)

Uses the **meta** package `CacheOrchestrator` (`AddCacheOrchestrator` = AspNetCore + Fusion). Equivalent without meta: reference `CacheOrchestrator.AspNetCore` + `CacheOrchestrator.FusionCache` and call `AddCacheOrchestratorAspNetCore` then `AddCacheOrchestratorFusionCache`.

Per-domain flags `OutputCache:Enabled` / `DataCache:Enabled` turn layers on or off in config without changing the endpoint code (see also §2 / §3 when you want fewer packages).

**Registration**

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.OutputCache;

builder.Services.AddCacheOrchestrator(builder.Configuration);
```

**Config**

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Enabled": true, "Ttl": "00:05:00" },
        "OutputCache": { "Enabled": true, "Ttl": "00:01:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

**Code**

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.OutputCache;

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

---

## 2. Output Cache only (AspNetCore package)

Packages: **`CacheOrchestrator.AspNetCore`** only (no Fusion / Hybrid). No data-cache provider — the handler does not use `IDomainDataCache`.

**Registration**

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.OutputCache;

builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
```

**Config**

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "Domains": {
      "catalog": {
        "Version": "1",
        "OutputCache": { "Enabled": true, "Ttl": "00:01:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

**Code**

```csharp
using CacheOrchestrator.OutputCache;

app.MapGet("/api/products/{id}", async (string id) =>
{
    var data = await LoadProductAsync(id);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

---

## 3. Data cache only (AspNetCore + FusionCache)

Packages: **`CacheOrchestrator.AspNetCore`** + **`CacheOrchestrator.FusionCache`** (not the meta convenience entry). No `.CacheOutputWithDomain` — base Output Cache policy is `NoCache`.

**Registration**

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;

builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
```

**Config**

```json
{
  "Cache": {
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Enabled": true, "Ttl": "00:05:00" }
      }
    }
  }
}
```

**Code** — same `GetOrSetAsync` shape as §1; pass the domain because the route has no OC domain metadata:

```csharp
using CacheOrchestrator.DataCache;

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, "catalog", ct => LoadProductAsync(id, ct));
    return Results.Json(data);
});
```

---

## 4. Redis data-cache L2 (Fusion) + InMemory Output Cache

Packages: **meta** `CacheOrchestrator` + **`CacheOrchestrator.Redis`**.

**Registration**

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.OutputCache;
using CacheOrchestrator.Redis;

builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());
```

**Config**

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "Redis" } },
    "Redis": { "Configuration": "localhost:6379" },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Enabled": true, "Ttl": "00:05:00" },
        "OutputCache": { "Enabled": true, "Ttl": "00:01:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

**Code** (same as §1)

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.OutputCache;

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

For Redis as the Output Cache store as well, set `"OutputCache": { "Provider": "Redis" }` (same registration).

---

## 5. HybridCache data provider

Packages: **`CacheOrchestrator.AspNetCore`** + **`CacheOrchestrator.HybridCache`** (+ Microsoft HybridCache).

**Registration**

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.OutputCache;

builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache();
```

**Config**

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Enabled": true, "Ttl": "00:05:00" },
        "OutputCache": { "Enabled": true, "Ttl": "00:01:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

**Code** (same as §1)

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.OutputCache;

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

---

## 6. Dynamic domain from the route

Packages: **meta** `CacheOrchestrator` (or AspNetCore + Fusion separately, as in §1).

**Registration**

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.OutputCache;

builder.Services.AddCacheOrchestrator(builder.Configuration);
```

**Config** — domain defaults for all resolved names (or one `Domains` entry per name, e.g. `tenant-acme`):

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "DomainDefaults": {
      "DataCache": { "Enabled": true, "Ttl": "00:05:00" },
      "OutputCache": { "Enabled": true, "Ttl": "00:01:00" },
      "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
    }
  }
}
```

**Code**

```csharp
using CacheOrchestrator.DataCache;
using CacheOrchestrator.OutputCache;

static string CatalogDomain(HttpContext http) =>
    $"tenant-{http.Request.RouteValues["tenant"]}";

app.MapGet("/t/{tenant}/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, CatalogDomain(http), ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain(CatalogDomain);
```

---

## 7. Class library + host

Library package references **`CacheOrchestrator.Core`**. Web host uses **meta** (or AspNetCore + Fusion) like §1.

**Library**

```csharp
using CacheOrchestrator.Orchestration;

public sealed class CatalogService(ICacheOrchestrator cache)
{
    public ValueTask<ProductDto?> GetProductAsync(
        CacheDomainContext cacheDomain,
        string id,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            cacheDomain,
            logicalKey: $"product:{id}",
            async ct => await LoadProductAsync(id, ct),
            cancellationToken);
}
```

**Registration** (web host)

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;

builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddScoped<CatalogService>();
```

**Config** (same shape as §1)

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Enabled": true, "Ttl": "00:05:00" },
        "OutputCache": { "Enabled": true, "Ttl": "00:01:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

**Code** (static domain)

```csharp
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;

var catalogDomain = new CacheDomainContext("catalog");

app.MapGet("/api/products/{id}", async (string id, CatalogService catalog, CancellationToken ct) =>
{
    var data = await catalog.GetProductAsync(catalogDomain, id, ct);
    return Results.Json(data);
})
.CacheOutputWithDomain(catalogDomain.Domain);
```

**Code** (dynamic domain — same library)

```csharp
using CacheOrchestrator.Orchestration;
using CacheOrchestrator.OutputCache;

CacheDomainContext CatalogDomain(HttpContext http) =>
    new($"tenant-{http.Request.RouteValues["tenant"]}");

app.MapGet("/t/{tenant}/products/{id}", async (
    HttpContext http, string id, CatalogService catalog, CancellationToken ct) =>
{
    var data = await catalog.GetProductAsync(CatalogDomain(http), id, ct);
    return Results.Json(data);
})
.CacheOutputWithDomain(http => CatalogDomain(http).Domain);
```

**Worker** (same library; packages **Core** + **FusionCache**)

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Orchestration;

builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
// + host wiring for IOptions<CacheOrchestratorOptions> / ICacheOrchestrator as required
builder.Services.AddScoped<CatalogService>();

var domain = new CacheDomainContext($"tenant-{job.TenantId}");
await catalog.GetProductAsync(domain, job.ProductId, cancellationToken);
```

---

## Config layers (nested)

| JSON section | Portable? | Meaning |
|--------------|-----------|---------|
| `DataCache` | Yes | Enable, instance, TTL, vary / no-store |
| `OutputCache` | AspNet | HTTP response cache |
| `ClientCache` | AspNet | Browser / CDN `Cache-Control` (+ schedule) |
| `FusionCache` | Fusion only | Hard TTL, fail-safe, factory timeouts, … |

Root engines: `OutputCache` + **`DataCacheInstances`**. Default key namespace suffix `{Namespace}-fc` is historical.

---

## Capability note (Fusion vs Hybrid)

| Feature | Fusion | Hybrid |
|---------|--------|--------|
| GetOrCreate + stampede | Yes | Yes |
| Tag invalidation | Yes | Yes (logical) |
| `DataCache.Ttl` | Soft / duration | Expiration |
| Hard TTL / fail-safe / factory timeouts | Yes | No (ignored) |
| Named `DataCacheInstances` | Yes | Single DI HybridCache |
| Redis L2 + backplane | Redis package | Configure Hybrid / `IDistributedCache` separately |

---

## Related

- [Topologies](guide/topologies.md)  
- [Architecture](architecture.md)  
- [Getting started](getting-started.md)  
- [Configuration](configuration.md)  
