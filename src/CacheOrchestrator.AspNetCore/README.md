# CacheOrchestrator.AspNetCore

[**CacheOrchestrator**](https://github.com/CacheOrchestrator/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package is the HTTP host layer: Output Cache domain policies, Client Cache headers, Admin API, vary rules, and HTTP **`IDomainDataCache`** (a thin projection over `CacheOrchestrator.Core` `ICacheOrchestrator`). It depends on **`CacheOrchestrator.Core`** only. You still need a Data Cache provider package (`CacheOrchestrator.FusionCache` or `CacheOrchestrator.HybridCache`) unless you use Output Cache alone.

## Install

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
```

## Configuration

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "InMemory" } },
    "Domains": {
      "catalog": {
        "Version": "1",
        "DataCache": { "TtlSeconds": 300 },
        "OutputCache": { "TtlSeconds": 60 },
        "ClientCache": { "Cacheability": "Public", "TtlSeconds": 30 }
      }
    }
  }
}
```

## Usage

With Fusion as the data engine (install that package as well):

```bash
dotnet add package CacheOrchestrator.FusionCache --prerelease
dotnet add package CacheOrchestrator.AspNetCore --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id:int}", async (HttpContext http, int id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

Without `.CacheOutputWithDomain` / `[CacheDomain]`, Output Cache does not store (base policy is `NoCache`).

Without identity bindings, Output Cache applies to **GET/HEAD** with Url identity. For other methods (or a custom GET key), use `.WithCacheIdentity` / `[CacheIdentity]` or `.WithContentHashCacheIdentity` / `[ContentHashCacheIdentity]` (`CacheOrchestrator.Identity`). Register named contracts with `AddCacheIdentityContract<T>()`. Docs: [endpoint cache identity](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/docs/reference/cache-identity.md).

For a single NuGet reference that already includes `CacheOrchestrator.AspNetCore` + `CacheOrchestrator.FusionCache`, see the **`CacheOrchestrator`** meta package.

## Documentation

- [README](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/README.md)
- [Documentation index](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/CacheOrchestrator/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/LICENSE.md)
