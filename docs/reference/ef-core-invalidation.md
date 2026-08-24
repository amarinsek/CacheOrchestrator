# EF Core SaveChanges invalidation

> **Reference.** Product overview: [root README](../../README.md). Orientation: [Guide — topologies](../guide/topologies.md). Catalog: [documentation index](../README.md).

Package **`CacheOrchestrator.EFCore.Invalidation`**. After a successful `SaveChanges` / `SaveChangesAsync`, the cache for the rows that changed is purged through `ICacheOrchestratorInvalidator`.

Package README: [src/CacheOrchestrator.EFCore.Invalidation/README.md](../../src/CacheOrchestrator.EFCore.Invalidation/README.md). See also [invalidation.md](invalidation.md), [domain-profiles.md](../guide/domain-profiles.md), [data-cache.md](data-cache.md), [configuration.md](configuration.md).

## How it works

A **domain** is a cache policy group (TTL, Version, data-cache instance). Ids are unique together with `entityKind`, not inside the domain alone.

Row identity is always `(domain, entityKind, resourceId)`:

```text
entity:store:products:42
entitykind:store:products
```

The EF package only maps a CLR type → `(domain, entityKind)` and the primary key → `resourceId`. It then calls `ICacheOrchestratorInvalidator`. Multi-instance behaviour is whatever that invalidator already does (local only, Redis Fusion backplane, and/or `CacheOrchestrator.HttpBus`).

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
        ├─ local OC + data-cache tag purge
        └─ Redis backplane / HttpBus  (if those packages are configured)
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

## Install and composition

Full **packages + registration + config + endpoint** samples (in-app EF, and EF inside a class library): [packages.md §8–§9](../guide/packages.md).

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.EFCore.Invalidation
```

| API | Notes |
|-----|--------|
| `AddCacheOrchestratorEfCoreInvalidation` | Binds `Cache:EFCore:Invalidation`, registers the interceptor as a **singleton** |
| `AddEfCoreInvalidation` | Same registration on `ICacheOrchestratorBuilder` |
| `AddCacheOrchestratorInvalidation` | Attaches the interceptor to **this** `DbContext` only |

`AddCacheOrchestratorInvalidation` attaches that interceptor to **this** `DbContext` only. Call it on each options builder that should invalidate.

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

The HTTP / library cache path must use the **same** domain and `entityKind` as the mapping — see [packages.md §8–§9](../guide/packages.md).

Primary keys: stringify each PK part with invariant culture, join composite keys with `:`, then `DomainName.NormalizeResourceId`. Route `resourceRouteKey` must produce the same string. Entity kinds use `DomainName.NormalizeEntityKind` (garbage such as `!!!` is empty, not `default`).

TPH: Fluent `CacheInvalidate` and `Map<T>` match the **exact** `ClrType` — map each concrete type (a base-type Fluent/`Map` entry does not cover derived types). **`[CacheEntity]` is inherited** (`Inherited = true`); an attribute on the base type **does** apply to derived CLR types.

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

## SaveChanges vs `Execute*`

Composition samples (GET + PUT): [packages.md §8–§9](../guide/packages.md).

| Path | Invalidation |
|------|----------------|
| Tracked entity + `SaveChangesAsync` | Interceptor only — do **not** call `Invalidate*` |
| `ExecuteUpdate` / `ExecuteDelete` / `ExecuteInsert` | Manual `InvalidateEntitiesAsync` / `InvalidateEntityKindAsync` (no ChangeTracker) |

```csharp
await db.Products
    .Where(p => ids.Contains(p.Id))
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, p => p.Price * 0.8m), cancellationToken);

await invalidator.InvalidateEntitiesAsync("catalog", "products", ids, cancellationToken);
```

Unknown / too many ids: `InvalidateEntityKindAsync("catalog", "products")`.

---

## Multi-instance

The EF package does not talk to Redis or the Bus. It only calls `ICacheOrchestratorInvalidator`.

| Topology | After `SaveChanges` on node A |
|----------|-------------------------------|
| Single process | Local OC + data cache only |
| Redis Fusion L2 + backplane | Shared L2 purged; other nodes drop L1 via Fusion backplane |
| `CacheOrchestrator.HttpBus` (InMemory multi-node) | One `InvalidateCommand` per `(domain, entityKind)` group; peers ApplyLocal |
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

## Limits

- Reads still go through `GetOrSetEntityAsync` and Output Cache as usual.
- List and index entries tagged only `domain:{name}` stay until TTL, Version, or `InvalidateDomainAsync`.
- `Execute*` and raw SQL need a manual `Invalidate*` call.
- Composite primary keys are joined with `:`, then normalized. Binary keys are **hex** (`Convert.ToHexString`) — the HTTP `resourceId` must use the same convention.
- `BulkThreshold <= 0` disables the bulk path (always `InvalidateEntitiesAsync`).
- There is no ambient `Suppress()` yet; turn the feature off with `Enabled: false` or omit the interceptor on that context.

## Related

- [Guide — topologies](../guide/topologies.md)  
- [invalidation.md](invalidation.md) — tags, invalidator, multi-instance strategies  
- [data-cache.md](data-cache.md) — `GetOrSetEntityAsync`  
- [output-cache.md](output-cache.md) — `resourceRouteKey` + `entityKind`  
- [faq.md](../guide/faq.md) — `ExecuteUpdate`
- Package README — copy-paste samples  
