# CacheOrchestrator.Redis

Redis backends for CacheOrchestrator: Output Cache store, Fusion data-cache **L2**, Fusion **backplane**, and a connection health probe.

Add this when several instances must share OC payloads and/or Fusion L2 data.

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.Redis
```

## Quick start

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddRedisBackend());
```

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": { "default": { "Provider": "Redis" } },
    "Redis": { "Configuration": "localhost:6379" }
  }
}
```

Default connection: `Cache:Redis`. Overrides: `Cache:OutputCache:Redis`, `Cache:DataCacheInstances:{name}:Redis`.

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Backends](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/backends.md)
- [Topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
