# Invalidation

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — topologies](guide/topologies.md). Catalog: [documentation index](README.md).

How to drop cached data so the next request loads it again. Snapshot versus changing records: [domain-profiles.md](domain-profiles.md).

1. **Version stamp** — change `Version` so new keys never match old ones (a bulk cutover).
2. **Domain tag** — remove everything tagged `domain:{name}`.
3. **Entity tag** — remove one resource tagged `entity:{domain}:{entityKind}:{resourceId}`.

## Version (preferred for bulk cutovers)

In config:

```json
"products": {
  "Version": "v1"
}
```

When you deploy a content update, bump `Version` (and reload configuration).  

- Output Cache vary value `data-version` changes  
- Fusion keys include the version hex  
- Old entries expire by TTL (no mass delete)

If `Version` is omitted, the library uses `"1"` and logs a warning (keys stable across restarts).

## Programmatic API (`ICacheOrchestratorInvalidator`)

All methods return **`CacheInvalidationResult`** (best-effort: they do **not** throw when Fusion or Output Cache fails).

```csharp
using CacheOrchestrator.Invalidation;

// Entire domain (OC + FC on the instance that owns the domain)
CacheInvalidationResult r1 = await invalidator.InvalidateDomainAsync("products", cancellationToken);
if (!r1.Succeeded)
{
    // r1.FusionSucceeded / r1.OutputSucceeded / r1.Errors
}

// Several domains
CacheInvalidationResult r2 = await invalidator.InvalidateDomainsAsync(
    ["products", "catalog"],
    cancellationToken);

// Single entity — requires entries stored with entityKind + resource id / resourceRouteKey
CacheInvalidationResult r3 = await invalidator.InvalidateEntityAsync(
    "store", "products", 42, cancellationToken);

// Several ids of one kind (one local apply + one Bus publish)
CacheInvalidationResult r4 = await invalidator.InvalidateEntitiesAsync(
    "store", "products", new[] { 42, 43 }, cancellationToken);

// Every entry tagged entitykind:store:products
CacheInvalidationResult r5 = await invalidator.InvalidateEntityKindAsync(
    "store", "products", cancellationToken);

// Custom or multiple tags (all FusionCache instances + Output Cache)
CacheInvalidationResult r6 = await invalidator.InvalidateTagsAsync(
    ["domain:store", "entity:store:products:42", "custom:batch-7"],
    cancellationToken);
```

### `CacheInvalidationResult`

| Property | Meaning |
|----------|---------|
| `Scope` | Label (domain, `domain/id`, joined tags, or `(skipped)`) |
| `Tags` | Tags targeted for eviction |
| `FusionSucceeded` | All Fusion removals for this call succeeded |
| `OutputSucceeded` | All Output Cache evictions succeeded |
| `IsSkipped` | No-op (empty domain/tags); nothing was evicted |
| `Succeeded` | Both layers succeeded **and** the call was not skipped. Cluster publish failures do not flip this. |
| `ClusterPublish` | Per-peer bus outcomes when the bus ran; `null` when publish was skipped. `InvalidateDomainsAsync` **drops** this on the aggregate (`null`); inspect `Errors` for peer failures on a batch. |
| `Errors` | Non-fatal messages (partial failure or skip reason) |

Empty input (null domain, no tags) → `CacheInvalidationResult.Skipped(...)` with `IsSkipped == true`, `Succeeded == false`, and no store calls.

`ICacheInvalidationObserver` sees `CacheInvalidationKind.Domain` (or Entity / EntityKind / Tags) per operation. `InvalidateDomainsAsync` also fires one aggregate **`Domains`** before/after pair for the batch (per-domain callbacks still run).

### Tag formats (`CacheTags`)

| Tag | When applied |
|-----|----------------|
| `domain:{name}` | Every Output Cache policy entry; every Fusion `GetOrSet` |
| `entity:{domain}:{entityKind}:{resourceId}` | Fusion when using `GetOrSetEntityAsync`; Output Cache when `resourceRouteKey` **and** `entityKind` are set on the policy |
| `entitykind:{domain}:{entityKind}` | Same writes; purge all entries of that kind with `InvalidateEntityKindAsync` |
| Custom | Your tags — purge with `InvalidateTagsAsync` |

### Wiring entity tags

Identity is declared once on the endpoint. Fusion and Output Cache share it; factories may add members / dependsOn / aliases via `EntityCache` / `EntitySet`.

