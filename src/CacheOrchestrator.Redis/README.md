# CacheOrchestrator.Redis

Redis backends for [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/) — domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

This package adds: ASP.NET Core Output Cache store, FusionCache L2 (`IDistributedCache`) + StackExchange.Redis backplane, and health probes.

**All Redis connection settings live in this package** (not in the core `CacheOrchestrator` options types).

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
    o.AddRedisBackend(); // optional: o.AddRedisBackend("MyCache") if config section is not "Cache"
});
```

## Configure

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "Redis" },
    "FusionCacheInstances": {
      "default": { "Provider": "Redis" },
      "pii": {
        "Provider": "Redis",
        "Redis": { "Configuration": "secure-redis:6379" }
      }
    },
    "Redis": {
      "Configuration": "localhost:6379",
      "ConnectTimeout": 5000,
      "SyncTimeout": 5000,
      "KeepAliveSeconds": 60
    },
    "Distributed": {
      "SoftTimeoutSeconds": 1,
      "HardTimeoutSeconds": 2,
      "CircuitBreakerSeconds": 5
    }
  }
}
```

### Connection resolution

| Surface | Override section | Fallback |
|---------|------------------|----------|
| Output Cache | `Cache:OutputCache:Redis` | `Cache:Redis` |
| Fusion instance `{name}` | `Cache:FusionCacheInstances:{name}:Redis` | `Cache:Redis` |

L2 soft/hard/circuit timeouts are **core** settings under `Cache:Distributed` (provider-agnostic).

See the main [documentation](https://github.com/amarinsek/CacheOrchestrator/tree/main/docs).
