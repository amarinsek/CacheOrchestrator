# Observability

> **Reference** — `X-Cache`, metrics, traces, logs, and health checks.

How to see what the cache is doing: the **`X-Cache`** response header, the **`CacheOrchestrator`** meter and activity source, logs, and health checks.

Operator dashboards and multi-instance actions: [admin](admin.md). Admin Console App **traffic** charts are **Prometheus-only** (point `AdminConsole:Metrics` at a scrape of the meter). Try Prometheus with the playground [topology labs](../../samples/CacheOrchestrator.Sample/labs/README.md) — sample Compose only, not a NuGet dependency.

## Table of Contents

- [X-Cache response header](#x-cache-response-header)
- [Metrics](#metrics)
- [Activities (tracing)](#activities-tracing)
- [Logging](#logging)
- [Health checks](#health-checks)
- [Security checklist](#security-checklist)

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
X-Cache: domain=products; version=v1; client=public; phase=approaching; oc=miss; dc=hit; ms=12
```

| Token | Wire values | Presence / meaning |
|-------|-------------|--------------------|
| `domain` | normalized name, or `_` | Always present. `_` when the domain could not be resolved (fail-closed path). |
| `version` | domain `Version` stamp, or `-` | Always present. `-` when unresolved. |
| `client` | `public` / `private` / `no-store` / `blocked` | Always present. Client Cache-Control class applied to the response. |
| `phase` | `calm` / `approaching` / `hold` / `n/a` | Always present. Same wire values as metrics `phase` tags. |
| `oc` | `hit` / `miss` / `bypass` / `off` | Always present. Output Cache outcome. |
| `dc` | `hit` / `miss` / `stale` / `bypass` / `off` / `unresolved` / `n/a` | Present unless `oc=hit`. `n/a` when no Data Cache operation ran. |
| `fa` | `run` | Present when application/origin work produced the result: direct generation (`dc=n/a`) and every non-hit Data Cache disposition (`miss` / `stale` / `bypass` / `off` / `unresolved`). Omitted on `oc=hit` and on `dc=hit`. Matches Admin FA run. |
| `ms` | integer milliseconds | Wall-clock of the timed server path when measured: Data Cache get-or-set (including L1/L2 `dc=hit`), or direct origin when `dc=n/a`. Omitted on Output Cache `oc=hit`. Use metric `cache_orchestrator.factory.duration` for factory/origin cost. |

A hard factory throw is recorded by factory failure telemetry (`result=fail` on metrics) and the exception propagates;

`dc=stale` has a deliberately narrow meaning: CacheOrchestrator observed a factory failure in this request and the provider returned its fail-safe value. `dc=hit` does not promise that provider-specific eager-refresh or timeout metadata says the value is fresh; the current provider contract does not expose a reliable stale signal for every background-refresh path.

An empty or unconfigured **domain name template** / resolver result fails closed with `Cache-Control: no-store`. When diagnostics are enabled, `X-Cache` uses `domain=_; version=-; client=no-store; phase=n/a; oc=bypass; dc=n/a; fa=run` and never echoes the unresolved value.

## Metrics

Meter name: **`CacheOrchestrator`**

| Instrument | Description |
|------------|-------------|
| `cache_orchestrator.dc.requests` | Data Cache ops by `domain`, `result` (`hit`/`miss`/`stale`/`fail`/`bypass`/`off`/`unresolved`; domain `_` when unresolved); optional `route` |
| `cache_orchestrator.factory.runs` | Canonical factory/origin executions by `domain`; optional `route`. Includes direct endpoints with no Data Cache operation. |
| `cache_orchestrator.factory.failures` | Factory/origin failures by `domain`; optional `route` |
| `cache_orchestrator.factory.duration` | **Canonical** factory/origin wall time (ms), including direct `dc=n/a` execution; optional `route` |
| `cache_orchestrator.factory.result_size` | Factory result size (bytes) when cheaply measurable on a successful factory (`miss` / `off` / `unresolved` / `bypass`); optional `route`. Independent of `Cache:Admin:TrackResultSize` (that flag only fills Admin `/stats` sums). |
| `cache_orchestrator.dc.duration` | Data Cache get-or-set duration (ms) for any timed result; use `factory.duration` for factory cost |
| `cache_orchestrator.oc.requests` | Output outcomes by `domain`, `result` (`hit`/`miss`/`bypass`/`off`/`unresolved`; domain `_` when unresolved); optional `route` |
| `cache_orchestrator.client.schedule` | Client Cache Schedule by `domain`, `phase` |
| `cache_orchestrator.invalidate` | Successful full invalidations by `domain` (domain-only label). Optional low-cardinality `kind` tag: `Domain` / `Entity` / `EntityKind`. Not recorded for raw `InvalidateTagsAsync`. |
| `cache_orchestrator.cluster.commands_published` | Cluster bus origin publish (`command_type`) — [cluster-bus.md](cluster-bus.md) |
| `cache_orchestrator.cluster.commands_received` | Cluster commands accepted on receive path |
| `cache_orchestrator.cluster.commands_applied` | Cluster ApplyLocal success |
| `cache_orchestrator.cluster.publish_failures` | Per-peer publish failure (`reason`) |
| `cache_orchestrator.cluster.command_dedupe_hits` | Duplicate `CommandId` within dedupe window |

**`route` tag** — when `Cache:Metrics:IncludeEndpointLabel` is `true` (default), Output Cache and Data Cache instruments add a stable endpoint key (`METHOD` + route template, the same shape as Admin Console App endpoint rows). It never uses raw paths with resource ids. Set `false` to drop the tag and lower cardinality. Keep the setting consistent across instances. Domain labels always remain.

`phase` tag values match X-Cache: `calm`, `approaching`, `hold`, `n/a`.

Cluster instruments are silent when the bus is Null / disabled (no meaningful counters without publish/receive).

Subscribe with OpenTelemetry / any `MeterListener`.

## Activities (tracing)

Activity source name: **`CacheOrchestrator`**

| Activity | When |
|----------|------|
| `cache.orchestrator.get_or_create` | Core `ICacheOrchestrator.GetOrCreateAsync` provider call |
| `cache.orchestrator.get_or_create_footprint` | Core entity-footprint provider call |
| `cache.dc.get_or_set` | Data Cache get/set path |
| `cache.oc.hit` | Output Cache hit |
| `cache.invalidate` | Domain invalidation |

Core activities tag `domain`, `provider`, and `cache.result`; the footprint activity distinguishes `hit` from `miss`. Wire names for the HTTP path use the same short layer ids as `X-Cache` and metrics (`dc`, `oc`). Data Cache activities tag `domain` and `cache.result` (including `unresolved`, `off`, and `bypass`), plus `entity_kind` / `resource_id` when set. Invalidation activities use `cache.scope`, `cache.kind`, `cache.tags`, `cache.dc.ok`, and `cache.oc.ok`, not `cache.result`. Failure events are `dc.invalidate.failed` and `oc.invalidate.failed`.

## Logging

| Component | Typical levels |
|-----------|----------------|
| Data Cache HIT/MISS | Debug |
| Data Cache no domain (uncached factory) | Warning |
| Data Cache STALE / errors | Information / Warning |
| Invalidation start | Information |
| Invalidation partial failure | Warning |
| Cluster peer publish failure / explicitly unauthenticated bus | Warning |
| Data Cache operation without a provider | One-time warning on first use; factory runs uncached |
| Cluster ignore (namespace / self / dedupe) | Debug |
| Unknown domain / missing Version | Warning |

Useful categories include `DomainOutputCachePolicy` and the Data Cache / invalidation services. Cluster bus categories live in the HttpBus package (`HttpClusterCommandBus`, …).

## Health checks

```csharp
builder.Services.AddHealthChecks()
    .AddCacheOrchestrator(
        name: "cache_orchestrator",
        failureStatus: HealthStatus.Degraded,
        timeout: TimeSpan.FromSeconds(3),
        tags: ["cache", "ready"]);
```

All arguments shown are the defaults. `timeout` is the health-check registration timeout for the combined probe run. Register additional provider or application probes as `ICacheOrchestratorHealthProbe`; each has a stable `Name` and throws when its dependency is unavailable.

- Runs health probes registered by active backend providers through their registration context
- Redis backend registers a probe that pings `IConnectionMultiplexer`  
- The Null provider is reported as `data_cache_provider: Null` but does not make health fail; this is a valid Output-Cache-only composition
- InMemory registers no external probe (healthy if none registered)  
- Custom backends (e.g., SQL Server) can register their own specific database probes
- Default timeout 3s; failure status default `Degraded`

See [Extensibility](extensibility.md#health-probe-icacheorchestratorhealthprobe) for a custom probe implementation.

Admin API `GET …/health` is a **separate** endpoint: it still returns HTTP 200 when a registered cache probe fails, with `Healthy: false` (Admin Console App maps that to **Degraded**). See [admin.md](admin.md#health-semantics-admin-console-app-mapping).

## Security checklist

> [!IMPORTANT]
> Diagnostics are useful in production only when you decide what clients and operators may see:
>
> - [ ] Set `Cache:EmitDiagnosticsHeaders` to `false` if `X-Cache` (domain, hit/miss, phase, `ms`) must not reach untrusted clients
> - [ ] Keep server-side meter / activity / logs enabled independently of that switch when you still need ops visibility
> - [ ] Do not log raw `Authorization`, cookies, or other secrets — vary and key paths already avoid putting them in `X-Cache`
> - [ ] Treat Admin `GET …/health` and process `/health` as internal endpoints; do not expose them anonymously on the public internet
> - [ ] When scraping Prometheus (or compatible), scrape over a private network or authenticated path — metrics can include domain and route labels

## Related

- [Operations guide](../guide/operations.md) — day-2 ops, health, and Admin wiring  
- [Admin](admin.md) — Admin API, Console App, and management surface  
- [Cluster bus](cluster-bus.md) — multi-instance command metrics  

