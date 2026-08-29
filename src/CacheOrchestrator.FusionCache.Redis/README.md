# CacheOrchestrator.FusionCache.Redis

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package registers **Redis** as FusionCache **L2** and **backplane** for named `DataCacheInstances`. Use it from web hosts or workers **without** referencing ASP.NET.

For Output Cache Redis only, use **CacheOrchestrator.AspNetCore.Redis**. For both surfaces, prefer the meta package **CacheOrchestrator.Redis**.

## Install

```bash
dotnet add package CacheOrchestrator.FusionCache.Redis --prerelease
```

## Configuration

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

## Usage

In an HTTP-free worker, install Core, FusionCache, and this Redis backend:

```bash
dotnet add package CacheOrchestrator.Core --prerelease
dotnet add package CacheOrchestrator.FusionCache --prerelease
dotnet add package CacheOrchestrator.FusionCache.Redis --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
builder.Services.AddRedisFusionCacheBackend(builder.Configuration);
```

In an ASP.NET Core host, replace the Core registration with the ASP.NET Core host layer:

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
builder.Services.AddRedisFusionCacheBackend(builder.Configuration);
```

For Output Cache **and** Fusion Redis L2 in one reference, use `CacheOrchestrator.Redis`.

## Documentation

- [Backends](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/backends.md)
- [Data Cache](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/data-cache.md)
- [Packages](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
