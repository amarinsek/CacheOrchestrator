# EF Core SaveChanges invalidation

Optional package **`CacheOrchestrator.EFCore.Invalidation`**: after a successful EF Core `SaveChanges` / `SaveChangesAsync`, purge CacheOrchestrator entity tags via the public invalidator.

| | |
|--|--|
| **Package** | `CacheOrchestrator.EFCore.Invalidation` (NuGet) |
| **Requires** | `CacheOrchestrator` 2.0+ (`entityKind` on row identity) |
| **Is not** | An EF second-level cache, a query cache, or a cluster transport |

Short package readme (install, mapping snippets, HTTP+EF samples): [src/CacheOrchestrator.EFCore.Invalidation/README.md](../src/CacheOrchestrator.EFCore.Invalidation/README.md).  
Related: [invalidation.md](invalidation.md) · [domain-profiles.md](domain-profiles.md) · [fusion-cache.md](fusion-cache.md) · [configuration.md](configuration.md).

---

## Mental model

A **domain** is a cache **policy** group (TTL, Version, Fusion instance). It is not a table and not a namespace in which ids are unique.

Row identity is always `(domain, entityKind, resourceId)`:

```text
entity:store:products:42
entitykind:store:products
```

The EF package only maps a CLR type → `(domain, entityKind)` and the primary key → `resourceId`. It then calls `ICacheOrchestratorInvalidator`. Multi-instance behaviour is whatever that invalidator already does (local only, Redis Fusion backplane, and/or `CacheOrchestrator.Bus`).

```text
SavingChanges          snapshot mapped Added | Modified | Deleted
        │
SaveChanges (DB)
        │
   success                          failure
        │                              │
SavedChanges                    SaveChangesFailed
        │                         discard snapshot
        ▼
InvalidateEntitiesAsync  or  InvalidateEntityKindAsync (OnBulk)
        │
        ├─ local OC + Fusion tag purge
        └─ Redis backplane / Bus  (if those packages are configured)
```

---

## When to use it

| Situation | Approach |
|-----------|----------|
| CRUD through tracked entities + `SaveChanges` | This package (automatic) |
| `ExecuteUpdate` / `ExecuteDelete` / `ExecuteInsert` | Manual `InvalidateEntitiesAsync` / `InvalidateEntityKindAsync` |
| List/index pages tagged only `domain:{name}` | TTL, Version bump, or `InvalidateDomainAsync` — **not** this interceptor |
| Snapshot / catalog cutover | Domain `Version`, not per-row EF hooks |

---

## Install and register

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.EFCore;

builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration);

builder.Services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseSqlServer(connectionString);
    opt.AddCacheOrchestratorInvalidation(sp);
});
```

| API | Notes |
|-----|--------|
| `AddCacheOrchestratorEfCoreInvalidation` | Binds `Cache:EFCore:Invalidation`, registers the interceptor as a **singleton** |
| `AddEfCoreInvalidation` | Same registration on `ICacheOrchestratorBuilder` |
| `AddCacheOrchestratorInvalidation` | Attaches the interceptor to **this** `DbContext` only |

The first call does **not** attach to every context. You must call `AddCacheOrchestratorInvalidation` on each options builder that should invalidate.

---

## Mapping (code only)

No type list in appsettings. First match wins:

1. Fluent `CacheInvalidate` on the EF model  
2. `[CacheEntity]` on the CLR type (`entry.Metadata.ClrType`)  
3. `Map<T>` at DI registration  

Unmapped types, owned types, and keyless types are skipped (no throw).

### 1. Fluent (`OnModelCreating` / `IEntityTypeConfiguration<T>`)

Preferred when the domain model must stay persistence-ignorant.

```csharp
modelBuilder.Entity<Product>().CacheInvalidate("store", "products");
```

### 2. Attribute

```csharp
[CacheEntity("store", "products")]
public class Product { public int Id { get; set; } }
```

### 3. `Map<T>` at composition root

```csharp
builder.Services.AddCacheOrchestratorEfCoreInvalidation(builder.Configuration, o =>
{
    o.Map<Product>("store", "products");
    o.Map<Asset>("store", "assets");
});
```

The HTTP cache path must use the **same** domain and `entityKind`:

```csharp
.CacheOutputWithDomain("store", resourceRouteKey: "id", entityKind: "products")
await cache.GetOrSetEntityAsync(http, "store", "products", id.ToString(), factory, cancellationToken);
```

Primary keys: stringify each PK part with invariant culture, join composite keys with `:`, then `DomainName.NormalizeResourceId`. Route `resourceRouteKey` must produce the same string.

TPH: map the **concrete** `ClrType`. Mapping only the base type does not cover derived types.

---

## What runs on SaveChanges

| Event | Behaviour |
|-------|-----------|
| `SavingChanges` | Snapshot mapped `Added` / `Modified` / `Deleted`. Per-`DbContext` bag (interceptor is a singleton — no instance fields). Deleted PKs are captured here. |
| Successful `SavedChanges` | Re-read PK (identity is assigned). Group by `(domain, entityKind)`. Invalidate. |
| `SaveChangesFailed` | Discard snapshot. No invalidation. |
| Invalidator throws | Logged. **Save still succeeds.** |
| Ambient transaction later rolled back | `SavedChanges` already ran → **false miss**, not stale. |

`OnBulk` applies per group when id count ≥ `BulkThreshold`:

| Value | Effect |
|-------|--------|
| `Entities` | Always `InvalidateEntitiesAsync` |
| `Kind` (default) | `InvalidateEntityKindAsync` — all rows of that kind, not sibling kinds |
| `Domain` | `InvalidateDomainAsync` — **entire policy group** (products **and** assets in `store`) |

---

## HTTP + EF (end to end)

### Tracked save — interceptor only

Load a tracked entity, mutate it, `SaveChangesAsync`. Do **not** call `Invalidate*`.

```csharp
product.Price = body.Price;
await db.SaveChangesAsync(cancellationToken);
// SaveChangesAsync uses ChangeTracker, so the interceptor invalidates automatically.
```

Next `GET` for that id is a MISS on OC + Fusion entity tags. Sibling ids stay cached.

### `Execute*` — manual invalidation

`ExecuteUpdateAsync` does **not** use `ChangeTracker`, so the interceptor does **not** run.

```csharp
List<int> ids = await db.Products
    .Where(p => p.CategoryId == categoryId)
    .Select(p => p.Id)
    .ToListAsync(cancellationToken);

