# CacheOrchestrator.HybridCache

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

This package registers Microsoft **HybridCache** as the **`IDataCacheProvider`**. It uses portable **`DataCache.TtlSeconds`** only. Fusion-specific options (fail-safe, hard TTL, factory timeouts, named data-cache instances) are not applied.

## Install

```bash
dotnet add package CacheOrchestrator.HybridCache --prerelease
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

## Config

```json
{
  "Cache": {
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 }
      }
    }
  }
}
```

## Example

In an ASP.NET Core host:

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
dotnet add package CacheOrchestrator.HybridCache --prerelease
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache();

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Optional L2: configure HybridCache / `IDistributedCache` as usual (outside this package). Prefer **CacheOrchestrator.FusionCache** when you need fail-safe and the full Fusion surface.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/3.0.0-beta.2) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/3.0.0-beta.2) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/3.0.0-beta.2) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/3.0.0-beta.2) | FusionCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/3.0.0-beta.2) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/3.0.0-beta.2) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/3.0.0-beta.2) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Data cache / Hybrid](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/data-cache.md#hybridcache-provider)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
