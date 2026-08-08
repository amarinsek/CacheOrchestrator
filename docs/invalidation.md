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
| Multi-instance InMemory, need immediate purge everywhere | Redis backplane **or** app fan-out via observer + bus |

---

## Multi-instance invalidation

### What the library does on one call

`ICacheOrchestratorInvalidator` always runs **in the current process**:

| Layer | Without Redis | With Redis (OC store and/or Fusion L2 + backplane) |
|-------|---------------|-----------------------------------------------------|
| Fusion L1 (memory) | Cleared only here | Cleared here; **other nodes** clear L1 via **backplane** |
| Fusion L2 | N/A or local only | Shared store purged |
| Output Cache | In-process only | Shared if OC provider is Redis |

So: **without a distributed cache + backplane, invalidation is machine-local.** That is expected.

Cluster **configuration** management (shared `appsettings.cache.json`, ConfigMap, env) does **not** by itself purge L1/L2 on other nodes. It only keeps **policy** in sync (Version, TTLs). See [deployment.md — Shared configuration](deployment.md#shared-configuration-across-instances).

### Approaches

| Approach | Immediate purge on all nodes? | When to use |
|----------|-------------------------------|-------------|
| **1. Bump `Version` (shared config)** | No — new key space; old entries expire by TTL | Snapshot / catalog cutover; simplest multi-node story |
| **2. Redis Fusion L2 + backplane** (+ optional Redis OC) | Yes for Fusion L1 (backplane) + shared L2 | **Recommended production multi-instance** |
| **3. Rolling restart of all instances** | Yes (cold process) | Emergency only |
| **4. App-level fan-out** (`ICacheInvalidationObserver` + message bus) | Yes if every node consumes and calls the invalidator | Multi-instance **InMemory** where you cannot use Redis backplane |

```text
Recommended multi-instance (immediate invalidation):

  Invalidate* on any node
       → local OC/FC
       → Redis L2 + pub/sub backplane
       → other nodes drop L1

Without Redis:

  Invalidate* on node A  →  only A
  Version bump (shared) → all nodes use new keys after reload/deploy
  Optional: observer publishes event → other nodes Invalidate* locally
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

### Approach 4 — Full fan-out sample with `ICacheInvalidationObserver`

Use when caches are **in-process** on each node but you still want “invalidate everywhere” after an admin action.

Flow:

```text
Node A: InvalidateDomainAsync("catalog")
     → local purge on A
     → Observer.OnAfterInvalidateAsync → publish message
              │
              ▼
     Message bus (RabbitMQ, Azure Service Bus, Redis pub/sub, NATS, …)
              │
     ┌────────┴────────┐
     ▼                 ▼
  Node B            Node C
  consumer          consumer
  → InvalidateTagsAsync (same tags)
  → local purge     → local purge
  (gate prevents re-publish → no loop)
```

The sample below is **application code** (not part of the library). Replace `IClusterBus` with your real bus client.

```csharp
using CacheOrchestrator.Invalidation;
using System.Collections.Concurrent;

// ---------- Message ----------

public sealed class CacheInvalidateMessage
{
    public required CacheInvalidationKind Kind { get; init; }
    public required string Scope { get; init; }
    public required string[] Tags { get; init; }
    public required string OriginInstanceId { get; init; }
}

// ---------- Abstract bus (swap for Rabbit / Service Bus / etc.) ----------

public interface IClusterBus
{
    Task PublishAsync(CacheInvalidateMessage message, CancellationToken cancellationToken);
    // In a real app, subscribe in a hosted service and call the consumer.
}

// ---------- Loop prevention ----------

/// <summary>
/// Marks invalidation calls that originated from a remote bus message so the
/// observer does not publish again (avoids A→bus→B→bus→A loops).
/// </summary>
public sealed class InvalidationFanoutGate
{
    private static readonly AsyncLocal<bool> Remote = new();

    public bool IsRemoteOrigin => Remote.Value;

    public IDisposable EnterRemote()
    {
        bool previous = Remote.Value;
        Remote.Value = true;
        return new Reset(previous);
    }

    private sealed class Reset(bool previous) : IDisposable
    {
        public void Dispose() => Remote.Value = previous;
    }
}

// ---------- Observer: publish after local invalidation ----------

public sealed class ClusterInvalidationPublisher : ICacheInvalidationObserver
{
    private readonly IClusterBus _bus;
    private readonly InvalidationFanoutGate _gate;
    private readonly string _instanceId;

    public ClusterInvalidationPublisher(IClusterBus bus, InvalidationFanoutGate gate)
    {
        _bus = bus;
        _gate = gate;
        _instanceId = Environment.MachineName; // or a stable pod/instance id
    }

    public ValueTask OnBeforeInvalidateAsync(
        CacheInvalidationContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public async ValueTask OnAfterInvalidateAsync(
        CacheInvalidationContext context,
        CacheInvalidationResult result,
        CancellationToken cancellationToken = default)
    {
        // Do not fan out invalidations that we applied because of a remote message.
        if (_gate.IsRemoteOrigin)
            return;

        // Optional: only broadcast successful (or always broadcast — product choice).
        if (context.Tags.Count == 0)
            return;

        var message = new CacheInvalidateMessage
        {
            Kind = context.Kind,
            Scope = context.Scope,
            Tags = context.Tags.ToArray(),
            OriginInstanceId = _instanceId
        };

        await _bus.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }
}

// ---------- Consumer: apply the same tags locally ----------

public sealed class ClusterInvalidationConsumer
{
    private readonly ICacheOrchestratorInvalidator _invalidator;
    private readonly InvalidationFanoutGate _gate;
    private readonly string _instanceId;

    public ClusterInvalidationConsumer(
        ICacheOrchestratorInvalidator invalidator,
        InvalidationFanoutGate gate)
    {
        _invalidator = invalidator;
        _gate = gate;
        _instanceId = Environment.MachineName;
    }

    public async Task HandleAsync(CacheInvalidateMessage message, CancellationToken cancellationToken)
    {
        if (string.Equals(message.OriginInstanceId, _instanceId, StringComparison.Ordinal))
            return;

        if (message.Tags is null || message.Tags.Length == 0)
            return;

        using (_gate.EnterRemote())
        {
            // Tags API hits all Fusion instances + Output Cache on this node.
            await _invalidator
                .InvalidateTagsAsync(message.Tags, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

// ---------- DI (Program.cs) ----------

builder.Services.AddSingleton<InvalidationFanoutGate>();
builder.Services.AddSingleton<IClusterBus, /* your bus */ MyRabbitClusterBus>();
builder.Services.AddSingleton<ICacheInvalidationObserver, ClusterInvalidationPublisher>();
builder.Services.AddSingleton<ClusterInvalidationConsumer>();

// In a BackgroundService / bus subscription:
//   var consumer = scope.ServiceProvider.GetRequiredService<ClusterInvalidationConsumer>();
//   await consumer.HandleAsync(message, stoppingToken);
```

**Notes for production fan-out**

- Prefer publishing **`Tags`** (stable, works for domain/entity/custom).  
- **Idempotent** consumers: double delivery should only re-evict.  
- **Ordering** is not required for tag purge.  
- If origin node failed mid-purge, you may still want to broadcast so peers clear.  
- For Output Cache + Fusion both InMemory, every node must run the consumer.  
- This pattern is **heavier** than Redis backplane; use Redis when you can.

### Choosing an approach (multi-instance)

| Goal | Prefer |
|------|--------|
| Monthly data cutover, long TTL | Shared config **Version** bump ([deployment.md](deployment.md)) |
| Many nodes, shared cache, immediate purge | **Redis** L2 + backplane |
| InMemory only, rare admin “clear catalog” | Observer + bus fan-out (sample above) |
| Sticky sessions + TTL-only | Local invalidation may be enough |

## Related

- [domain-profiles.md](domain-profiles.md) — Snapshot vs Dynamic + config recipes  
- [deployment.md](deployment.md) — multi-instance topologies + shared configuration  
- [configuration.md](configuration.md)  
- [output-cache.md](output-cache.md)  
- [fusion-cache.md](fusion-cache.md)  
- [backends.md](backends.md) — Redis package  
