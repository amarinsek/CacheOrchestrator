# CacheOrchestrator

Domain-based caching for ASP.NET Core that orchestrates **Output Cache**, **FusionCache**, and client **Cache-Control** under the same model.

| | |
|--|--|
| **NuGet** | [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) |
| **Targets** | `net8.0`, `net10.0` |
| **Redis (optional)** | [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) |
| **Full documentation** | **[GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)** |

## Install

```bash
dotnet add package CacheOrchestrator
```

```bash
# Optional Redis backends (Output Cache store + Fusion L2 / backplane):
dotnet add package CacheOrchestrator.Redis
```

## Quick start

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;

builder.Services.AddCacheOrchestrator(builder.Configuration);
// Redis: builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductsAsync(ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Configure domains under `"Cache"` in `appsettings.json` — see the full examples on GitHub.

## Documentation

Everything else (why domains, Client Cache Schedule, invalidation, deployment, samples) lives in the repository:

- **[README (full)](https://github.com/amarinsek/CacheOrchestrator#readme)**
- [Getting started](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/getting-started.md)
- [Docs index](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/README.md)
- [Minimal sample](https://github.com/amarinsek/CacheOrchestrator/tree/main/samples/CacheOrchestrator.Minimal)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
