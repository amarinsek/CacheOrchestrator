# CacheOrchestrator.HybridCache

Microsoft **HybridCache** as `IDataCacheProvider` for CacheOrchestrator. Uses portable **`DataCache.Ttl`** only — Fusion-only knobs (fail-safe, hard TTL, factory timeouts, named instances) are not applied.

## Install

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.HybridCache
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

## Quick start

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache();
```

Optional L2: configure HybridCache / `IDistributedCache` as usual (outside this package).

## Capabilities (vs Fusion)

| Feature | Hybrid |
|---------|--------|
| GetOrCreate + stampede | Yes |
| Tag invalidation | Yes (logical) |
| `DataCache.Ttl` | Yes |
| Fail-safe / hard TTL / factory timeouts | No |
| Named `DataCacheInstances` | No (single DI HybridCache) |

Prefer **CacheOrchestrator.FusionCache** when you need fail-safe and the full Fusion surface.

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
