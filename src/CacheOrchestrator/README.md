# CacheOrchestrator

Meta package for typical ASP.NET Core apps: **AspNetCore** (Output Cache, Client Cache-Control, Admin API, `IDomainDataCache`) + **FusionCache** (data-cache provider).

Coordinates OC, data cache (DC), and client headers under one **domain** model. It does not own a store.

Targets **.NET 8** and **.NET 10**.

## Install

```bash
dotnet add package CacheOrchestrator
```

## Quick start

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Ttl": "00:05:00" },
        "OutputCache": { "Ttl": "00:01:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:00:30" }
      }
    }
  }
}
```

## Related packages

| Package | When |
|---------|------|
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Libraries / workers (`ICacheOrchestrator`) |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Hybrid instead of Fusion |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Shared Redis OC / Fusion L2 |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Multi-instance commands |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Invalidate after `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Getting started](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/getting-started.md)
- [Configuration](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/configuration.md)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
