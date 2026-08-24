# CacheOrchestrator.Core

Http-free contracts and orchestration: domains, Version, portable `DataCache` policy, entity footprint/tags, **`ICacheOrchestrator`**, **`CacheDomainContext`**, invalidation, and cluster command contracts.

Add this package for **class libraries** and workers. It does not reference ASP.NET, FusionCache, or HybridCache.

## Install

```bash
dotnet add package CacheOrchestrator.Core
```

## Quick start

```csharp
public sealed class CatalogService(ICacheOrchestrator cache)
{
    public ValueTask<ProductDto?> GetProductAsync(
        CacheDomainContext cacheDomain,
        string id,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            cacheDomain,
            logicalKey: $"product:{id}",
            async ct => await LoadProductAsync(id, ct),
            cancellationToken);
}
```

The host supplies `CacheDomainContext` and registers a data provider (Fusion or Hybrid) plus optional AspNetCore for Output Cache.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | Fusion `IDataCacheProvider` |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Hybrid `IDataCacheProvider` |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | HTTP OC / Client Cache / Admin |
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta (AspNetCore + Fusion) |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md) (library scenarios)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
