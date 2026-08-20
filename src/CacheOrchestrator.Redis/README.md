# CacheOrchestrator.Redis

Redis backends for [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/).

Add this package when several application instances must share cache data. You get Redis as the Output Cache store, as FusionCache L2, and as the Fusion backplane, plus a health probe for the connection.

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.Redis
```

## Register

```csharp
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

A connection under `Cache:Redis` is the default. Output Cache may use `Cache:OutputCache:Redis`; a named Fusion instance may use `Cache:FusionCacheInstances:{name}:Redis`.

Orientation: [Guide — topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md). Reference: [backends.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/backends.md). Overview: [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme).

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