```csharp
// Minimal API — detail
app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainFusionCache cache, CancellationToken ct) =>
{
    var product = await cache.GetOrSetEntityAsync(http, token => factory(token), ct);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products");

// MVC
[CacheDomain("store", resourceRouteKey: "id", entityKind: "products")]
public class ProductsController : ControllerBase { }

// Collection (kind-scoped)
[CacheDomain("store", entityKind: "products")]
public async Task<ActionResult<IReadOnlyList<Product>>> List(...) { /* GetOrSetEntitySetAsync */ }
```

### Observers (audit / webhooks)

Register one or more `ICacheInvalidationObserver` implementations. They run in DI registration order on the **same process** that called the invalidator. Exceptions from observers are logged and **do not** fail invalidation.

Simple audit hook:

```csharp
public sealed class AuditInvalidationObserver : ICacheInvalidationObserver
{
    public ValueTask OnBeforeInvalidateAsync(CacheInvalidationContext context, CancellationToken cancellationToken)
    {
        // context.Kind, context.Scope, context.Tags
        return ValueTask.CompletedTask;
    }

    public ValueTask OnAfterInvalidateAsync(
        CacheInvalidationContext context,
        CacheInvalidationResult result,
        CancellationToken cancellationToken)
    {
        // result.Succeeded, result.Errors
        return ValueTask.CompletedTask;
    }
}

// Program.cs
builder.Services.AddSingleton<ICacheInvalidationObserver, AuditInvalidationObserver>();
```

Observers are **audit/webhooks on this process only**. Cross-instance purge is the [cluster bus](cluster-bus.md) or Redis Fusion backplane — see [Multi-instance invalidation](#multi-instance-invalidation).

### Implementation notes

1. Normalize domain / resource id  
2. Resolve Fusion instance from domain options (domain/entity APIs)  
3. `IFusionCache.RemoveByTagAsync`  
4. `IOutputCacheStore.EvictByTagAsync`  
5. Best-effort failures → warnings + `CacheInvalidationResult.Errors`  
6. Metrics `cache_orchestrator.invalidate` only when **both** layers succeed for that scope  
7. `InvalidateTagsAsync` fans out to **all** registered FusionCache instances  

## When to use which

| Scenario | Approach |
|----------|----------|
| Scheduled content release (tiles, catalog cutover) | Bump `Version` |
| Admin updated one product | `InvalidateEntityAsync` |
| Purge whole domain | `InvalidateDomainAsync` |
| Purge several domains after deploy | `InvalidateDomainsAsync` |
| Emergency “everything for products is wrong” | Domain invalidate and/or Version bump |
| Custom multi-tag purge | `InvalidateTagsAsync` |
| Audit / Slack / webhook | `ICacheInvalidationObserver` |
| Multi-instance InMemory, need immediate purge everywhere | **CacheOrchestrator.Bus** or Redis backplane |
| Admin Version/TTL on all InMemory nodes | Bus + Admin `distribute: true` (or Admin Console App fan-out) |

---

## EF Core SaveChanges (`CacheOrchestrator.EFCore.Invalidation`)

After a successful `SaveChanges` the package calls the public invalidator (and the Bus, if configured). Mapping lives in code. Full guide: [ef-core-invalidation.md](ef-core-invalidation.md).

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(cs);
    opt.AddCacheOrchestratorInvalidation(sp);
});
```

Map types in **code** (not appsettings): `[CacheEntity]`, Fluent `CacheInvalidate` on the EF model, or `Map<T>` at registration. Full guide: [ef-core-invalidation.md](ef-core-invalidation.md).

`ExecuteUpdate` / `ExecuteDelete` / `ExecuteInsert` do **not** go through `ChangeTracker`:

```csharp
await invalidator.InvalidateEntitiesAsync("store", "products", productIds);
// or, unknown / too many ids:
await invalidator.InvalidateEntityKindAsync("store", "products");
```

List/index endpoints tagged only `domain:{name}` are not refreshed by row invalidation.

---

## Multi-instance invalidation

### What the library does on one call

`ICacheOrchestratorInvalidator` always applies **locally** on the calling process. When **`CacheOrchestrator.Bus`** is registered and enabled, it then **publishes** an `InvalidateCommand` to peers (peers ApplyLocal only — no echo).

| Layer | Without Redis | With Redis (OC store and/or Fusion L2 + backplane) |
|-------|---------------|-----------------------------------------------------|
| Fusion L1 (memory) | Cleared only here (unless Bus peers apply too) | Cleared here; **other nodes** clear L1 via **backplane** |
| Fusion L2 | N/A or local only | Shared store purged |
| Output Cache | In-process only (unless Bus peers apply too) | Shared if OC provider is Redis |

Without a distributed store, a backplane, or the Bus, invalidation applies on the calling process only.

Cluster **configuration** management (shared `appsettings.cache.json`, ConfigMap, env) does **not** by itself purge L1/L2 on other nodes. It only keeps **policy** in sync (Version, TTLs). See [deployment.md — Shared configuration](deployment.md#shared-configuration-across-instances).

### Approaches

| Approach | Immediate purge on all nodes? | When to use |
|----------|-------------------------------|-------------|
| **1. Bump `Version` (shared config)** | No — new key space; old entries expire by TTL | Snapshot / catalog cutover; simplest multi-node story |
| **2. Redis Fusion L2 + backplane** (+ optional Redis OC) | Yes for Fusion L1 (backplane) + shared L2 | **Recommended production multi-instance** |
| **3. CacheOrchestrator.Bus** (HTTP + Static / ServiceDiscovery) | Yes if every peer has receive endpoints | Multi-instance **InMemory**; also Version/TTL overlays via Admin `distribute`. Full guide: [cluster-bus.md](cluster-bus.md) |
| **4. Rolling restart of all instances** | Yes (cold process) | Emergency only |
| **5. Custom observer + external bus** | Yes if you implement it | Rare; prefer package Bus |

```text
Recommended multi-instance (immediate invalidation):

  Invalidate* on any node
       → local OC/FC
       → Redis L2 + pub/sub backplane
       → other nodes drop L1

