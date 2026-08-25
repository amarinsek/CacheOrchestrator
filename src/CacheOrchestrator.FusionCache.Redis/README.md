# CacheOrchestrator.FusionCache.Redis

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

This package registers **Redis** as FusionCache **L2** and **backplane** for named `DataCacheInstances`. Use it from web hosts or workers **without** referencing ASP.NET.

For Output Cache Redis only, use **CacheOrchestrator.AspNetCore.Redis**. For both surfaces, prefer the meta package **CacheOrchestrator.Redis**.

## Install

Available on nuget.org **from release 3.0.0-beta.3** onwards (until then, reference the project from source):

```bash
dotnet add package CacheOrchestrator.FusionCache.Redis --prerelease
```

## Config

```json
{
  "Cache": {
    "DataCacheInstances": { "default": { "Provider": "Redis" } },
    "Redis": { "Configuration": "localhost:6379" },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 }
      }
    }
  }
}
```

Default connection: `Cache:Redis`. Override per instance: `Cache:DataCacheInstances:{name}:Redis`.

## Example

Worker / library-style host (no ASP.NET Output Cache):

```bash
dotnet add package CacheOrchestrator.FusionCache --prerelease
dotnet add package CacheOrchestrator.FusionCache.Redis --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
builder.Services.AddRedisFusionCacheBackend(builder.Configuration);

// resolve ICacheOrchestrator from DI, then:
var product = await cacheOrchestrator.GetOrCreateAsync(
    new CacheDomainContext("catalog"),
    logicalKey: $"product:{id}",
    ct => LoadProductAsync(id, ct),
    cancellationToken);
```

With ASP.NET Core for Output Cache and HTTP helpers (OC store can stay InMemory):

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
dotnet add package CacheOrchestrator.FusionCache --prerelease
dotnet add package CacheOrchestrator.FusionCache.Redis --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
builder.Services.AddRedisFusionCacheBackend(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

For Output Cache **and** Fusion Redis L2 in one reference, see **CacheOrchestrator.Redis**.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/3.0.0-beta.2) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/3.0.0-beta.2) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/3.0.0-beta.2) | FusionCache data-cache provider |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/3.0.0-beta.2) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/3.0.0-beta.2) | Meta Redis (OC + Fusion L2) |
| `CacheOrchestrator.AspNetCore.Redis` | Redis Output Cache only (from **3.0.0-beta.3**) |
| `CacheOrchestrator.Redis.Shared` | Support / transitive — do not install alone |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/3.0.0-beta.2) | Multi-instance invalidate / Version / settings bus |

## Documentation

- [Backends](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/backends.md)
- [Data cache](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/data-cache.md)
- [Packages](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
