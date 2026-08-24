# Observability

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — operations](guide/operations.md). Catalog: [documentation index](README.md).

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
X-Cache: domain=products; client=public; phase=approaching; oc=miss; dc=hit; ms=12
```

| Token | Meaning |
|-------|---------|
| `domain` | Normalized domain |
| `client` | `public` / `private` / `no-store` / `blocked` |
| `phase` | Client Cache Schedule: `calm` / `approaching` / `hold` / `n/a` |
| `oc` | Output Cache: `hit` / `miss` / `bypass` / `off` |
| `dc` | Data cache result (omitted on OC `hit`) |
| `fa` | `run` when `dc` is present and is not a fresh hit (factory callback ran). Omitted on OC `hit` and on `dc=hit`. |
| `ms` | Data-cache elapsed ms (omitted on OC `hit`) |

When the header is emitted, `phase` is always present on responses that go through the policy header path (same wire values as metrics tags). `fa=run` matches Admin FA run: every non-hit Fusion disposition still invokes the factory (`miss` / `stale` / `bypass` / `off` / `unresolved`). There is no `dc=fail` on the header — a hard factory throw is meter `result=fail` only.

## Metrics

Meter name: **`CacheOrchestrator`**

| Instrument | Description |
|------------|-------------|
| `cache_orchestrator.dc.requests` | Data-cache ops by `domain`, `result` (`hit`/`miss`/`stale`/`fail`/`bypass`/`off`/`unresolved`; domain `_` when unresolved); optional `route` |
| `cache_orchestrator.factory.duration` | **Canonical** factory wall time (ms) whenever the factory callback ran (`miss` / `stale` / `fail` / `off` / `unresolved` / `bypass`); optional `route` |
| `cache_orchestrator.factory.result_size` | Factory result size (bytes) when cheaply measurable on a successful factory (`miss` / `off` / `unresolved` / `bypass`); optional `route`. Independent of `Cache:Admin:TrackResultSize` (that flag only fills Admin `/stats` sums). |
| `cache_orchestrator.dc.duration` | Legacy data-cache get-or-set duration (ms) for any timed result; prefer `factory.duration` for factory cost |
| `cache_orchestrator.oc.requests` | Output outcomes by `domain`, `result` (`hit`/`miss`/`bypass`/`off`); optional `route` |
| `cache_orchestrator.client.schedule` | Client Cache Schedule by `domain`, `phase` |
| `cache_orchestrator.invalidate` | Successful full invalidations by `domain` (domain-only label). Optional low-cardinality `kind` tag: `Domain` / `Entity` / `EntityKind`. Not recorded for raw `InvalidateTagsAsync`. |
| `cache_orchestrator.cluster.commands_published` | Cluster bus origin publish (`command_type`) — [cluster-bus.md](cluster-bus.md) |
| `cache_orchestrator.cluster.commands_received` | Cluster commands accepted on receive path |
| `cache_orchestrator.cluster.commands_applied` | Cluster ApplyLocal success |
| `cache_orchestrator.cluster.publish_failures` | Per-peer publish failure (`reason`) |
| `cache_orchestrator.cluster.command_dedupe_hits` | Duplicate `CommandId` within dedupe window |

**`route` tag** — when `Cache:Metrics:IncludeEndpointLabel` is `true` (default), OC/DC instruments add a stable endpoint key (`METHOD` + route template, same as Admin Console App endpoint rows). Never uses raw paths with resource ids. Set `false` to drop the tag (lower cardinality). Keep the setting consistent across instances. Domain labels always remain.

`phase` tag values match X-Cache: `calm`, `approaching`, `hold`, `n/a`.

Cluster instruments are silent when the bus is Null / disabled (no meaningful counters without publish/receive).

Subscribe with OpenTelemetry / any `MeterListener`.

## Activities (tracing)

Activity source name: **`CacheOrchestrator`**

| Activity | When |
|----------|------|
| `cache.dc.get_or_set` | Data-cache get/set path |
| `cache.oc.hit` | Output cache hit |
| `cache.invalidate` | Domain invalidation |

Wire names use the same short layer ids as `X-Cache` / metrics (`dc`, `oc`). Data-cache activities tag `domain` and `cache.result` (including `unresolved` / `off` / `bypass`), plus `entity_kind` / `resource_id` when set. Invalidate activities use `cache.scope`, `cache.kind`, `cache.tags`, `cache.dc.ok`, `cache.oc.ok` — not `cache.result`. Failure events: `dc.invalidate.failed`, `oc.invalidate.failed`.

## Logging

| Component | Typical levels |
|-----------|----------------|
| Data-cache HIT/MISS | Debug |
| Data-cache no domain (uncached factory) | Warning |
| Data-cache STALE / errors | Information / Warning |
| Invalidation start | Information |
| Invalidation partial failure | Warning |
| Cluster peer publish failure / bus open without ApiKey | Warning |
| Cluster ignore (namespace / self / dedupe) | Debug |
| Unknown domain / missing Version | Warning |

Log categories use the implementing type names (internal): `DomainDataCacheService`, `DomainCacheOptionsProvider`, `CacheOrchestratorInvalidator`, and public `DomainOutputCachePolicy`. Cluster bus: `HttpClusterCommandBus`, `DefaultClusterCommandHandler`, `CacheOrchestrator.HttpBus`.

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

- [Guide — operations](guide/operations.md)  
- [cluster-bus.md](cluster-bus.md) — multi-instance command bus metrics and endpoints  
- [architecture.md](architecture.md)  