Without Redis (optional Bus package):

  Invalidate* on node A
       → local purge on A
       → HttpClusterCommandBus → peers ApplyLocal
  Version/TTL Admin with distribute:true → VersionBumpCommand / TtlPatchCommand
```

### Approach 1 — Version + shared config (no bus)

1. Put domain `Version` in shared config ([deployment.md](deployment.md)).  
2. On cutover, set e.g. `"2026-08"` → `"2026-09"` once and deliver to all instances.  
3. New requests use new keys; old entries age out.

No need to call `InvalidateDomainAsync` on every machine for bulk content releases.

### Approach 2 — Redis backplane (library-supported)

Use `CacheOrchestrator.Redis`, `"Provider": "Redis"` for Fusion (and optionally Output Cache).  
`InvalidateDomainAsync` / entity / tags on **any** instance:

- removes tags from shared L2  
- publishes backplane messages so **other instances clear L1**

Details: [deployment.md](deployment.md), [backends.md](backends.md).

### Approach 3 — CacheOrchestrator.Bus (optional package)

Full reference: **[cluster-bus.md](cluster-bus.md)** (install, membership Static/ServiceDiscovery, commands, Admin Console App, metrics, security).

```bash
dotnet add package CacheOrchestrator.Bus
```

```csharp
using CacheOrchestrator.Bus;

builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddHttpClusterBus());
app.UseCacheOrchestrator();
app.MapCacheOrchestratorHttpBus(); // independent of Admin
```

| Behaviour | Detail |
|-----------|--------|
| `Invalidate*` (app code) | Local apply + publish when bus enabled |
| Admin API mutations | `distribute: true` → peers; default is this process only |
| Peers | `POST …/cluster/apply` ApplyLocal only (anti-echo) |

Prefer Redis L2 and the backplane when instances share Fusion data. Use the Bus when stores are in-memory and you still need commands on every node, including runtime Version and TTL overlays.

`ICacheInvalidationObserver` remains for **audit/webhooks only** — not a second cluster bus when Bus is registered.

### Choosing an approach (multi-instance)

| Goal | Prefer |
|------|--------|
| Monthly data cutover, long TTL | Shared config **Version** bump ([deployment.md](deployment.md)) |
| Many nodes, shared cache, immediate purge | **Redis** L2 + backplane |
| InMemory only, invalidate everywhere | **CacheOrchestrator.Bus** |
| Runtime Version/TTL on all InMemory nodes | Bus + Admin `distribute: true` (or Admin Console App fan-out to each node) |
| Sticky sessions + TTL-only | Local invalidation may be enough |

## Related

- [Guide — topologies](guide/topologies.md) — which approach across instances  
- [cache-keys.md](cache-keys.md) — keys vs tags, Version in key material  
- [domain-profiles.md](domain-profiles.md) — Snapshot vs Dynamic + config recipes  
- [deployment.md](deployment.md) — multi-instance topologies + shared configuration  
- [configuration.md](configuration.md)  
- [output-cache.md](output-cache.md)  
- [fusion-cache.md](fusion-cache.md)  
- [backends.md](backends.md) — Redis package  
- [cluster-bus.md](cluster-bus.md) — optional multi-instance command bus  

