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
| **8** | Web + EF invalidation | Fusion | yes | Meta + EFCore.Invalidation |
| **9** | Library (EF) + web host | Fusion | yes | Core (+ EF) in library; Meta + EFCore.Invalidation in host |

Each scenario below uses the **same product endpoint shape** where possible. Differences are in **packages**, **registration**, and **config**. Base Output Cache policy is `NoCache` — without `.CacheOutputWithDomain` there is no OC entry. EF mapping and SaveChanges behaviour: [ef-core-invalidation.md](ef-core-invalidation.md).

---

## 1. Typical web — OC + data cache + client headers (InMemory Fusion)

Uses the **meta** package `CacheOrchestrator` (`AddCacheOrchestrator` = AspNetCore + Fusion). You can instead install the two packages separately:

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.FusionCache
```

and call `AddCacheOrchestratorAspNetCore` then `AddCacheOrchestratorFusionCache`.

Per-domain `OutputCache:Enabled` / `DataCache:Enabled` turn layers on or off in config without changing the endpoint code (see also §2 / §3 when you want fewer packages).

**Packages**

```bash
dotnet add package CacheOrchestrator
```

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

## 2. Output Cache only (AspNetCore package)

No Fusion / Hybrid. Handler does not use `IDomainDataCache`.

**Packages**

```bash
dotnet add package CacheOrchestrator.AspNetCore
```

**Registration**

```csharp
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
app.MapGet("/api/products/{id}", async (string id) =>
{
    var data = await LoadProductAsync(id);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

---

## 3. Data cache only (AspNetCore + FusionCache)

Not the meta package. No `.CacheOutputWithDomain` — base Output Cache policy is `NoCache`.

**Packages**

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.FusionCache
```

**Registration**

```csharp
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
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, "catalog", ct => LoadProductAsync(id, ct));
    return Results.Json(data);
});
```

---

## 4. Redis data-cache L2 (Fusion) + InMemory Output Cache

**Packages**

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.Redis
```

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

For Redis as the Output Cache store as well, set `"OutputCache": { "Provider": "Redis" }` (same packages and registration).

---

## 5. HybridCache data provider

**Packages**

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.HybridCache
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

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

---

## 6. Dynamic domain from the route

Same packages as §1 (meta, or AspNetCore + Fusion separately).

**Packages**

```bash
dotnet add package CacheOrchestrator
```

**Registration**

```csharp
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

**Library packages**

```bash
dotnet add package CacheOrchestrator.Core
```

**Library**

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

**Host packages** (web — same as §1)

```bash
dotnet add package CacheOrchestrator
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

**Worker** (same library)

```bash
dotnet add package CacheOrchestrator.Core
dotnet add package CacheOrchestrator.FusionCache
```

```csharp
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
// + host wiring for options / ICacheOrchestrator as required
builder.Services.AddScoped<CatalogService>();

var domain = new CacheDomainContext($"tenant-{job.TenantId}");
await catalog.GetProductAsync(domain, job.ProductId, cancellationToken);
```

---

## 8. EF Core invalidation — all in the web app

Same read path as §1, plus SaveChanges → automatic entity tag purge. Domain and `entityKind` on the GET must match the EF mapping (`[CacheEntity]`, Fluent `CacheInvalidate`, or `Map<T>`). Details: [ef-core-invalidation.md](ef-core-invalidation.md).

**Packages**

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

**Registration**

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(connectionString);
    opt.AddCacheOrchestratorInvalidation(sp);
});
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

**Code**

```csharp
[CacheEntity("catalog", "products")]
public class Product { public int Id { get; set; } public decimal Price { get; set; } }

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache, AppDbContext db) =>
{
    var data = await cache.GetOrSetEntityAsync(http, async ct =>
    {
        Product? p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id.ToString() == id, ct);
        return p is null ? null : new ProductDto(p.Id, p.Price);
    });
    return data is null ? Results.NotFound() : Results.Json(data);
})
.CacheOutputWithDomain("catalog", resourceRouteKey: "id", entityKind: "products");

app.MapPut("/api/products/{id}", async (string id, UpdatePriceBody body, AppDbContext db, CancellationToken ct) =>
{
    Product product = await db.Products.SingleAsync(x => x.Id.ToString() == id, ct);
    product.Price = body.Price;
    await db.SaveChangesAsync(ct); // interceptor invalidates entity tags — no manual Invalidate*
    return Results.NoContent();
});
```

---

## 9. EF Core in a class library + web host

Library owns DbContext usage and cache reads/writes. Host wires CacheOrchestrator, the EF interceptor, and HTTP. Keep the same domain / `entityKind` in mapping, `CacheDomainContext`, and `.CacheOutputWithDomain`.

**Library packages**

```bash
dotnet add package CacheOrchestrator.Core
dotnet add package Microsoft.EntityFrameworkCore
```

**Library**

```csharp
public sealed class CatalogService(ICacheOrchestrator cache, AppDbContext db)
{
    public ValueTask<ProductDto?> GetProductAsync(
        CacheDomainContext cacheDomain,
        string id,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateEntityAsync(
            cacheDomain,
            logicalKey: $"product:{id}",
            resourceId: id,
            async ct =>
            {
                Product? p = await db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id.ToString() == id, ct);
                return p is null ? null : new ProductDto(p.Id, p.Price);
            },
            defaultEntityKind: "products",
            cancellationToken);

    public async Task UpdatePriceAsync(string id, decimal price, CancellationToken cancellationToken)
    {
        Product product = await db.Products.SingleAsync(x => x.Id.ToString() == id, cancellationToken);
        product.Price = price;
        await db.SaveChangesAsync(cancellationToken);
    }
}
```

Map the entity in the library model (or let the host call `Map<Product>`):

```csharp
modelBuilder.Entity<Product>().CacheInvalidate("catalog", "products");
```

**Host packages**

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

**Registration**

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(connectionString);
    opt.AddCacheOrchestratorInvalidation(sp);
});
builder.Services.AddScoped<CatalogService>();
```

**Config** (same shape as §1 / §8)

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
var catalogDomain = new CacheDomainContext("catalog", entityKind: "products");

app.MapGet("/api/products/{id}", async (string id, CatalogService catalog, CancellationToken ct) =>
{
    var data = await catalog.GetProductAsync(catalogDomain, id, ct);
    return data is null ? Results.NotFound() : Results.Json(data);
})
.CacheOutputWithDomain(catalogDomain.Domain, resourceRouteKey: "id", entityKind: "products");

app.MapPut("/api/products/{id}", async (string id, UpdatePriceBody body, CatalogService catalog, CancellationToken ct) =>
{
    await catalog.UpdatePriceAsync(id, body.Price, ct);
    return Results.NoContent();
});
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
- [EF Core invalidation](ef-core-invalidation.md) — mapping, SaveChanges rules, options  

