# CacheOrchestrator.AspNetCore

ASP.NET Core host integration: Output Cache domain policies, client Cache-Control, Admin API, vary materialization, and HTTP **`IDomainDataCache`** (projection over Core `ICacheOrchestrator`).

Depends on **CacheOrchestrator.Core** only. Register a data provider separately (**FusionCache** or **HybridCache**), or use the meta package **CacheOrchestrator** (AspNetCore + Fusion).

## Install

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.FusionCache
```

## Quick start

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

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta convenience entry |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Libraries |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Hybrid instead of Fusion |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Redis backends |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Output Cache](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/output-cache.md)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
