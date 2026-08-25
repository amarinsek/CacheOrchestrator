# CacheOrchestrator.Redis

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

This package adds **Redis** backends: Output Cache store, Fusion data-cache **L2**, Fusion **backplane**, and a connection health probe. Use it when several app instances must share cache data.

## Install

```bash
dotnet add package CacheOrchestrator.Redis --prerelease
```

## Config

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "Redis" } },
    "Redis": { "Configuration": "localhost:6379" }
  }
}
```

Default connection: `Cache:Redis`. Overrides: `Cache:OutputCache:Redis`, `Cache:DataCacheInstances:{name}:Redis`. Set `"OutputCache": { "Provider": "Redis" }` to store full HTTP responses in Redis as well.

## Example

```bash
dotnet add package CacheOrchestrator --prerelease
dotnet add package CacheOrchestrator.Redis --prerelease
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/3.0.0-beta.2) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/3.0.0-beta.2) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/3.0.0-beta.2) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/3.0.0-beta.2) | FusionCache data-cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/3.0.0-beta.2) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/3.0.0-beta.2) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/3.0.0-beta.2) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Backends](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/backends.md)
- [Topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
