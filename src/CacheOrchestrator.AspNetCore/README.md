# CacheOrchestrator.AspNetCore

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package is the **ASP.NET Core host** layer: Output Cache domain policies, Client Cache headers, Admin API, vary rules, and HTTP **`IDomainDataCache`** (a thin projection over Core `ICacheOrchestrator`). It depends on **Core** only. You still need a Data Cache provider package (Fusion or Hybrid) unless you use Output Cache alone.

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

Without identity bindings, Output Cache applies to **GET/HEAD** with Url identity. For other methods (or a custom GET key), use `.WithCacheIdentity` / `[CacheIdentity]` or `.WithContentHashCacheIdentity` / `[ContentHashCacheIdentity]` (`CacheOrchestrator.Identity`). Register named contracts with `AddCacheIdentityContract<T>()`. Docs: [endpoint cache identity](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/cache-identity.md).

For a single NuGet reference that already includes AspNetCore + Fusion, see **CacheOrchestrator**.

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Output Cache](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/output-cache.md)
- [Endpoint cache identity](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/cache-identity.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
