# Data cache (`IDataCacheProvider`)

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — concepts](guide/concepts.md). Catalog: [documentation index](README.md). Packages: [packages.md](packages.md).

The **data cache** (DC) stores **application objects** from your factory (DTOs, tiles, aggregates) — not full HTTP responses. CacheOrchestrator scopes it to the same **domain** as Output Cache and client headers. It does not own a store: a registered **`IDataCacheProvider`** does.

Portable domain policy lives under nested **`DataCache`** (`TtlSeconds`, `Enabled`, `Instance`, vary / no-store flags). HTTP apps typically call **`IDomainDataCache`**; libraries / workers use Core **`ICacheOrchestrator`** (+ optional `CacheDomainContext`). Composition: [packages.md](packages.md).

## Providers

| Package | Engine | Config |
|---------|--------|--------|
| **CacheOrchestrator.FusionCache** | ZiggyCreatures FusionCache (default in the meta package) | `DataCache.*` + nested **`FusionCache.*`** (hard TTL, fail-safe, jitter, factory timeouts, …) |
| **CacheOrchestrator.HybridCache** | Microsoft HybridCache | `DataCache.TtlSeconds` only — ignores `FusionCache` |

Register exactly one provider. Meta `AddCacheOrchestrator` = AspNetCore + Fusion. Hybrid: `AddHybridCache()` then `AddCacheOrchestratorAspNetCore` + `AddCacheOrchestratorHybridCache` (replaces any prior `IDataCacheProvider`). Package READMEs: [FusionCache](../src/CacheOrchestrator.FusionCache/README.md), [HybridCache](../src/CacheOrchestrator.HybridCache/README.md).

Fusion/Hybrid capabilities below. Domain resolution, keys, entity identity, and `dc=` results are **shared**.

---

## How the data cache finds the domain

`IDomainDataCache.GetOrSetAsync` looks for domain options in this order:

1. The overload `GetOrSetAsync(http, domain, factory)` — same domain reuses the request snapshot; a **different** name replaces it (so `products` and `catalog` never share an entry).
2. Already on the request — usually set by Output Cache when you use `.CacheOutputWithDomain` or `[CacheDomain]`.
3. Endpoint metadata (the same attribute or extension), then the options are loaded.
4. If none of those apply, the factory runs uncached.

