# CacheOrchestrator.Redis

Optional **Redis** backends for [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/).

| | |
|--|--|
| **Provides** | Output Cache store, FusionCache L2 + backplane, Redis health probe |
| **Requires** | `CacheOrchestrator` core package + `AddRedisBackend()` |
| **Full docs** | **[GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)** · [backends.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/backends.md) |

This package owns Redis connection options (not the core package).

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.Redis
```

## Register

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;

builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddRedisBackend();
});
```

## Configure

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "Redis" },
    "FusionCacheInstances": {
      "default": { "Provider": "Redis" }
    },
    "Redis": {
      "Configuration": "localhost:6379"
    }
  }
}
```

| Surface | Override | Fallback |
|---------|----------|----------|
| Output Cache | `Cache:OutputCache:Redis` | `Cache:Redis` |
| Fusion instance `{name}` | `Cache:FusionCacheInstances:{name}:Redis` | `Cache:Redis` |

More (multi-instance, backplane, custom backends): [documentation on GitHub](https://github.com/amarinsek/CacheOrchestrator/tree/main/docs).

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
