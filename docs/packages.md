# Packages and composition

> **Reference.** Product overview: [root README](../README.md). Catalog: [documentation index](README.md). Quick path: [getting-started](getting-started.md).

CacheOrchestrator is split so **policy and orchestration** live in Core, while **engines and HTTP** are optional packages. The application chooses topology with NuGet references and DI — domain rules and the library call site stay the same.

Dependency rule: arrows point at **Core**. Core never references ASP.NET, FusionCache, HybridCache, Redis, HttpBus, or EF.

---

## Package map

| Package | Role |
|---------|------|
| [**CacheOrchestrator.Core**](../src/CacheOrchestrator.Core/README.md) | Domains, Version, portable `DataCache` policy, entity footprint/tags, `ICacheOrchestrator`, invalidation and cluster **contracts** |
| [**CacheOrchestrator.FusionCache**](../src/CacheOrchestrator.FusionCache/README.md) | ZiggyCreatures FusionCache as `IDataCacheProvider`; owns JSON `FusionCache` knobs (hard TTL, fail-safe, factory timeouts, …) |
| [**CacheOrchestrator.HybridCache**](../src/CacheOrchestrator.HybridCache/README.md) | Microsoft HybridCache as `IDataCacheProvider` (portable `DataCache.Ttl` only; no fail-safe) |
| [**CacheOrchestrator.AspNetCore**](../src/CacheOrchestrator.AspNetCore/README.md) | Output Cache, Client Cache-Control, HTTP `IDomainDataCache`, Admin API, `AddCacheOrchestratorAspNetCore` |
| [**CacheOrchestrator**](../src/CacheOrchestrator/README.md) (meta) | Convenience: AspNetCore + FusionCache; `AddCacheOrchestrator` wires both |
| [**CacheOrchestrator.Redis**](../src/CacheOrchestrator.Redis/README.md) | Redis Output Cache store and Fusion L2 / backplane |
| [**CacheOrchestrator.HttpBus**](../src/CacheOrchestrator.HttpBus/README.md) | HTTP cluster command bus (invalidate, Version, settings) |
| [**CacheOrchestrator.EFCore.Invalidation**](../src/CacheOrchestrator.EFCore.Invalidation/README.md) | After `SaveChanges`, purge via the invalidator |

**Admin Console App** (`src/CacheOrchestrator.AdminConsole`) is a separate host, not a NuGet package. [admin.md](admin.md) · [deploy/admin](../deploy/admin/README.md).

Libraries should take **`ICacheOrchestrator`** (and/or `ICacheOrchestratorInvalidator`) from Core (including entity/footprint helpers). Web endpoints often use AspNetCore’s **`IDomainDataCache`** + `.CacheOutputWithDomain` / `[CacheDomain]` — a thin HTTP projection over the same orchestrator (no Ziggy dependency in AspNetCore).

---

## `ICacheOrchestrator` vs `IDomainDataCache`

| | `ICacheOrchestrator` | `IDomainDataCache` |
|--|----------------------|--------------------|
| Package | Core | AspNetCore |
| Typical caller | Class libraries, workers | Minimal APIs / controllers |
| Input | `CacheEntryRequest` or domain + key / entity args — **no** `HttpContext` | `HttpContext` (domain and entity identity from the request) |
| Role | Domain policy, Version keying, tags, get-or-create via `IDataCacheProvider` | Resolve domain / vary key / auth from HTTP, then call `ICacheOrchestrator` |

`IDomainDataCache` is not a second cache. Output Cache and Client Cache-Control are separate layers applied by the ASP.NET host; they are not invoked through either interface.

### Who chooses the domain name

`.CacheOutputWithDomain(...)` / `[CacheDomain]` attach a domain to an **HTTP endpoint**. `ICacheOrchestrator` has no ambient request: the library must pass a domain string into each call. The library author typically **does not** hard-code that name — the **host** supplies it (environment / product mapping). Keep the library Http-free: inject options, not `HttpContext`.