### With Output Cache

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("products");
```

```csharp
[CacheDomain("products")]
public class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromServices] IDomainDataCache cache,
        CancellationToken cancellationToken)
    {
        var data = await cache.GetOrSetAsync(HttpContext, LoadAsync, cancellationToken);
        return Ok(data);
    }
}
```

### Data cache only (no Output Cache domain)

When the endpoint has no Output Cache domain, pass the name:

```csharp
await cache.GetOrSetAsync(http, "products", factory, cancellationToken);
```

Equivalent:

```csharp
domains.EnsureDomainOptions(http, "products");
await cache.GetOrSetAsync(http, factory, cancellationToken);
```

If you omit the domain:

- a Warning is logged (`Data cache skipped: no domain resolved…`)
- metric `cache_orchestrator.dc.requests` is recorded with `domain=_`, `result=unresolved`
- `X-Cache` may show `dc=unresolved; fa=run` when Output Cache still writes headers

### Entity identity

Entity identity is optional and lives **inside** a domain (domains stay the configuration unit). Endpoint metadata owns domain + primary kind/id; the data-cache HTTP helpers consume that identity on the happy path.

```csharp
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache, CancellationToken cancellationToken) =>
{
    var product = await cache.GetOrSetEntityAsync(
        http,
        ct => LoadProductAsync(id, ct),
        cancellationToken);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");

await invalidator.InvalidateEntityAsync("store", "products", productId, cancellationToken);
```

Tags for that detail entry: `domain:store`, `entity:store:products:42`, `entitykind:store:products`.

The same footprint model also covers lists, references, aggregates, nested collections, batch ids, aliases, derived data, and composites — via `EntityCache` / `EntitySet` (`Members`, `DependsOn`, `Alias`, `Miss`) and `GetOrSetEntitySetAsync`. Cookbook with use cases: **[entity-footprint.md](entity-footprint.md)**.

Also: [domain-profiles.md](domain-profiles.md), [invalidation.md](invalidation.md), [cache-keys.md](cache-keys.md).

#### Migration (obsolete overloads)

`GetOrSetEntityAsync(http, entityKind, resourceId, …)` and `GetOrSetEntityAsync(http, domain, entityKind, resourceId, …)` are obsolete. Prefer endpoint identity or `SetEntityIdentity`. They remain as thin wrappers until the next major.

## When the factory runs uncached

- No domain on the request or metadata — disposition `Unresolved` (Warning + metric `result=unresolved`).
- `DataCache.Enabled: false` — `Off`.
- Request `no-store` and `DataCache.RespectNoStore` — `Bypass`.
- Auth bypass would fire **and** `DataCacheRespectAuthBypass` is `true` (the default) — `Bypass` (Debug: data cache skipped due to auth bypass). Set `DataCacheRespectAuthBypass: false` for 2.1-like data-cache-under-Authorization.

## Cache key

`DefaultDomainKeyGenerator` builds a deterministic key (XxHash3).

With entity identity (`GetOrSetEntityAsync`):

```text
{domain}:{versionHex}:id:{entityKind}:{resourceId}:{hash}
```

Without a resource id (URL-shaped):

1. Route pattern and route values (or path)
2. Query string (tracking parameters omitted: `utm_*`, `gclid`, …)
3. `Accept-Encoding` if `DataCache.VaryOnEncoding`
4. Scheme and host if `DataCache.VaryOnPublicAddress`

The string includes **domain** and **Version**. Every entry is tagged `domain:{name}`.

Details: [cache-keys.md](cache-keys.md).

### Custom key generator

Implement `IDomainKeyGenerator` when you must vary on something the default ignores (tenant claim, extra header). Register it before `AddCacheOrchestrator` (`TryAddSingleton` will keep yours), or `Replace` it afterwards.

```csharp
using CacheOrchestrator.Vary;

services.AddSingleton<IDomainKeyGenerator, TenantKeyGenerator>();
services.AddCacheOrchestrator(configuration);
```

```csharp
using CacheOrchestrator.Vary;

public sealed class TenantKeyGenerator : IDomainKeyGenerator
{
    private readonly DefaultDomainKeyGenerator _inner;

    public TenantKeyGenerator(CacheVaryMaterializer materializer)
        => _inner = new DefaultDomainKeyGenerator(materializer);

    public string Generate(DomainCacheOptions options, HttpContext httpContext)
    {
        var baseKey = _inner.Generate(options, httpContext);
        var tenantId = httpContext.User.FindFirst("tenant_id")?.Value ?? "anon";
        return $"{baseKey}|t:{tenantId}";
    }
}
```

`new DefaultDomainKeyGenerator()` (no materializer) skips `ICacheVaryContributor`, Accept, and auth-user material. Prefer the DI constructor above, or replace `IDomainKeyGenerator` after `AddCacheOrchestrator`.

Keys must be deterministic, must not contain secrets (they land in Redis and in logs), and should stay short.

## Results (`X-Cache` `dc=` and `DataCacheResult`)

| Result | Meaning |
|--------|---------|
| `Hit` | Served from cache |
| `Miss` | Factory ran; value stored |
| `Stale` | Factory failed; fail-safe may serve stale (**Fusion**; Hybrid does not expose the same fail-safe model) |
| `Bypass` | Skipped (for example `no-store`, or auth bypass when `DataCacheRespectAuthBypass`) |
| `Off` | Data cache disabled for the domain. The factory still runs and counts as a factory invocation (FA run). |
| `Unresolved` | No domain resolved; factory ran uncached (also a factory invocation). |

There is **no** `DataCacheResult.Fail` and **no** `dc=fail` on `X-Cache`. A hard factory throw with no fail-safe value is recorded on the meter as `cache_orchestrator.dc.requests` `result=fail` (and `factory.duration`), then the exception propagates.

When `dc` is present and is not `hit`, `X-Cache` also includes `fa=run`. That is the same factory-invocation set as Admin FA run (`miss` / `stale` / `bypass` / `off` / `unresolved`). OC `hit` omits `dc` and `fa`.

Admin Console exclusive pipeline mix is **OC hit + DC hit (fresh) + FA run**. FA run is factory-callback share of requests (including `off` / `unresolved` / bypass-with-factory / miss / stale). **DC stale %** is an overlay on requests, not a fourth mix segment. Layer `bypass` remains auth / no-store skip, not “caching disabled”.

---

## FusionCache provider

**CacheOrchestrator.FusionCache** registers ZiggyCreatures FusionCache as `IDataCacheProvider`. Entries are **serializable objects** (JSON via System.Text.Json): L1 in memory, optional L2 via a distributed store, optional backplane. Named engines: root **`DataCacheInstances`**. Redis L2 / backplane: [backends.md](backends.md), [deployment.md](deployment.md).

### Entry options

| Domain setting | FusionCache |
|----------------|-------------|
| `DataCache.TtlSeconds` → `DataCacheTtl` | `Duration` (capped by `FusionCache.HardTtlSeconds` if soft is larger) |
| `FusionCache.FailSafeSeconds` | `FailSafeMaxDuration` (+ fail-safe enabled when &gt; 0) |
| `FusionCache.JitterSeconds` | `JitterMaxDuration` |
| `FusionCache.EagerRefreshRatio` | `EagerRefreshThreshold` (`0` = disabled) |
| `FusionCache.FactorySoftTimeoutSeconds` / `FactoryHardTimeoutSeconds` | `FactorySoftTimeout` / `FactoryHardTimeout` |
| `FusionCache.AllowBackground*` | distributed and backplane background work |

Stampede protection and fail-safe stale serve come from FusionCache itself. Nested JSON schema: [configuration.md](configuration.md#fusioncache-fusion-package-only).

---

## HybridCache provider

**CacheOrchestrator.HybridCache** registers Microsoft HybridCache as `IDataCacheProvider`.

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache();
```

- Uses portable **`DataCache.TtlSeconds`** (expiration / local expiration).
- Nested **`FusionCache`** settings are ignored (no fail-safe / hard TTL / factory timeouts / named Fusion instances).
- Optional L2: configure HybridCache / `IDistributedCache` as usual (outside this package) — not Fusion `AddRedisBackend`.
- Prefer **Fusion** when you need fail-safe, eager refresh, or the full Fusion surface.

Package README: [CacheOrchestrator.HybridCache](../src/CacheOrchestrator.HybridCache/README.md).

---

## Related

- [Guide — concepts](guide/concepts.md)
- [packages.md](packages.md)
- [cache-keys.md](cache-keys.md)
- [configuration.md](configuration.md)
- [invalidation.md](invalidation.md)
- [architecture.md](architecture.md)
- [output-cache.md](output-cache.md)
