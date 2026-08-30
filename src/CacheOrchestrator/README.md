# CacheOrchestrator

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This **meta** package is the usual starting point for web apps: it includes **`CacheOrchestrator.AspNetCore`** + **`CacheOrchestrator.FusionCache`**.

Targets **.NET 8** and **.NET 10**.

## Install

```bash
dotnet add package CacheOrchestrator --prerelease
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

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);

var app = builder.Build();
app.UseCacheOrchestrator();

app.MapGet("/api/products/{id:int}", async (HttpContext http, int id, IDomainDataCache cache) =>
{
    var data = await cache.GetOrSetAsync(http, ct => LoadProductAsync(id, ct));
    return Results.Json(data);
})
.CacheOutputWithDomain("catalog");
```

More layouts (Redis, Hybrid, libraries, EF): [packages.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md).

## Documentation

- [README](https://github.com/amarinsek/CacheOrchestrator/blob/main/README.md)
- [Documentation index](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
