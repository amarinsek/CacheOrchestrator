# CacheOrchestrator.Core

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) configures Output Cache, application **data cache**, and client `Cache-Control` under one **domain** model. It does not replace those systems or own a store.

This package is the **Http-free core**: domain options, Version, portable `DataCache` policy, entity footprint/tags, **`ICacheOrchestrator`**, **`CacheDomainContext`**, invalidation, and cluster **contracts**. Use it from class libraries and workers. It does not reference ASP.NET or a concrete cache engine.

## Install

```bash
dotnet add package CacheOrchestrator.Core
```

## Example

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

The host application chooses the domain name (`new CacheDomainContext("catalog")` or a per-request value), registers a data provider (Fusion or Hybrid), and optionally AspNetCore for Output Cache / client headers.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | FusionCache data-cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
