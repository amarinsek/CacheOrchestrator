# Package composition

> **How-to.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md). Which NuGet: [Packages guide](../guide/packages.md). First endpoint: [getting-started](../guide/getting-started.md).

Copy-paste wiring for common stacks. Domain rules stay the same; only **packages**, **registration**, and **config** change.

Without `.CacheOutputWithDomain` / `[CacheDomain]`, the base Output Cache policy is `NoCache` — there is no Output Cache entry. EF mapping and SaveChanges rules: [EF Core invalidation](../reference/ef-core-invalidation.md).

Decision tables: [packages](../guide/packages.md) · [topologies](../guide/topologies.md).

## Scenarios

- [§1. Typical web (InMemory Fusion)](#scenario-1)
- [§2. Output Cache only](#scenario-2)
- [§3. Data cache only](#scenario-3)
- [§4. Redis data-cache L2](#scenario-4)
- [§5. HybridCache](#scenario-5)
- [§6. Dynamic domain](#scenario-6)
- [§7. Class library + host](#scenario-7)
- [§8. EF invalidation (web app)](#scenario-8)
- [§9. EF in library + web host](#scenario-9)

---
<a id="scenario-1"></a>
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
        "DataCache": { "Enabled": true, "TtlSeconds": 300 },
        "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

<a id="scenario-2"></a>
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
        "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

<a id="scenario-3"></a>
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
        "DataCache": { "Enabled": true, "TtlSeconds": 300 }
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

<a id="scenario-4"></a>
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
        "DataCache": { "Enabled": true, "TtlSeconds": 300 },
        "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

<a id="scenario-5"></a>
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
        "DataCache": { "Enabled": true, "TtlSeconds": 300 },
        "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

<a id="scenario-6"></a>
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
      "DataCache": { "Enabled": true, "TtlSeconds": 300 },
      "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
      "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

<a id="scenario-7"></a>
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
        "DataCache": { "Enabled": true, "TtlSeconds": 300 },
        "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

<a id="scenario-8"></a>
## 8. EF Core invalidation — all in the web app

Same read path as §1, plus SaveChanges → automatic entity tag purge. Domain and `entityKind` on the GET must match the EF mapping (`[CacheEntity]`, Fluent `CacheInvalidate`, or `Map<T>`). Details: [ef-core-invalidation.md](../reference/ef-core-invalidation.md).

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
        "DataCache": { "Enabled": true, "TtlSeconds": 300 },
        "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

<a id="scenario-9"></a>
## 9. EF Core in a class library + web host

Library owns DbContext usage and cache reads/writes. Host wires CacheOrchestrator, the EF interceptor, and HTTP. Keep the same domain / `entityKind` in mapping, `CacheDomainContext`, and `.CacheOutputWithDomain`.

**Library packages**

```bash
dotnet add package CacheOrchestrator.Core
dotnet add package Microsoft.EntityFrameworkCore
```

**Library**

```csharp
[CacheEntity("catalog", "products")]
public class Product { public int Id { get; set; } public decimal Price { get; set; } }

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
        "DataCache": { "Enabled": true, "TtlSeconds": 300 },
        "OutputCache": { "Enabled": true, "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
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

## Related

- [Packages guide](../guide/packages.md) — which NuGet to install  
- [Topologies](../guide/topologies.md)  
- [Configuration](../reference/configuration.md)  
- [Data cache](../reference/data-cache.md)  
- [EF Core invalidation](../reference/ef-core-invalidation.md)  
