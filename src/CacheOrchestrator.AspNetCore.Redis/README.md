# CacheOrchestrator.AspNetCore.Redis

Redis **Output Cache** store for CacheOrchestrator. Use when you need shared HTTP response caching without taking the Fusion Redis L2 package.

For Fusion L2 / backplane, use `CacheOrchestrator.FusionCache.Redis`. For both, use the meta package `CacheOrchestrator.Redis`.

## Install

This package is not yet published to nuget.org for the split line. Until then, reference the project from source or wait for the coordinated Redis-split release. When published:

```bash
dotnet add package CacheOrchestrator.AspNetCore.Redis --prerelease
```

## Example

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(
    builder.Configuration,
    o => o.AddRedisOutputCacheBackend());
```

## Related

- Meta: `CacheOrchestrator.Redis`
- Fusion Redis: `CacheOrchestrator.FusionCache.Redis`
- Support (transitive): `CacheOrchestrator.Redis.Shared` — do not install alone
