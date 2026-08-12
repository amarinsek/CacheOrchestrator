# Observability

For multi-instance **Admin App** dashboards, Local Admin health, and live counter aggregation, see [admin.md](admin.md).

## X-Cache response header

Written by `DomainOutputCachePolicy` on response start via `XCacheHeaderFormatter`, when
**`Cache:EmitDiagnosticsHeaders`** is `true` (the default).

### Controlling emission

```json
{
  "Cache": {
    "EmitDiagnosticsHeaders": true
  }
}
```

| Value | Behaviour |
|-------|-----------|
| `true` (default) | Clients receive `X-Cache` (and any future diagnostic response headers under this flag) |
| `false` | No diagnostic response headers; **Cache-Control**, ETag, metrics, activities, and logs still work |

Use `false` in production if you prefer not to expose domain names, hit/miss status, schedule phase, or timing to untrusted clients. Server-side observability (meter `CacheOrchestrator`, activity source `CacheOrchestrator`) is independent of this switch.

Example when enabled:

```http
X-Cache: domain=products; client=public; phase=approaching; output=miss; data=hit; ms=12
```

| Token | Meaning |
|-------|---------|
| `domain` | Normalized domain |
| `client` | `public` / `private` / `no-store` / `blocked` |
| `phase` | Client Cache Schedule: `calm` / `approaching` / `hold` / `n/a` |
| `output` | Output Cache: `hit` / `miss` / `bypass` |
| `data` | Fusion result (omitted on output `hit`) |
| `ms` | Fusion elapsed ms (omitted on output `hit`) |

When the header is emitted, `phase` is always present on responses that go through the policy header path (same wire values as metrics tags).

## Metrics

Meter name: **`CacheOrchestrator`**

| Instrument | Description |
|------------|-------------|
| `cache_orchestrator.fc.requests` | Fusion ops by `domain`, `result` (`hit`/`miss`/`stale`/`bypass`/`off`/`unresolved`; domain `_` when unresolved) |
| `cache_orchestrator.fc.duration` | Fusion duration (ms) |
| `cache_orchestrator.oc.requests` | Output outcomes by `domain`, `result` |
| `cache_orchestrator.client.schedule` | Client Cache Schedule by `domain`, `phase` |
| `cache_orchestrator.invalidate` | Successful full invalidations by `domain` |

`phase` tag values match X-Cache: `calm`, `approaching`, `hold`, `n/a`.

Subscribe with OpenTelemetry / any `MeterListener`.

## Activities (tracing)

Activity source name: **`CacheOrchestrator`**

| Activity | When |
|----------|------|
| `cache.fusion.get_or_set` | Fusion get/set path |
| `cache.output.hit` | Output cache hit |
| `cache.invalidate` | Domain invalidation |

Tags include `domain`, `cache.result`, and success flags on invalidate.

## Logging

| Component | Typical levels |
|-----------|----------------|
| Fusion HIT/MISS | Debug |
| Fusion no domain (uncached factory) | Warning |
| Fusion STALE / errors | Information / Warning |
| Invalidation start | Information |
| Invalidation partial failure | Warning |
| Unknown domain / missing Version | Warning |

Log categories use the implementing type names (internal): `DomainFusionCacheService`, `DomainCacheOptionsProvider`, `CacheOrchestratorInvalidator`, and public `DomainOutputCachePolicy`.

## Health checks

```csharp
builder.Services.AddHealthChecks()
    .AddCacheOrchestrator(); // name: cache_orchestrator
```

- Runs health probes registered by the active backend providers (via `ICacheBackendRegistrar.RegisterHealthProbes`)
- Redis backend registers a probe that pings `IConnectionMultiplexer`  
- InMemory registers no external probe (healthy if none registered)  
- Custom backends (e.g., SQL Server) can register their own specific database probes
- Default timeout 3s; failure status default `Degraded`  

## Related

- [architecture.md](architecture.md)  
