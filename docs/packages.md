# Packages and composition

> **Reference.** Product overview: [root README](../README.md). Catalog: [documentation index](README.md). Quick path: [getting-started](getting-started.md).

CacheOrchestrator is split so **policy and orchestration** live in Core, while **engines and HTTP** are optional packages. The application chooses topology with NuGet references and DI — domain rules and call sites stay stable.

Dependency rule: arrows point at **Core**. Core never references ASP.NET, FusionCache, HybridCache, Redis, HttpBus, or EF.

---

## Package map

| Package | Role |
|---------|------|
| [**CacheOrchestrator.Core**](../src/CacheOrchestrator.Core/README.md) | Domains, Version, portable `DataCache` policy, entity footprint/tags, `ICacheOrchestrator`, `CacheDomainContext`, invalidation and cluster **contracts** |
| [**CacheOrchestrator.FusionCache**](../src/CacheOrchestrator.FusionCache/README.md) | ZiggyCreatures FusionCache as `IDataCacheProvider`; owns JSON `FusionCache` knobs (hard TTL, fail-safe, factory timeouts, …) |
| [**CacheOrchestrator.HybridCache**](../src/CacheOrchestrator.HybridCache/README.md) | Microsoft HybridCache as `IDataCacheProvider` (portable `DataCache.Ttl` only; no fail-safe) |
| [**CacheOrchestrator.AspNetCore**](../src/CacheOrchestrator.AspNetCore/README.md) | Output Cache, Client Cache-Control, HTTP `IDomainDataCache`, Admin API, `AddCacheOrchestratorAspNetCore` |
| [**CacheOrchestrator**](../src/CacheOrchestrator/README.md) (meta) | Convenience: AspNetCore + FusionCache; `AddCacheOrchestrator` wires both |
| [**CacheOrchestrator.Redis**](../src/CacheOrchestrator.Redis/README.md) | Redis Output Cache store and Fusion L2 / backplane |
| [**CacheOrchestrator.HttpBus**](../src/CacheOrchestrator.HttpBus/README.md) | HTTP cluster command bus (invalidate, Version, settings) |
| [**CacheOrchestrator.EFCore.Invalidation**](../src/CacheOrchestrator.EFCore.Invalidation/README.md) | After `SaveChanges`, purge via the invalidator |

**Admin Console App** (`src/CacheOrchestrator.AdminConsole`) is a separate host, not a NuGet package. [admin.md](admin.md) · [deploy/admin](../deploy/admin/README.md).

| API | Package | Role |
|-----|---------|------|
| `ICacheOrchestrator` | Core | Http-free data get-or-create |
| `CacheDomainContext` | Core | Host-supplied domain (+ optional entity kind) for libraries |
| `IDomainDataCache` | AspNetCore | HTTP projection over `ICacheOrchestrator` |

---

## Use-case matrix

| # | Host | Data provider | Output Cache | Client Cache | Typical packages |
|---|------|---------------|--------------|--------------|------------------|
| **A** | Worker | Fusion | — | — | Core + FusionCache |
| **B** | Worker | Hybrid | — | — | Core + HybridCache |
| **C** | Web | Fusion | yes | yes | **Meta** *or* AspNetCore + FusionCache (+ Redis optional) |
| **D** | Web | Hybrid | yes | yes | AspNetCore + HybridCache |
| **E** | Web | unused | yes | yes | AspNetCore / meta — OC/CC only |
| **F** | Web + EF | Fusion | yes | yes | **C** + EFCore.Invalidation |

**Registration**

- Meta: `AddCacheOrchestrator(configuration)` = AspNetCore + Fusion.
- Hybrid web: `AddHybridCache()` → `AddCacheOrchestratorAspNetCore` → `AddCacheOrchestratorHybridCache()`.
- Redis: `AddCacheOrchestrator(..., o => o.AddRedisBackend())` (after Redis package).

Shared nested config shape (adapt providers / enable flags per scenario):

```json
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
```

---

## Endpoint composition examples

Assume `AddCacheOrchestrator` (or AspNetCore + a data provider) is already called. Examples use Minimal APIs; controllers use `[CacheDomain("catalog")]` the same way.

### 1. Output Cache + data cache + client headers (typical web)

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Domain on the endpoint drives Output Cache, Client Cache-Control, and (via `IDomainDataCache`) data cache.

### 2. Output Cache only (no data get-or-set)

```csharp
app.MapGet("/api/about", () => Results.Content("…", "text/html"))
.CacheOutputWithDomain("static-pages");
```

Or keep data registration but set `"DataCache": { "Enabled": false }` for that domain. Handler does not call `IDomainDataCache` / `ICacheOrchestrator`.

