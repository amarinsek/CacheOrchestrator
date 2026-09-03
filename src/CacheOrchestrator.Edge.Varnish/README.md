# CacheOrchestrator.Edge.Varnish

[**CacheOrchestrator**](https://github.com/CacheOrchestrator/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package integrates `CacheOrchestrator.Edge` with Varnish through the `xkey` VMOD. It emits opaque `xkey` response tags and sends protected `PURGE` requests containing `xkey-purge`.

```csharp
builder.Services.AddCacheOrchestratorEdge(
    builder.Configuration,
    edge => edge.AddVarnish());
```

Varnish requires the documented VCL contract to consume the edge TTL/grace headers, hide internal headers from clients, and authorize the PURGE route. Configure `Cache:EdgeInstances:{name}:Provider` as `Varnish` and set `Varnish:PurgeUrl`. `StaleWhileRevalidateSeconds` maps to Varnish grace; `StaleIfErrorSeconds` is rejected because Varnish does not offer the same portable, independently configurable semantics.

See the [Edge cache guide](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/docs/guide/edge.md) for configuration and VCL.

## License

MIT — [LICENSE.md](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/LICENSE.md)
