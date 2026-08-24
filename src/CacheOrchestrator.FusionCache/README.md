# CacheOrchestrator.FusionCache

ZiggyCreatures **FusionCache** as `IDataCacheProvider` for CacheOrchestrator. Registers named engines from `DataCacheInstances` and owns nested JSON **`FusionCache`** knobs (hard TTL, fail-safe, factory timeouts, …). Portable TTL stays under **`DataCache`**.

## Install

```bash
dotnet add package CacheOrchestrator.AspNetCore
dotnet add package CacheOrchestrator.FusionCache
```

Or use the meta package **CacheOrchestrator**, which already includes this package.

## Quick start

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
```

With meta: `AddCacheOrchestrator` already calls both.

## Related packages

| Package | Role |
|---------|------|
| [CacheOrchestrator.Core](https://www.nuget.org/packages/CacheOrchestrator.Core/) | Contracts |
| [CacheOrchestrator.HybridCache](https://www.nuget.org/packages/CacheOrchestrator.HybridCache/) | Alternative provider |
| [CacheOrchestrator.Redis](https://www.nuget.org/packages/CacheOrchestrator.Redis/) | Fusion L2 + backplane |

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [FusionCache provider](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/fusion-cache.md)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