### 3. Data cache only (no Output Cache on the route)

```csharp
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, "catalog", ct => LoadProductAsync(id, ct));
    return Results.Json(data);
});
// no .CacheOutputWithDomain — base Output Cache policy is NoCache (no OC entry)
```

Pass the domain name into `GetOrSetAsync` when there is no endpoint domain metadata. Without a domain Output Cache policy, client `Cache-Control` from the domain is also not applied on that route.

### 4. Redis as data-cache L2 (and optional OC store)

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());
```

```json
"OutputCache": { "Provider": "InMemory" },
"DataCacheInstances": { "default": { "Provider": "Redis" } },
"Redis": { "Configuration": "localhost:6379" }
```

Endpoint code stays like example 1. Swap `"OutputCache": { "Provider": "Redis" }` when full HTTP responses should also live in Redis.

### 5. HybridCache instead of Fusion

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache();
```

Endpoint code unchanged (still `IDomainDataCache` + `.CacheOutputWithDomain`). Fusion-only JSON (`FusionCache` hard TTL / fail-safe) is ignored.

### 6. Dynamic domain from the route

```csharp
static string CatalogDomain(HttpContext http) =>
    $"tenant-{http.Request.RouteValues["tenant"]}";

app.MapGet("/t/{tenant}/products", async (HttpContext http, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, CatalogDomain(http), LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain(CatalogDomain);
```

Use matching entries under `Cache:Domains` (or rely on domain defaults). Prefer one shared resolver so Output Cache and data cache never diverge.

### 7. Worker / no HTTP (data cache only)

No Minimal API. Register Core + Fusion (or Hybrid) and call `ICacheOrchestrator` with an explicit `CacheDomainContext` (see next section). Output/Client settings in config do not apply without an ASP.NET pipeline.

---

## `CacheDomainContext` (libraries)

Http-free libraries should not hard-code domain names and should not take `HttpContext`. By convention they accept a **`CacheDomainContext`** from the host:

```csharp
public sealed class CacheDomainContext
{
    public CacheDomainContext(string domain, string? entityKind = null);
    public string Domain { get; }
    public string? EntityKind { get; }
    public string EntityKindOr(string defaultEntityKind);
}
```

Optional entity kind supports entity APIs; resource ids stay method parameters. Core also provides `ICacheOrchestrator` extension overloads that take `CacheDomainContext`.

### Library + endpoint (static domain)

**Library** (Core only):

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

**Host** — build one context; reuse for Output Cache and the library:

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddScoped<CatalogService>();

var catalogDomain = new CacheDomainContext("catalog");

app.MapGet("/api/products/{id}", async (
    string id,
    CatalogService catalog,
    CancellationToken ct) =>
    Results.Json(await catalog.GetProductAsync(catalogDomain, id, ct)))
.CacheOutputWithDomain(catalogDomain.Domain);
```

**Configuration** — policy under that domain name (`DataCache` / `OutputCache` / `ClientCache` as needed).

### Library + endpoint (dynamic domain)

```csharp
CacheDomainContext CatalogDomain(HttpContext http) =>
    new($"tenant-{http.Request.RouteValues["tenant"]}");

app.MapGet("/t/{tenant}/products/{id}", async (
    HttpContext http,
    string id,
    CatalogService catalog,
    CancellationToken ct) =>
{
    CacheDomainContext domain = CatalogDomain(http);
    return Results.Json(await catalog.GetProductAsync(domain, id, ct));
})
.CacheOutputWithDomain(http => CatalogDomain(http).Domain);
```

### Worker using the same library

```csharp
var domain = new CacheDomainContext($"tenant-{job.TenantId}");
await catalog.GetProductAsync(domain, job.ProductId, cancellationToken);
```

No `.CacheOutputWithDomain`. Same `CatalogService` API.

---

## Config layers (nested)

| JSON section | Portable? | Meaning |
|--------------|-----------|---------|
| `DataCache` | Yes | Enable, instance, TTL, vary / no-store |
| `OutputCache` | AspNet | HTTP response cache |
| `ClientCache` | AspNet | Browser / CDN `Cache-Control` (+ schedule) |
| `FusionCache` | Fusion only | Hard TTL, fail-safe, factory timeouts, … |

Root engines: `OutputCache` + **`DataCacheInstances`**. Default key namespace suffix `{Namespace}-fc` is historical (override per instance if needed).

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

- [Topologies](guide/topologies.md) — InMemory / Redis / bus layouts  
- [Architecture](architecture.md) — request flow and project layout  
- [Getting started](getting-started.md) — first web endpoint  
- [Configuration](configuration.md) — full schema  