`Configure<TOptions>` is the usual .NET Options pattern. A library can wrap it in one extension for a shorter host line, for example `AddCatalogCache(o => o.Domain = "catalog")` that calls `Configure` + `AddScoped<CatalogService>` internally. Binding from configuration (`Configure<CatalogCacheOptions>(config.GetSection("CatalogCache"))`) is equivalent.

### Library + web host (shared domain config)

One domain block under `Cache:Domains` can hold **DataCache**, **OutputCache**, and **ClientCache**. The library uses the data-cache policy (and Version). The host applies Output Cache and client headers for the **same** domain name around the library call.

**Library** (references Core only — no domain literal):

```csharp
public sealed class CatalogCacheOptions
{
    /// <summary>Maps to Cache:Domains:{Domain}. Required — set by the host.</summary>
    public string Domain { get; set; } = "";
}

public sealed class CatalogService(
    ICacheOrchestrator cache,
    IOptions<CatalogCacheOptions> options)
{
    public ValueTask<ProductDto?> GetProductAsync(string id, CancellationToken cancellationToken)
    {
        string domain = options.Value.Domain;
        if (string.IsNullOrWhiteSpace(domain))
            throw new InvalidOperationException("CatalogCacheOptions.Domain must be set by the host.");

        return cache.GetOrCreateAsync(
            new CacheEntryRequest { Domain = domain, Key = $"product:{id}" },
            async ct => await LoadProductAsync(id, ct),
            cancellationToken);
    }
}
```

**Host** — one place decides the name; Options and Output Cache both use it:

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);

const string catalogDomain = "catalog";
builder.Services.Configure<CatalogCacheOptions>(o => o.Domain = catalogDomain);
builder.Services.AddScoped<CatalogService>();

app.MapGet("/api/products/{id}", async (string id, CatalogService catalog, CancellationToken ct) =>
    Results.Json(await catalog.GetProductAsync(id, ct)))
.CacheOutputWithDomain(catalogDomain);
```

**Configuration** (policy for that domain name):

```json
"Domains": {
  "catalog": {
    "Version": "1",
    "DataCache": { "Ttl": "00:05:00" },
    "OutputCache": { "Ttl": "00:01:00" },
    "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
  }
}
```

With several modules, register several options types (each with its own `Domain`). Multiple domains in `Cache:Domains` are independent policies; each library options instance points at one of them.

Request path: client headers → Output Cache → handler → `CatalogService` → `ICacheOrchestrator` → Fusion or Hybrid. Layer TTLs may differ; coordination is the shared domain name and Version (and tag invalidation). A worker host sets the same `CatalogCacheOptions.Domain` and skips `.CacheOutputWithDomain` — Output/Client sections simply do not apply in that process.

---

## Use-case matrix

Same domain policy idea in every row. What changes is **which packages you install** and **how the host registers** them.

| # | Host | Data provider | Output Cache | Client Cache | Typical packages |
|---|------|---------------|--------------|--------------|------------------|
| **A** | Worker / class library | Fusion | — | — | Core + FusionCache (+ host registration) |
| **B** | Worker / class library | Hybrid | — | — | Core + HybridCache (+ host registration) |
| **C** | Web | Fusion | yes | yes | **Meta** *or* AspNetCore + FusionCache (+ Redis optional) |
| **D** | Web | Hybrid | yes | yes | AspNetCore + HybridCache |
| **E** | Web | unused | yes | yes | AspNetCore / meta — call OC/CC only; skip data get-or-set |
| **F** | Web + EF | Fusion | yes | yes | **C** + EFCore.Invalidation |

**Host registration:** meta package `AddCacheOrchestrator` = `AddCacheOrchestratorAspNetCore` + `AddCacheOrchestratorFusionCache` (binds `Cache`, Output Cache, named Fusion instances, `ICacheOrchestrator`, `IDomainDataCache`). AspNetCore alone does **not** register a data engine — call Fusion or Hybrid explicitly. For Hybrid: `AddHybridCache()` → `AddCacheOrchestratorAspNetCore` → `AddCacheOrchestratorHybridCache()`. Class libraries still depend only on Core contracts so the **call site below does not change**.

---

## Invariant call site (rows A–D)

Library code that loads domain data (domain name from host options — see above):

```csharp
cache.GetOrCreateAsync(
    new CacheEntryRequest { Domain = options.Value.Domain, Key = $"product:{id}" },
    async ct => await LoadProductAsync(id, ct),
    cancellationToken);
