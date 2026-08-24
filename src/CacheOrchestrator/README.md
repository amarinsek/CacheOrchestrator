# CacheOrchestrator

**CacheOrchestrator** configures and coordinates three existing layers in ASP.NET Core — Output Cache (OC), FusionCache (L1/L2), and client Cache-Control (CC) — under one **domain** model. Define the rules once in configuration, then apply them on endpoints with a single attribute or extension. It does not replace those systems or own a store: ASP.NET still holds the HTTP response, FusionCache still holds the object, and the browser or CDN still honours `Cache-Control`.

The package targets **.NET 8** and **.NET 10**.

## Install

```bash
dotnet add package CacheOrchestrator
```

Related packages, when you need them:

- [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) — Redis for Output Cache and FusionCache L2 / backplane
- [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) — invalidate, Version, and TTL commands across instances
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
