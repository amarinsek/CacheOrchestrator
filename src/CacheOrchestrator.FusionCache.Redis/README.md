# CacheOrchestrator.FusionCache.Redis

Redis **L2** and **backplane** for CacheOrchestrator.FusionCache. Use from workers or libraries **without** referencing ASP.NET.

For Output Cache Redis, use `CacheOrchestrator.AspNetCore.Redis`. For both, use the meta package `CacheOrchestrator.Redis`.

## Install

This package is not yet published to nuget.org for the split line. Until then, reference the project from source or wait for the coordinated Redis-split release. When published:

```bash
dotnet add package CacheOrchestrator.FusionCache.Redis --prerelease
```

## Example

```csharp
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
builder.Services.AddRedisFusionCacheBackend(builder.Configuration);
```

## Related

- Meta: `CacheOrchestrator.Redis`
- Output Cache Redis: `CacheOrchestrator.AspNetCore.Redis`
- Support (transitive): `CacheOrchestrator.Redis.Shared` — do not install alone
