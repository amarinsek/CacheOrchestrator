# CacheOrchestrator.Redis

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) configures Output Cache, application **data cache**, and client `Cache-Control` under one **domain** model. It does not replace those systems or own a store.

This package adds **Redis** backends: Output Cache store, Fusion data-cache **L2**, Fusion **backplane**, and a connection health probe. Use it when several app instances must share cache data.

## Install

```bash
dotnet add package CacheOrchestrator.Redis
```

## Example

Typical web host with the meta package (or AspNetCore + Fusion) plus Redis:

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.Redis
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());
```

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

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) | Meta package (AspNetCore + Fusion) for typical web apps |
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Http-free domains and `ICacheOrchestrator` (libraries / workers) |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/) | FusionCache data-cache provider |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Microsoft HybridCache data-cache provider |
| [CacheOrchestrator.HttpBus](https://www.nuget.org/packages/CacheOrchestrator.HttpBus/) | Multi-instance invalidate / Version / settings bus |
| [CacheOrchestrator.EFCore.Invalidation](https://www.nuget.org/packages/CacheOrchestrator.EFCore.Invalidation/) | Invalidate after EF `SaveChanges` |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Backends](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/backends.md)
- [Topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
