# CacheOrchestrator.FusionCache

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package registers ZiggyCreatures **FusionCache** as the **`IDataCacheProvider`**. It wires named engines from `DataCacheInstances` and owns nested JSON **`FusionCache`** settings (hard TTL, fail-safe, factory timeouts, …). Portable TTL stays under **`DataCache`**.

## Install

```bash
dotnet add package CacheOrchestrator.FusionCache --prerelease
```

## Configuration

```json
{
  "Cache": {
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 },
        "FusionCache": {
          "HardTtlSeconds": 600,
          "FailSafeSeconds": 3600
        }
      }
    }
  }
}
```

## Usage

In an HTTP-free worker, pair it with Core:

```bash
dotnet add package CacheOrchestrator.Core --prerelease
dotnet add package CacheOrchestrator.FusionCache --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
```

In an ASP.NET Core host, pair with AspNetCore for Output Cache and HTTP helpers:

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
dotnet add package CacheOrchestrator.FusionCache --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
```

For a typical web app, the `CacheOrchestrator` meta package already includes AspNetCore and FusionCache.

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Data Cache / Fusion](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/data-cache.md) (Fusion section)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
