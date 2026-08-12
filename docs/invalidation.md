# Invalidation

Three complementary strategies:

1. **Version stamp** — change `Version` so new keys never match old ones (bulk cutover)  
2. **Domain tag eviction** — remove all entries tagged `domain:{name}`  
3. **Entity tag eviction** — remove one resource tagged `entity:{domain}:{resourceId}` (CRUD under the same Version)

For a plain-language explanation of Snapshot vs Dynamic domains, see **[domain-profiles.md](domain-profiles.md)**.

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
- Old entries age out by TTL (no thundering delete storm)

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

// Single entity — requires entries stored with resource id / resourceRouteKey
CacheInvalidationResult r3 = await invalidator.InvalidateEntityAsync(
    "product-detail", "42", cancellationToken);

// Custom or multiple tags (all FusionCache instances + Output Cache)
CacheInvalidationResult r4 = await invalidator.InvalidateTagsAsync(
    ["domain:products", "entity:products:42", "custom:batch-7"],
    cancellationToken);
```

### `CacheInvalidationResult`

| Property | Meaning |
|----------|---------|
| `Scope` | Label (domain, `domain/id`, joined tags, or `(skipped)`) |
| `Tags` | Tags targeted for eviction |
| `FusionSucceeded` | All Fusion removals for this call succeeded |
| `OutputSucceeded` | All Output Cache evictions succeeded |
| `Succeeded` | Both layers succeeded |
| `Errors` | Non-fatal messages (partial failure detail) |

Empty input (null domain, no tags) → `CacheInvalidationResult.Skipped(...)` with `Succeeded == true` and no store calls.

### Tag formats (`CacheTags`)

| Tag | When applied |
|-----|----------------|
| `domain:{name}` | Every Output Cache policy entry; every Fusion `GetOrSet` |
| `entity:{domain}:{resourceId}` | Fusion when using `GetOrSetAsync(http, domain, resourceId, factory)`; Output Cache when `resourceRouteKey` is set on the policy |
| Custom | Your tags — purge with `InvalidateTagsAsync` |

### Wiring entity tags

```csharp
// Fusion
await cache.GetOrSetAsync(http, "product-detail", productId, factory, ct);

// Output Cache — tag entity from route value "id"
app.MapGet("/api/products/{id}", ...).CacheOutputWithDomain("product-detail", resourceRouteKey: "id");

// MVC
[CacheDomain("product-detail", resourceRouteKey: "id")]
public class ProductsController : ControllerBase { }
```

### Observers (audit / webhooks)

Register one or more `ICacheInvalidationObserver` implementations. They run in DI registration order on the **same process** that called the invalidator. Exceptions from observers are logged and **do not** fail invalidation.

Simple audit hook:

```csharp
public sealed class AuditInvalidationObserver : ICacheInvalidationObserver
{
    public ValueTask OnBeforeInvalidateAsync(CacheInvalidationContext context, CancellationToken ct)
    {
        // context.Kind, context.Scope, context.Tags
        return ValueTask.CompletedTask;
    }

    public ValueTask OnAfterInvalidateAsync(
        CacheInvalidationContext context,
        CacheInvalidationResult result,
        CancellationToken ct)
    {
        // result.Succeeded, result.Errors
        return ValueTask.CompletedTask;
    }
}

