# CacheOrchestrator.Redis

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This is the **meta** Redis package: Output Cache store **and** Fusion Data Cache L2 / backplane. Prefer it for typical web apps.

- Output Cache only: `CacheOrchestrator.AspNetCore.Redis`
- Fusion L2 only (no ASP.NET): `CacheOrchestrator.FusionCache.Redis`
- `CacheOrchestrator.Redis.Shared` is a **support** package pulled in transitively — do **not** install it alone.

## Install

```bash
dotnet add package CacheOrchestrator.Redis --prerelease
```

## Configuration

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

## Usage

```bash
dotnet add package CacheOrchestrator --prerelease
dotnet add package CacheOrchestrator.Redis --prerelease
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());

var app = builder.Build();
app.UseCacheOrchestrator();
```

## Documentation

- [README](https://github.com/amarinsek/CacheOrchestrator/blob/main/README.md)
- [Documentation index](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
