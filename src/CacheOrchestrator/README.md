# CacheOrchestrator

**CacheOrchestrator** configures and coordinates Output Cache (OC), **data cache** (DC — FusionCache or HybridCache), and client Cache-Control (CC) under one **domain** model. This meta package pulls **AspNetCore + FusionCache** for typical web apps.

It does not own a store: ASP.NET holds the HTTP response, FusionCache holds the object, and the browser or CDN honours `Cache-Control`.

Targets **.NET 8** and **.NET 10**.

## Install

```bash
dotnet add package CacheOrchestrator
```

Compose other packages when needed — [packages and composition](../../docs/packages.md):

- [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) — `ICacheOrchestrator`, domains (libraries)
- [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) — Hybrid instead of Fusion
- [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) — Redis OC / Fusion L2
- [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) — cluster commands
- [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) — invalidate after `SaveChanges`

## Configure

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Ttl": "00:05:00" },
        "OutputCache": { "Ttl": "00:02:00" },
        "ClientCache": { "Cacheability": "Public", "Ttl": "00:01:00" }
      }
    }
  }
}
```

## Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
var app = builder.Build();
app.UseCacheOrchestrator();
```

## Apply

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Libraries can inject `ICacheOrchestrator` from Core instead of `IDomainDataCache`.

Docs: [getting started](../../docs/getting-started.md) · [packages](../../docs/packages.md) · [configuration](../../docs/configuration.md).