```

This call is the same for Fusion and Hybrid. The registered `IDataCacheProvider` is what differs.

### Registration diffs

**C — Web + Fusion (typical, meta package):**

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
// optional: o => o.AddRedisBackend()
// Equivalent without meta:
//   AddCacheOrchestratorAspNetCore(...) + AddCacheOrchestratorFusionCache(configuration)
```

**D — Web + Hybrid:**

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache(); // replaces IDataCacheProvider
```

**F — add EF invalidation** on top of C:

```csharp
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);
// + interceptor / DbContext registration — see ef-core-invalidation.md
```

**A / B — worker-style:** still compose Core contracts + a data provider; most hosts reuse `AddCacheOrchestrator` (AspNetCore) or wire the same Core services in custom DI. Prefer depending on `ICacheOrchestrator`, not on Fusion or Hybrid types.

---

## Web happy path (rows C / D / F)

When AspNetCore is present, endpoints usually attach the domain once and use the HTTP helper:

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

That path still resolves the same domain options and goes through the orchestrator / data provider. Controllers use `[CacheDomain("catalog")]` the same way.

Row **E** (Output + Client only): keep `.CacheOutputWithDomain` / `[CacheDomain]` and do **not** call data get-or-set (or set `DataCache:Enabled` false for that domain).

---

## Config layers (nested)

Under each domain (and `DomainDefaults`):

| JSON section | Portable? | Meaning |
|--------------|-----------|---------|
| `DataCache` | Yes | Enable, instance name, TTL, vary / no-store — Fusion **or** Hybrid |
| `OutputCache` | AspNet | HTTP response cache |
| `ClientCache` | AspNet | Browser / CDN `Cache-Control` (+ schedule) |
| `FusionCache` | Fusion package only | Hard TTL, fail-safe, factory timeouts, jitter, … |

Root providers: `OutputCache` + **`DataCacheInstances`** (named engines; default instance is typically InMemory or Redis via the Redis package).

Default key namespace for a data-cache instance remains `{Namespace}-fc` / `{Namespace}-fc-{name}` when unset (historical suffix; override with per-instance `Namespace` if you prefer).

---

## Capability note (Fusion vs Hybrid)

| Feature | Fusion provider | Hybrid provider |
|---------|-----------------|-----------------|
| GetOrCreate + stampede | Yes | Yes |
| Tag invalidation | Yes | Yes (logical) |
| `DataCache.Ttl` | Soft / duration | Expiration |
| Hard TTL / fail-safe / eager / factory timeouts | Yes (`FusionCache` section) | No (ignored) |
| Named `DataCacheInstances` | Yes | Single DI HybridCache |
| Redis L2 + backplane | Yes (Redis package) | Configure Hybrid / `IDistributedCache` separately |

Admin runtime overlays for `fusionCache.*` require the Fusion package contributor. Details: [fusion-cache.md](fusion-cache.md), [HybridCache README](../src/CacheOrchestrator.HybridCache/README.md), [configuration.md](configuration.md).

---

## Related

- [Topologies](guide/topologies.md) — InMemory / Redis / bus layouts  
- [Architecture](architecture.md) — request flow and project layout  
- [Getting started](getting-started.md) — first web endpoint  
