# Observability

> **Reference** — `X-CacheOrchestrator`, metrics, traces, logs, and health checks.

How to see what the cache is doing: the **`X-CacheOrchestrator`** response header, the **`CacheOrchestrator`** meter and activity source, logs, and health checks.

Operator dashboards and multi-instance actions: [admin](admin.md). Admin Console App **traffic** charts are **Prometheus-only** (point `AdminConsole:Metrics` at a scrape of the meter). Try Prometheus with the playground [topology labs](../../samples/CacheOrchestrator.Sample/labs/README.md) — sample Compose only, not a NuGet dependency.

## Table of Contents

- [X-CacheOrchestrator response header](#x-cacheorchestrator-response-header)
- [Metrics](#metrics)
- [Activities (tracing)](#activities-tracing)
- [Logging](#logging)
  - [Levels by event](#levels-by-event)
  - [Categories](#categories)
  - [Configuring levels in appsettings](#configuring-levels-in-appsettings)
- [Health checks](#health-checks)
- [Security checklist](#security-checklist)

## X-CacheOrchestrator response header

Written by `DomainOutputCachePolicy` on response start via `CacheOrchestratorHeaderFormatter`, when
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
| `true` (default) | Clients receive `X-CacheOrchestrator` (and any future diagnostic response headers under this flag) |
| `false` | No diagnostic response headers; **Cache-Control**, ETag, metrics, activities, and logs still work |

Use `false` in production if you prefer not to expose domain names, hit/miss status, schedule phase, or timing to untrusted clients. Server-side observability (meter `CacheOrchestrator`, activity source `CacheOrchestrator`) is independent of this switch.

Example when enabled:

```http
X-CacheOrchestrator: domain=products; version=v1; client=public; phase=approaching; oc=miss; dc=hit; ms=12
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

An empty or unconfigured **domain name template** / resolver result fails closed with `Cache-Control: no-store`. When diagnostics are enabled, `X-CacheOrchestrator` uses `domain=_; version=-; client=no-store; phase=n/a; oc=bypass; dc=n/a; fa=run` and never echoes the unresolved value.

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

`phase` tag values match X-CacheOrchestrator: `calm`, `approaching`, `hold`, `n/a`.

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

Core activities tag `domain`, `provider`, and `cache.result`; the footprint activity distinguishes `hit` from `miss`. Wire names for the HTTP path use the same short layer ids as `X-CacheOrchestrator` and metrics (`dc`, `oc`). Data Cache activities tag `domain` and `cache.result` (including `unresolved`, `off`, and `bypass`), plus `entity_kind` / `resource_id` when set. Invalidation activities use `cache.scope`, `cache.kind`, `cache.tags`, `cache.dc.ok`, and `cache.oc.ok`, not `cache.result`. Failure events are `dc.invalidate.failed` and `oc.invalidate.failed`.

## Logging

CacheOrchestrator uses `Microsoft.Extensions.Logging` only. Libraries do not register a logging provider; the host (console, Application Insights, Seq, …) decides sinks. Prefer **metrics** and **`X-CacheOrchestrator`** for steady-state ops; raise log detail for an incident window, then turn it down again ([operations](../guide/operations.md#use-logs-and-traces-for-the-reason)).

### Levels by event

| Event | Level |
|-------|-------|
| Data Cache HIT / MISS | Debug |
| Output Cache HIT | Debug |
| Data Cache skip (auth bypass, `Cache-Control: no-store`) | Debug |
| Cache identity null material → caching bypassed | Debug |
| Cluster ignore (namespace / self / dedupe), “no peers” | Debug |
| Fusion / Hybrid provider GetOrCreate / Set (key) | Debug |
| Data Cache STALE | Information |
| Invalidation start | Information |
| Domain options snapshot refresh | Information |
| Data Cache no domain (factory runs uncached) | Warning |
| Dynamic Output Cache domain not configured | Warning |
| Unknown domain / missing Version | Warning |
| Content-hash identity skipped (`MaxBodyBytes` exceeded) | Warning |
| Data Cache ERROR / invalidation partial failure | Warning |
| Cluster peer publish failure / bus AllowUnauthenticated | Warning |
| Data Cache requested but no provider registered | Warning (once per process on first use) |

There is **no** dedicated log line for every internal step (vary materialization, Output Cache store, physical key assembly). Those surfaces show up as metrics, activities, and `X-CacheOrchestrator`. Output Cache **MISS** is also silent in logs (only HIT is logged at Debug).

### Categories

Most components use `ILogger<T>`, so the category is the **fully qualified type name**. Two map-time warnings use short fixed strings (`CacheOrchestrator.Admin`, `CacheOrchestrator.HttpBus`).

In `Logging:LogLevel`, a category is a **prefix**: `"CacheOrchestrator"` matches everything below; `"CacheOrchestrator.Configuration"` matches only the Configuration children; a full type name matches that logger only. The table is ordered so each deeper name sits under its parent prefix (alphabetical among siblings).

| Category | Package | What you typically see |
|----------|---------|------------------------|
| `CacheOrchestrator` | — | LogLevel prefix for the whole product (not a single logger type) |
| `CacheOrchestrator.Admin` | AspNetCore | Admin API mapped without `ApiKey` (fixed string) |
| `CacheOrchestrator.Admin.CacheOrchestratorManagement` | Core | Cluster publish failures from management actions |
| `CacheOrchestrator.AdminConsole.Services.Hints.HintRuleRegistry` | Admin Console App | Hint rule load / parse (not a NuGet package) |
| `CacheOrchestrator.Cluster.DefaultClusterCommandHandler` | Core | Apply / ignore / unsupported cluster commands |
| `CacheOrchestrator.Configuration` | — | LogLevel prefix for configuration loggers |
| `CacheOrchestrator.Configuration.CacheOrchestratorOptionsValidator` | Core | Startup / options normalization warnings |
| `CacheOrchestrator.Configuration.DomainCacheOptionsProvider` | Core | Options refresh; unknown domain / missing Version |
| `CacheOrchestrator.Configuration.RequestDomainCacheOptionsProvider` | AspNetCore | Request domain snapshot replaced mid-request |
| `CacheOrchestrator.DataCache.DomainDataCacheService` | AspNetCore | DC HIT / MISS / STALE / ERROR; auth / no-store skips; content-hash oversize on the Data Cache identity path |
| `CacheOrchestrator.EFCore.CacheInvalidationSaveChangesInterceptor` | EFCore.Invalidation | SaveChanges invalidation failures / skipped keys |
| `CacheOrchestrator.FusionCache.FusionDataCacheProvider` | FusionCache | GetOrCreate / Set with physical key |
| `CacheOrchestrator.HttpBus` | HttpBus | Bus receive mapped with `AllowUnauthenticated` (fixed string) |
| `CacheOrchestrator.HttpBus.HttpClusterCommandBus` | HttpBus | Publish peer failures; empty peer list |
| `CacheOrchestrator.HttpBus.ServiceDiscoveryClusterMembership` | HttpBus | Discovery empty / failures |
| `CacheOrchestrator.HybridCache.HybridDataCacheProvider` | HybridCache | GetOrCreate / Set with physical key |
| `CacheOrchestrator.Invalidation.CacheOrchestratorInvalidator` | Core | Invalidation start; DC/OC / observer failures |
| `CacheOrchestrator.Orchestration.CacheOrchestratorService` | Core | Data cache off; missing provider (one-time) |
| `CacheOrchestrator.OutputCache.DomainOutputCachePolicy` | AspNetCore | OC HIT; dynamic domain bypass; content-hash oversize and identity bypass on the Output Cache path |

> [!NOTE]
> Setting `"CacheOrchestrator.Admin": "Warning"` also covers `CacheOrchestrator.Admin.CacheOrchestratorManagement`, because LogLevel matching is prefix-based. The same applies to `"CacheOrchestrator.HttpBus"` and its children. Prefer the full type name when you need to tune only the typed logger.

Redis leaf packages do not emit their own product logs; connection problems surface through health probes, provider exceptions, and the invalidation / Data Cache Warning paths above.

### Configuring levels in appsettings

More specific category prefixes win over broader ones. Examples:

**Production-ish baseline** — framework quiet; CacheOrchestrator anomalies and invalidation visible; no HIT/MISS noise:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "CacheOrchestrator": "Warning",
      "CacheOrchestrator.Invalidation.CacheOrchestratorInvalidator": "Information"
    }
  }
}
```

**Incident / playground tuning** — keep product Information, turn on HIT/MISS and provider keys for one window:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "CacheOrchestrator": "Information",
      "CacheOrchestrator.DataCache.DomainDataCacheService": "Debug",
      "CacheOrchestrator.OutputCache.DomainOutputCachePolicy": "Debug",
      "CacheOrchestrator.FusionCache.FusionDataCacheProvider": "Debug"
    }
  }
}
```

The playground sample sets `"CacheOrchestrator": "Information"` so STALE and invalidation show without enabling Debug. For a full Debug dump during a lab, set `"CacheOrchestrator": "Debug"` temporarily.

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
> - [ ] Set `Cache:EmitDiagnosticsHeaders` to `false` if `X-CacheOrchestrator` (domain, hit/miss, phase, `ms`) must not reach untrusted clients
> - [ ] Keep server-side meter / activity / logs enabled independently of that switch when you still need ops visibility
> - [ ] Do not log raw `Authorization`, cookies, or other secrets — vary and key paths already avoid putting them in `X-CacheOrchestrator`
> - [ ] Treat Admin `GET …/health` and process `/health` as internal endpoints; do not expose them anonymously on the public internet
> - [ ] When scraping Prometheus (or compatible), scrape over a private network or authenticated path — metrics can include domain and route labels

## Related

- [Operations guide](../guide/operations.md) — day-2 ops, health, and Admin wiring  
- [Admin](admin.md) — Admin API, Console App, and management surface  
- [Cluster bus](cluster-bus.md) — multi-instance command metrics  

