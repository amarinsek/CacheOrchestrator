# CacheOrchestrator.HybridCache

Microsoft **HybridCache** adapter for [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core) (`IDataCacheProvider`).

## Registration

```csharp
builder.Services.AddHybridCache(); // Microsoft.Extensions.Caching.Hybrid

builder.Services.AddCacheOrchestrator(builder.Configuration); // may register Fusion by default
builder.Services.AddCacheOrchestratorHybridCache();           // replaces IDataCacheProvider
```

Optional L2: configure HybridCache / `IDistributedCache` as usual (e.g. Redis) — outside this package.

## Capability matrix (vs Fusion)

| Feature | Hybrid provider |
|---------|-----------------|
| GetOrCreate + stampede | Yes |
| Tag invalidation | Yes (logical) |
| `DataCache.Ttl` → expiration | Yes |
| Soft + hard TTL split | Soft/TTL only (`DataCache.Ttl`) |
| Fail-safe stale serve | **No** |
| Eager refresh / jitter / factory timeouts | **No** (ignored) |
| Named data-cache instances | **No** (single DI `HybridCache`) |
| Redis backplane L1 | Weaker than Fusion; prefer Version bump + cluster bus |

Use **CacheOrchestrator.FusionCache** when you need fail-safe and the full Fusion surface.
