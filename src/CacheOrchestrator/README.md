# CacheOrchestrator

**CacheOrchestrator** is domain-based caching for ASP.NET Core: define rules once per domain in configuration, then apply them on endpoints with a single attribute or extension. It orchestrates Output Cache (OC), FusionCache (L1/L2), and client Cache-Control (CC) under the same model.

The package targets **.NET 8** and **.NET 10**.

## Install

```bash
dotnet add package CacheOrchestrator
```

Related packages, when you need them:

- [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) — Redis for Output Cache and FusionCache L2 / backplane
- [CacheOrchestrator.Bus](https://www.nuget.org/packages/CacheOrchestrator.Bus/) — invalidate, Version, and TTL commands across instances
- [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) — the cache follows your EF Core saves

## Configure

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

## Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();
```

## Apply

```csharp
app.MapGet("/api/products", async (HttpContext http, IDomainFusionCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, LoadProductsAsync);
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

On a controller, use `[CacheDomain("catalog")]` and inject `IDomainFusionCache` in the same way.

## Documentation

- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)
- [Getting started](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/getting-started.md)
- [Guide](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/README.md)
- [Documentation index](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/README.md)
- [Minimal sample](https://github.com/amarinsek/CacheOrchestrator/tree/main/samples/CacheOrchestrator.Minimal)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
