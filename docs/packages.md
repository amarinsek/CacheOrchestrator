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
| [**CacheOrchestrator.AspNetCore**](../src/CacheOrchestrator.AspNetCore/README.md) | Output Cache, Client Cache-Control, HTTP domain helpers, Local Admin API, host `AddCacheOrchestrator` |
| [**CacheOrchestrator**](../src/CacheOrchestrator/README.md) (meta) | Convenience: AspNetCore + FusionCache for typical web apps |
| [**CacheOrchestrator.Redis**](../src/CacheOrchestrator.Redis/README.md) | Redis Output Cache store and Fusion L2 / backplane |
| [**CacheOrchestrator.HttpBus**](../src/CacheOrchestrator.HttpBus/README.md) | HTTP cluster command bus (invalidate, Version, settings) |
| [**CacheOrchestrator.EFCore.Invalidation**](../src/CacheOrchestrator.EFCore.Invalidation/README.md) | After `SaveChanges`, purge via the invalidator |

**Admin Console App** (`src/CacheOrchestrator.AdminConsole`) is a separate host, not a NuGet package. [admin.md](admin.md) · [deploy/admin](../deploy/admin/README.md).

Libraries should take **`ICacheOrchestrator`** (and/or `ICacheOrchestratorInvalidator`) from Core. Web endpoints often use AspNetCore’s **`IDomainFusionCache`** + `.CacheOutputWithDomain` / `[CacheDomain]` — that is the HTTP projection over the same orchestrator.

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

**Host registration today:** turnkey entry is AspNetCore’s `AddCacheOrchestrator` (also what the meta package exposes). It binds `Cache`, Output Cache, named `DataCacheInstances`, `ICacheOrchestrator`, and Fusion as the default data provider. Call `AddCacheOrchestratorHybridCache()` afterward to replace the provider with Hybrid (and call Microsoft `AddHybridCache()` first). Class libraries still depend only on Core contracts so the **call site below does not change**.

---

## Invariant call site (rows A–D)

Application or library code that loads domain data:

```csharp
public sealed class CatalogService(ICacheOrchestrator cache)
{
    public ValueTask<CatalogDto?> GetAsync(CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            new CacheEntryRequest
            {
                Domain = "catalog",
                Key = "all",
            },
            async ct => await LoadCatalogAsync(ct),
            cancellationToken);
}
```

This is the same for Fusion and Hybrid. The registered `IDataCacheProvider` is what differs.

### Registration diffs

**C — Web + Fusion (typical):**

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
// optional: o => o.AddRedisBackend()
```

**D — Web + Hybrid:**

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestrator(builder.Configuration);
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
app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
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
