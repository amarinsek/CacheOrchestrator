# CacheOrchestrator.HttpBus

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model. It ensures seamless coordination and cache invalidation across all layers while significantly reducing boilerplate code.

This package is the HTTP **cluster command bus**: it delivers invalidate, Version, and settings patches to every configured peer. Use it when you run more than one instance and need those **commands** everywhere (it does not share Redis cache payloads by itself).

## Install

```bash
dotnet add package CacheOrchestrator.HttpBus --prerelease
```

## Config

```json
{
  "Cache": {
    "Namespace": "app1",
    "InstanceId": "app1-a",
    "Cluster": {
      "Bus": {
        "Enabled": true,
        "Membership": "Static",
        "ApiKey": "…",
        "Static": {
          "Instances": [
            { "Id": "app1-a", "Url": "http://10.0.0.1:8080" },
            { "Id": "app1-b", "Url": "http://10.0.0.2:8080" }
          ]
        }
      }
    }
  }
}
```

`Membership` may also be `ServiceDiscovery`. Peers authenticate `POST …/cluster/apply` with `X-Cache-Admin-Key` (`Cache:Cluster:Bus:ApiKey`, or `Cache:Admin:ApiKey` if empty).

## Example

```bash
dotnet add package CacheOrchestrator --prerelease
dotnet add package CacheOrchestrator.HttpBus --prerelease
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddHttpClusterBus());

var app = builder.Build();
app.UseCacheOrchestrator();
app.MapCacheOrchestratorHttpBus();

app.MapGet("/api/products/{id}", async (HttpContext http, string id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Invalidate / Version / settings from Admin or `ICacheOrchestratorInvalidator` are then delivered to peers over the bus.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/3.0.0-beta.2) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/3.0.0-beta.2) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/3.0.0-beta.2) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/3.0.0-beta.2) | FusionCache data-cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/3.0.0-beta.2) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/3.0.0-beta.2) | Redis Output Cache store / Fusion L2 / backplane |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/3.0.0-beta.2) | Invalidate after EF `SaveChanges` |

## Documentation

- [Cluster bus](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/cluster-bus.md)
- [Topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md)
- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
