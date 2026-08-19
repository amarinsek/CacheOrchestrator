# Observability

Dashboards and multi-instance ops: [admin.md](admin.md). **Admin Console traffic stats are Prometheus-only** (`AdminConsole:Metrics` → `GET /api/stats/window`). Local Admin (`Cache:Admin:Enabled`) still exposes health, config, invalidate, Version/TTL, and an obsolete process-lifetime `GET …/stats` for diagnostics. Time series belong on the `CacheOrchestrator` meter (OpenTelemetry / Prometheus). Playground topology labs including Prometheus (sample-only Docker Compose, not a NuGet dependency): [samples/CacheOrchestrator.Sample/labs/README.md](../samples/CacheOrchestrator.Sample/labs/README.md).

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
| `cache_orchestrator.fc.requests` | Fusion ops by `domain`, `result` (`hit`/`miss`/`stale`/`fail`/`bypass`/`off`/`unresolved`; domain `_` when unresolved); optional `route` |
| `cache_orchestrator.factory.duration` | **Canonical** factory wall time (ms) on miss/stale/**fail**; optional `route` |
| `cache_orchestrator.factory.result_size` | Factory result size (bytes) when cheaply measurable on miss; optional `route` |
| `cache_orchestrator.fc.duration` | Legacy Fusion GetOrSet duration (ms) for any timed result; prefer `factory.duration` for factory cost |
| `cache_orchestrator.oc.requests` | Output outcomes by `domain`, `result`; optional `route` |
| `cache_orchestrator.client.schedule` | Client Cache Schedule by `domain`, `phase` |
| `cache_orchestrator.invalidate` | Successful full invalidations by `domain` |

**`route` tag** — when `Cache:Metrics:IncludeEndpointLabel` is `true` (default), OC/FC instruments add a stable endpoint key (`METHOD` + route template, same as Admin Console App endpoint rows). Never uses raw paths with resource ids. Set `false` to drop the tag (lower cardinality). Keep the setting consistent across instances. Domain labels always remain.
| `cache_orchestrator.cluster.commands_published` | Cluster bus origin publish (`command_type`) — [cluster-bus.md](cluster-bus.md) |
| `cache_orchestrator.cluster.commands_received` | Cluster commands accepted on receive path |
| `cache_orchestrator.cluster.commands_applied` | Cluster ApplyLocal success |
| `cache_orchestrator.cluster.publish_failures` | Per-peer publish failure (`reason`) |
| `cache_orchestrator.cluster.command_dedupe_hits` | Duplicate `CommandId` within dedupe window |

`phase` tag values match X-Cache: `calm`, `approaching`, `hold`, `n/a`.

Cluster instruments are silent when the bus is Null / disabled (no meaningful counters without publish/receive).

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
| Cluster peer publish failure / bus open without ApiKey | Warning |
| Cluster ignore (namespace / self / dedupe) | Debug |
| Unknown domain / missing Version | Warning |

Log categories use the implementing type names (internal): `DomainFusionCacheService`, `DomainCacheOptionsProvider`, `CacheOrchestratorInvalidator`, and public `DomainOutputCachePolicy`. Cluster bus: `HttpClusterCommandBus`, `DefaultClusterCommandHandler`, `CacheOrchestrator.Bus`.

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

Local Admin `GET …/health` is a **separate** endpoint: it still returns HTTP 200 when a registered cache probe fails, with `Healthy: false` (Admin Console maps that to **Degraded**). See [admin.md](admin.md#health-semantics-admin-console-app-mapping).

## Related

- [cluster-bus.md](cluster-bus.md) — multi-instance command bus metrics and endpoints  
- [architecture.md](architecture.md)  

