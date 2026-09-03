# CacheOrchestrator.Edge.Cloudflare

[**CacheOrchestrator**](https://github.com/CacheOrchestrator/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package is the Cloudflare provider for `CacheOrchestrator.Edge`. It emits `Cache-Tag` and `Cloudflare-CDN-Cache-Control`, then purges matching opaque tags through the Cloudflare API outside the request/invalidation path.

## Install and register

```bash
dotnet add package CacheOrchestrator.Edge.Cloudflare --prerelease
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
builder.Services.AddCacheOrchestratorEdge(
    builder.Configuration,
    edge => edge.AddCloudflare());
```

Configure `Cache:EdgeInstances:{name}:Provider` as `Cloudflare`, supply the nested `Cloudflare:ZoneId` and `Cloudflare:ApiToken` from secret configuration, and enable `Edge` on selected domains. The provider supports edge `TtlSeconds`, `StaleWhileRevalidateSeconds`, and `StaleIfErrorSeconds` without changing browser `Cache-Control`.

## Documentation

- [Cloudflare setup and behavior](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/docs/guide/edge.md)
- [Configuration reference](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/docs/reference/configuration.md)
- [Repository](https://github.com/CacheOrchestrator/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/LICENSE.md)