await db.Products
    .Where(p => ids.Contains(p.Id))
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 0.8m), cancellationToken);

await invalidator.InvalidateEntitiesAsync(
    "store", "products", ids.Select(id => id.ToString()), cancellationToken);
```

Unknown / too many ids: `InvalidateEntityKindAsync("store", "products")`.

---

## Multi-instance

The EF package does not talk to Redis or the Bus. It only calls `ICacheOrchestratorInvalidator`.

| Topology | After `SaveChanges` on node A |
|----------|-------------------------------|
| Single process | Local OC + Fusion only |
| Redis Fusion L2 + backplane | Shared L2 purged; other nodes drop L1 via Fusion backplane |
| `CacheOrchestrator.Bus` (InMemory multi-node) | One `InvalidateCommand` per `(domain, entityKind)` group; peers ApplyLocal |
| Neither | Other nodes keep stale L1 until TTL / Version |

Use the same `entityKind` on every node. Mixed 1.0 / 2.0 entity tags do not match.

Upgrade all nodes together before relying on entity Bus commands (2.0 tag shape).

---

## Configuration (`Cache:EFCore:Invalidation`)

Operational flags only. Bound from the same root section as `AddCacheOrchestrator` (default `Cache`).

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `true` | Master switch |
| `BulkThreshold` | `20` | Id count that triggers `OnBulk` |
| `OnBulk` | `Kind` | `Entities` · `Kind` · `Domain` |

---

## Limitations

- Not a cache of EF entities. Reads still go through `GetOrSetEntityAsync` / Output Cache as usual.  
- List/index entries tagged only `domain:{name}` are not evicted by row or kind invalidation.  
- `Execute*` and raw SQL stay manual.  
- Composite / binary PKs must match the HTTP resource id convention.  
- No `Suppress()` ambient scope yet (disable with `Enabled: false` or omit the interceptor on that context).

---

## Tests

Unit tests (EF InMemory + invalidator spy) cover mapping order, batching, delete PK snapshot, failed save, `OnBulk`, and interceptor exceptions.

Existing Redis multi-node and Bus tests already cover “`InvalidateEntitiesAsync` on one host evicts Fusion/OC on another”. The interceptor is a thin caller of that API, so a second suite of two-host EF + Redis tests would mostly re-run the backplane with a heavier fixture. A more useful extra test, if added later, is **one process**: TestServer + real Fusion + EF `SaveChanges` → next HTTP GET is a MISS.

---

## Related

- [invalidation.md](invalidation.md) — tags, invalidator, multi-instance strategies  
- [fusion-cache.md](fusion-cache.md) — `GetOrSetEntityAsync`  
- [output-cache.md](output-cache.md) — `resourceRouteKey` + `entityKind`  
- [faq.md](faq.md) — `ExecuteUpdate` gotcha  
- Package README — copy-paste samples  