// Program.cs
builder.Services.AddSingleton<ICacheInvalidationObserver, AuditInvalidationObserver>();
```

For **multi-instance fan-out** (publish to a bus so other nodes invalidate locally), see [Multi-instance invalidation](#multi-instance-invalidation) below — full sample using `ICacheInvalidationObserver`.

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
| Admin Version/TTL on all InMemory nodes | Bus + Admin `distribute: true` (or Admin App fan-out) |

---

## Multi-instance invalidation

### What the library does on one call

`ICacheOrchestratorInvalidator` always applies **locally** on the calling process. When **`CacheOrchestrator.Bus`** is registered and enabled, it then **publishes** an `InvalidateCommand` to peers (peers ApplyLocal only — no echo).

| Layer | Without Redis | With Redis (OC store and/or Fusion L2 + backplane) |
|-------|---------------|-----------------------------------------------------|
| Fusion L1 (memory) | Cleared only here (unless Bus peers apply too) | Cleared here; **other nodes** clear L1 via **backplane** |
| Fusion L2 | N/A or local only | Shared store purged |
| Output Cache | In-process only (unless Bus peers apply too) | Shared if OC provider is Redis |

So: **without a distributed cache + backplane and without Bus, invalidation is machine-local.** That is expected.

Cluster **configuration** management (shared `appsettings.cache.json`, ConfigMap, env) does **not** by itself purge L1/L2 on other nodes. It only keeps **policy** in sync (Version, TTLs). See [deployment.md — Shared configuration](deployment.md#shared-configuration-across-instances).

### Approaches

| Approach | Immediate purge on all nodes? | When to use |
|----------|-------------------------------|-------------|
| **1. Bump `Version` (shared config)** | No — new key space; old entries expire by TTL | Snapshot / catalog cutover; simplest multi-node story |
| **2. Redis Fusion L2 + backplane** (+ optional Redis OC) | Yes for Fusion L1 (backplane) + shared L2 | **Recommended production multi-instance** |
| **3. CacheOrchestrator.Bus** (HTTP + Static membership) | Yes if every peer has receive endpoints | Multi-instance **InMemory**; also Version/TTL overlays via Admin `distribute` |
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

Install and register:

```bash
dotnet add package CacheOrchestrator.Bus
```

```csharp
using CacheOrchestrator.Bus;

builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddHttpClusterBus();
});

app.UseCacheOrchestrator();
app.MapCacheOrchestratorHttpBus(); // receive endpoints — independent of Admin
// app.MapCacheOrchestratorAdmin(); // optional
```

```json
{
  "Cache": {
    "Namespace": "app1",
    "InstanceId": "app1-a",
    "Cluster": {
      "Bus": {
        "Enabled": true,
        "Membership": "Static",
        "PeerTimeoutMs": 2000,
        "MaxParallelism": 32,
        "Static": {
          "Instances": [
            { "Id": "app1-a", "Url": "http://10.0.0.1:8080" },
            { "Id": "app1-b", "Url": "http://10.0.0.2:8080" }
          ]
        }
      }
    }
  }
}
```

| Behaviour | Detail |
|-----------|--------|
| `Invalidate*` (app code) | Local apply + publish when bus enabled |
| Admin `POST …/invalidate` | `distribute: false` (default) = local only; `true` = local + publish |
| Admin version / TTL | Same `distribute` flag → `VersionBumpCommand` / `TtlPatchCommand` |
| Peers | `POST {prefix}/cluster/apply` ApplyLocal only (anti-echo) |
| Auth | `X-Cache-Admin-Key` from `Cache:Cluster:Bus:ApiKey` or `Cache:Admin:ApiKey` |

**Bus vs Redis:** Bus is not a replacement for Fusion L2 + Redis backplane. Prefer Redis for continuous shared data; use Bus for InMemory multi-instance and for runtime Version/TTL overlays.

`ICacheInvalidationObserver` remains for **audit/webhooks only** — do not use it to build a second cluster fan-out when Bus is registered.

### Choosing an approach (multi-instance)

| Goal | Prefer |
|------|--------|
| Monthly data cutover, long TTL | Shared config **Version** bump ([deployment.md](deployment.md)) |
| Many nodes, shared cache, immediate purge | **Redis** L2 + backplane |
| InMemory only, invalidate everywhere | **CacheOrchestrator.Bus** |
| Runtime Version/TTL on all InMemory nodes | Bus + Admin `distribute: true` (or Admin App fan-out to each node) |
| Sticky sessions + TTL-only | Local invalidation may be enough |

## Related

- [cache-keys.md](cache-keys.md) — keys vs tags, Version in key material  
- [domain-profiles.md](domain-profiles.md) — Snapshot vs Dynamic + config recipes  
- [deployment.md](deployment.md) — multi-instance topologies + shared configuration  
- [configuration.md](configuration.md)  
- [output-cache.md](output-cache.md)  
- [fusion-cache.md](fusion-cache.md)  
- [backends.md](backends.md) — Redis package  
