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

Multiple observers run in DI registration order. Exceptions from observers are logged and **do not** fail invalidation.

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

## Related

- [domain-profiles.md](domain-profiles.md) — Snapshot vs Dynamic + config recipes  
- [configuration.md](configuration.md)  
- [output-cache.md](output-cache.md)  
- [fusion-cache.md](fusion-cache.md)  
