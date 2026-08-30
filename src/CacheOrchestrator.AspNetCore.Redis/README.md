# CacheOrchestrator.AspNetCore.Redis

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package registers **Redis** as the ASP.NET Core **Output Cache** store. Use it when several instances must share full HTTP responses, without taking the Fusion Redis L2 package.

For Fusion L2 / backplane only, use **CacheOrchestrator.FusionCache.Redis**. For both surfaces, prefer the meta package **CacheOrchestrator.Redis**.

## Install

```bash
dotnet add package CacheOrchestrator.AspNetCore.Redis --prerelease
```

## Configuration

```json
{
  "Cache": {
    "OutputCache": { "Provider": "Redis" },
    "Redis": { "Configuration": "localhost:6379" }
  }
}
```

Default connection: `Cache:Redis`. Override: `Cache:OutputCache:Redis`.

## Usage

```bash
dotnet add package CacheOrchestrator.AspNetCore --prerelease
dotnet add package CacheOrchestrator.AspNetCore.Redis --prerelease
```

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(
    builder.Configuration,
    o => o.AddRedisOutputCacheBackend());
```

Add a Data Cache provider separately when endpoints use Data Cache. For Output Cache **and** Fusion Redis L2 in one reference, use `CacheOrchestrator.Redis`.

## Documentation

- [README](https://github.com/amarinsek/CacheOrchestrator/blob/main/README.md)
- [Documentation index](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
