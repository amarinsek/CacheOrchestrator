# CacheOrchestrator.FusionCache

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

This package registers ZiggyCreatures **FusionCache** as the **`IDataCacheProvider`** (data cache / DC). It wires named engines from `DataCacheInstances` and owns nested JSON **`FusionCache`** settings (hard TTL, fail-safe, factory timeouts, …). Portable TTL stays under **`DataCache`**.

## Install

```bash
dotnet add package CacheOrchestrator.FusionCache --prerelease
```

## Config

```json
{
  "Cache": {
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 },
        "FusionCache": {
          "HardTtlSeconds": 600,
          "FailSafeSeconds": 3600
        }
      }
    }
  }
}
```

## Example

In an ASP.NET Core host, pair with AspNetCore for Output Cache and HTTP helpers:

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
dotnet add package CacheOrchestrator.FusionCache --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

For a single NuGet reference that already includes AspNetCore + Fusion, see **CacheOrchestrator**.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/3.0.0-beta.2) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/3.0.0-beta.2) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/3.0.0-beta.2) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/3.0.0-beta.2) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/3.0.0-beta.2) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/3.0.0-beta.2) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/3.0.0-beta.2) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Data cache / Fusion](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/data-cache.md) (Fusion section)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
