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
| **1** | Web | Fusion (InMemory) | yes | Meta |
| **2** | Web | off | yes | Meta / AspNetCore |
| **3** | Web | Fusion | no | Meta |
| **4** | Web | Fusion (Redis L2) | yes (InMemory or Redis) | Meta + Redis |
| **5** | Web | Hybrid | yes | AspNetCore + HybridCache |
| **6** | Web | Fusion | yes (dynamic domain) | Meta |
| **7** | Library + web / worker | Fusion | yes / n/a | Core in library; Meta (or Fusion) in host |

Each scenario below uses the **same product endpoint shape** where possible. Differences are in **registration** and **config**. Base Output Cache policy is `NoCache` — without `.CacheOutputWithDomain` there is no OC entry.

---

## 1. Typical web — OC + data cache + client headers (InMemory Fusion)

**Registration**

```csharp
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
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

---

## 2. Output Cache only (data cache disabled for the domain)

**Registration**

```csharp
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
        "DataCache": { "Enabled": false },
        "OutputCache": { "Enabled": true, "Ttl": "00:01:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

**Code** (same as §1 — `GetOrSetAsync` runs the factory uncached when data cache is off)

```csharp
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Alternatively omit `IDomainDataCache` and call `LoadProductAsync` directly in the handler; Output Cache still applies via `.CacheOutputWithDomain`.

---

## 3. Data cache only (no Output Cache on the route)

**Registration**

```csharp
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
        "OutputCache": { "Enabled": false },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

**Code** — same handler; **no** `.CacheOutputWithDomain` (base policy is `NoCache`). Pass the domain into `GetOrSetAsync` because endpoint metadata does not set it:

```csharp
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, "catalog", ct => LoadProductAsync(id, ct));
    return Results.Json(data);
});
```

Client `Cache-Control` from the domain is applied by the Output Cache domain policy; without `.CacheOutputWithDomain` those headers are not written by that policy.

---

## 4. Redis data-cache L2 (Fusion) + InMemory Output Cache

**Registration**

```csharp
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

**Registration**

```csharp
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
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Fusion-only domain knobs (`FusionCache` hard TTL / fail-safe / …) are ignored.

---

## 6. Dynamic domain from the route

**Registration**

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
```

**Config** — either domain defaults for all tenants, or one entry per resolved name (e.g. `tenant-acme`). Example with defaults:

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

**Code** — same `GetOrSetAsync` + `.CacheOutputWithDomain`, shared resolver:

```csharp
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

Http-free library takes a host-supplied domain binding on each call. Host registration/config match §1 (or §4 / §5). Endpoint code stays close to §1; the library owns the load logic.

**Library** (references Core only)

```csharp
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

**Worker** (same library, no Output Cache)

```csharp
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
// + Core orchestrator / options registration as used by your host
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
