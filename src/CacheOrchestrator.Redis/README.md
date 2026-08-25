# CacheOrchestrator.Redis

[CacheOrchestrator](https://github.com/amarinsek/CacheOrchestrator) unifies the configuration of Output Cache, data cache, and client Cache-Control within a single domain model.

This is the **meta** Redis package: Output Cache store **and** Fusion data-cache L2 / backplane. Prefer it for typical web apps.

- Output Cache only: `CacheOrchestrator.AspNetCore.Redis`
- Fusion L2 only (no ASP.NET): `CacheOrchestrator.FusionCache.Redis`
- `CacheOrchestrator.Redis.Shared` is a **support** package pulled in transitively — do **not** install it alone.

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
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/3.0.0-beta.2) | Http-free domains and `ICacheOrchestrator` |
| [CacheOrchestrator.AspNetCore](https://www.nuget.org/packages/CacheOrchestrator.AspNetCore/3.0.0-beta.2) | Output Cache, Client Cache, Admin API, `IDomainDataCache` |
| [CacheOrchestrator.FusionCache](https://www.nuget.org/packages/CacheOrchestrator.FusionCache/3.0.0-beta.2) | FusionCache data-cache provider |
| `CacheOrchestrator.AspNetCore.Redis` | Redis Output Cache only (not yet on nuget.org for the split line) |
| `CacheOrchestrator.FusionCache.Redis` | Redis Fusion L2 only (not yet on nuget.org for the split line) |
| `CacheOrchestrator.Redis.Shared` | Support / transitive — do not install alone |

## Documentation

- [Packages](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md)
- [Getting started](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/getting-started.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
