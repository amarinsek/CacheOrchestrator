# CacheOrchestrator.HybridCache

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package registers Microsoft **HybridCache** as the **`IDataCacheProvider`**. It uses portable **`DataCache.TtlSeconds`** and the resolved Data Cache namespace. Fusion-specific options (fail-safe, hard TTL, factory timeouts) are not applied. HybridCache supports only `DataCacheInstances:default`; startup validation rejects named instances.

## Install

```bash
dotnet add package CacheOrchestrator.HybridCache --prerelease
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

## Configuration

```json
{
  "Cache": {
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 }
      }
    }
  }
}
```

## Usage

In an ASP.NET Core host:

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
dotnet add package CacheOrchestrator.HybridCache --prerelease
dotnet add package Microsoft.Extensions.Caching.Hybrid
```

```csharp
builder.Services.AddHybridCache();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorHybridCache();
```

Optional L2: configure HybridCache / `IDistributedCache` as usual (outside this package). Prefer **CacheOrchestrator.FusionCache** when you need fail-safe and the full Fusion surface.

Keys and tags are namespaced before they reach HybridCache, so applications can safely share a distributed store when their `Cache:Namespace` values differ.

## Documentation

- [README](https://github.com/amarinsek/CacheOrchestrator/blob/main/README.md)
- [Documentation index](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
