# CacheOrchestrator.AspNetCore

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

This package is the **ASP.NET Core host** layer: Output Cache domain policies, client Cache-Control, Admin API, vary rules, and HTTP **`IDomainDataCache`** (a thin projection over Core `ICacheOrchestrator`). It depends on **Core** only. You still need a data-cache provider package (Fusion or Hybrid) unless you use Output Cache alone.

## Install

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
```

## Config

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 },
        "OutputCache": { "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
      }
    }
  }
}
```

## Example

With Fusion as the data engine (install that package as well):

```bash
dotnet add package CacheOrchestrator.FusionCache --prerelease
dotnet add package CacheOrchestrator.AspNetCore --prerelease
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

Without `.CacheOutputWithDomain` / `[CacheDomain]`, Output Cache does not store (base policy is `NoCache`).

For a single NuGet reference that already includes AspNetCore + Fusion, see **CacheOrchestrator**.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/3.0.0-beta.2) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/3.0.0-beta.2) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/3.0.0-beta.2) | FusionCache data-cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/3.0.0-beta.2) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/3.0.0-beta.2) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/3.0.0-beta.2) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/3.0.0-beta.2) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Output Cache](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/output-cache.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
