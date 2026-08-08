# CacheOrchestrator

Domain-based caching for ASP.NET Core that orchestrates **Output Cache**, **FusionCache**, and client **Cache-Control** under the same model.

| | |
|--|--|
| **Targets** | `net8.0`, `net10.0` |
| **Redis** | Install [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) and call `AddRedisBackend()` |
| **Docs & samples** | [GitHub repository](https://github.com/amarinsek/CacheOrchestrator) |

## Install

```bash
dotnet add package CacheOrchestrator
# Optional Redis backends:
dotnet add package CacheOrchestrator.Redis
```

## Quick start

**1. `appsettings.json`**

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "Domains": {
      "catalog": {
        "Version": "1",
        "ClientCacheability": "Public",
        "ClientTtlSeconds": 60,
        "OutputCacheTtlSeconds": 120,
        "FusionCacheSoftTtlSeconds": 300
      }
    }
  }
}
```

**2. `Program.cs`**

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.OutputCache;

builder.Services.AddCacheOrchestrator(builder.Configuration);
// With Redis: builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductsAsync(ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

## More

- Full README, samples, and deep docs:  
  **https://github.com/amarinsek/CacheOrchestrator**
- Getting started:  
  **https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/getting-started.md**
- Minimal sample (MISS → HIT in one minute):  
  **https://github.com/amarinsek/CacheOrchestrator/tree/main/samples/CacheOrchestrator.Minimal**

## License

MIT — see [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md).
