# CacheOrchestrator.Core

[**CacheOrchestrator**](https://github.com/CacheOrchestrator/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package is the **HTTP-free core**: domain options, Version, portable `DataCache` policy, entity footprint/tags, **`ICacheOrchestrator`**, **`ICacheOrchestratorManagement`**, invalidation, and cluster **contracts**. Use it from class libraries and workers. It does not reference ASP.NET or a concrete cache engine.

## Install

```bash
dotnet add package CacheOrchestrator.Core --prerelease
```

A reusable library stops there. A standalone worker also installs one Data Cache provider, for example `CacheOrchestrator.FusionCache`.
Without a provider, startup and health remain valid for hosts that do not use Data Cache. The first actual Data Cache operation logs a one-time warning and runs its factory uncached.

## Configuration

The host binds domain policy (the library only consumes it via `ICacheOrchestrator`):

```json
{
  "Cache": {
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "Enabled": true, "TtlSeconds": 300 }
      }
    }
  }
}
```

## Usage

```csharp
builder.Services.AddCacheOrchestratorCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);

public sealed class CatalogService(ICacheOrchestrator cache)
{
    public ValueTask<ProductDto?> GetProductAsync(
        CacheDomainContext cacheDomain,
        int id,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            cacheDomain,
            logicalKey: $"product:{id}",
            async ct => await LoadProductAsync(id, ct),
            cancellationToken);
}
```

The worker installs `CacheOrchestrator.Core` plus one provider package. A reusable class library needs only Core; its host owns registration. Add `CacheOrchestrator.AspNetCore` only when the host needs Output Cache, Client Cache headers, or HTTP helpers.

## Documentation

- [README](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/README.md)
- [Documentation index](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/CacheOrchestrator/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/LICENSE.md)
