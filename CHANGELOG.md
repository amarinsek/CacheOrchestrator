# Changelog

All notable changes to **CacheOrchestrator** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- OTel histogram **`cache_orchestrator.factory.duration`** (ms; miss/stale/**fail** factory path)
- OTel Fusion **`result=fail`** on hard factory throw (no fail-safe value); Console window maps it to factory failures
- `AdminLiveStatsRawSnapshot` / raw counter DTOs; `IAdminStatsCollector.GetRawSnapshot()`
- `CacheImpactKpiDto` on domain/endpoint stats (filled by Admin Console from Prometheus)
- Admin Console: **ImpactMath** (factory avoidance, est. time saved, benefit/candidate bands)
- Admin Console hint paths `domain.impact.*` / `endpoint.impact.*` + core rules (poor candidate, at-risk, strong)
- Console **`GET /api/stats/window`** — domain/endpoint traffic + impact + hints from Prometheus for the selected Range

### Fixed

- OTel `cache_orchestrator.invalidate` no longer uses entity scope paths (e.g. `product-crud/products/42`) as the `domain` label — only the domain name (cardinality-safe). Optional low-cardinality `kind` tag (Domain/Entity/EntityKind).
- Window stats use Prometheus **`increase()` over the Range** (not instant `now − offset`), so idle/stale label sets stay visible like charts; brand-new single-scrape series fall back to current value

### Changed

- **Admin Console stats are Prometheus-only** — no Local Admin process-totals fan-out for traffic UI; Overview/header KPIs, Domains, Endpoints, Hints, impact all use `/api/stats/window`
- Removed Local Admin **`GET …/stats/v2`**; process-lifetime **`GET …/stats`** kept but **obsolete** (diagnostics / external tools)
- Console `GET /api/stats` and `/api/endpoints` return empty shells (wire compatibility); SPA does not use them for traffic
- Local Admin live counters store **raw** values; `/stats` fat DTO still projected via `AdminStatsV1Mapper`
- Admin factory latency samples only on factory path (miss/stale/fail), not on Fusion hits
- `cache_orchestrator.fc.duration` remains as legacy dual-write (all timed Fusion results)
- Metrics panels: `factory_p95_ms`, `factory_run_rate`, `factory_share`, `factory_size_p95` (windowed)
- **`Cache:Admin:TrackResultSize`** + Admin raw `factoryResultSize*`; OTel `cache_orchestrator.factory.result_size`
- Console global **Range** picker: Last 15m–7d / absolute from–to (Grafana-style); Metrics `from`/`to` query params (process-totals mode removed)
- Console Metrics UI: multi-select domains, empty-window charts + no-samples badge, shared chart cards on Overview
- Console: current-value styling (green underline), Metrics store card on Instances, breadcrumbs only on detail pages
- Window stats: **HintEngine** on Prometheus rows + optional **by-instance** (`instance_id`, missing → `undefined`)

### Removed

- Local Admin **`GET …/stats/v2`**
- Console **Process totals** Range mode and instance counter fan-out for stats UI

### Documentation

- `docs/admin.md` / Console README / labs — Prom-only Console stats; `/stats` obsolete; `docs/observability.md` — `result=fail`, `factory.duration`

## [2.1.0] - 2026-08-15

Admin Console App–focused release: Metrics UI, declarative hints, Docker image on GHCR, and host rename. NuGet libraries stay compatible; additive metrics/Admin API fields only.

### Added

#### Libraries (NuGet)

- **`Cache:Metrics:IncludeEndpointLabel`** (default `true`) — optional stable `route` tag on OC/FC meter instruments (`METHOD` + route template, same as Admin endpoint keys); set `false` to lower Prometheus cardinality
- Local Admin **`GET …/cluster/info`** when `Cache:Admin` is enabled (works without CacheOrchestrator.Bus)

#### Admin Console App (host; not a NuGet package)

- **Docker image** published on GitHub Release: `ghcr.io/amarinsek/cacheorchestrator-admin-console`
  - Operator volume `/app/data`: custom packs in `data/rules/*.json`, Settings disables in `data/disabled.local.json`
  - Product `hints/core-hints.json` stays in the image; runbook under `deploy/admin/`
- **Metrics UI** — optional Prometheus-compatible time series (`AdminConsole:Metrics`: `Enabled`, `Provider`, `BaseUrl`)
  - BFF: `GET /api/metrics/status|catalog|series|summary` (allowlisted panels; no free-form PromQL from the browser)
  - SPA `#/metrics` (range / domain filters, window KPIs, SVG charts); Overview “last 1h” embed; detail embeds on domain / instance / endpoint
  - Chart enlarge modal, hover snap on series; `NotConfigured` / `Disconnected` / `Connected` without fake zeros
  - Dev scrape stack: `samples/CacheOrchestrator.Sample/deploy/prometheus` (Playground `/metrics` only)
- **Declarative recommendation hints** — JSON rule packs after fan-out (not on the instance hot path)
  - Product pack `hints/core-hints.json` (always loaded) plus optional packs via `AdminConsole:Hints:RuleFiles`
  - Compiler/checker (rule code + path in errors); enable/disable per code (config and Settings UI)
  - Settings `#/settings` (catalog, severity, view JSON, reload); expanded default rules (schedule, factory, TTL, drift, …)
- `InstanceReachabilityCache` + `DownReprobeSeconds` — skip HTTP to known-down instances until re-probe
- Playground sample: OpenTelemetry `/metrics` (Output Cache NoStore), Local Admin enabled, `IncludeEndpointLabel` on
- Unit tests: metrics catalog / query service, fan-out skip-down, declarative hint compiler

### Changed

#### Libraries (NuGet)

- Admin Local API / stats DTOs: prefer **`factoryShare`** (request share of factory runs); **`originShare`** remains as an obsolete synonym (same value) for wire compatibility
- Endpoint key (`METHOD` + route template) resolved once per request (`HttpContext.Items`) and shared by Local Admin counters and optional metrics `route` tag
- Bus: skip duplicate `GET …/cluster/info` when Local Admin already maps it (same route prefix)

#### Admin Console App

- **Host rename** — `CacheOrchestrator.Admin` → **`CacheOrchestrator.AdminConsole`** (namespaces `CacheOrchestrator.AdminConsole.*`); config section **`CacheAdmin` → `AdminConsole`** (`AdminConsoleOptions`); DTO file/types `AdminAppModels` / `AdminApp*Request` → **`AdminConsoleModels`** / **`AdminConsole*Request`**. Core Admin API (`Cache:Admin`, `MapCacheOrchestratorAdmin`) unchanged
- Targets **`net10.0` only** (monitored instances may still be net8 or net10). Unit tests for the host run under net10 only
- Defaults: Production/Docker empty `Instances`, Metrics off, custom hints under `data/rules/`; Development keeps playground `:5289` and `hints/*`
- Hint product codes: `high-factory-share` / `critical-factory-share` / `instance-factory-spread` (replacing `*-origin-*`)
- SPA soft refresh (no full “Loading…” flash); overview request coalescing; Metrics charts patch in place
- Instance health KPI / header: error styling unless **all** configured instances are healthy; JSON enums as strings for SPA
- Hints page: severity KPIs show **visible/total** when filters apply
- Fan-out: stats + domains in parallel; single stats pass with `ByInstance` for overview/hints

### Fixed

- Admin Console App: partial instance outage no longer blocks the dashboard on stacked timeouts (down targets marked and re-probed)
- Admin Console App cluster probe: non-JSON / HTML responses (e.g. `MapFallbackToFile`) no longer surface raw `JsonException`; clearer error text

### Documentation

- Root README, package and sample READMEs, and `docs/` aligned after 2.0.0
- Admin Console App Docker / operator guide (`deploy/admin/`), Metrics + Prometheus sample, hints operator handbook (`hints/README.md`, `docs/admin-hints.md`)
- Product wording unified to **Admin Console App** (was “Admin App”)

## [2.0.0] - 2026-08-13

### Breaking

- Entity identity is now `(domain, entityKind, resourceId)`. A domain is a cache **policy** group and does not uniquely identify a row.
  - Removed `InvalidateEntityAsync(domain, resourceId)`. Use `InvalidateEntityAsync(domain, entityKind, resourceId)`, `InvalidateEntitiesAsync`, or `InvalidateEntityKindAsync`.
  - Removed `IDomainFusionCache.GetOrSetAsync(http, domain, resourceId, factory)`. Use `GetOrSetEntityAsync` (domain optional when already on the request).
  - Entity tags are `entity:{domain}:{entityKind}:{resourceId}` (was `entity:{domain}:{resourceId}`). Kind-wide tag: `entitykind:{domain}:{entityKind}`.
  - Fusion resource keys include `entityKind`: `{domain}:{versionHex}:id:{entityKind}:{resourceId}:{hash}`.
  - `CacheOutputWithDomain` / `[CacheDomain]` require `entityKind` when `resourceRouteKey` is set.
  - Admin API `scope=entity` requires `entityKind`. New `scope=entityKind` purges the kind tag.
  - In-flight 1.0.0 entity entries are not evicted by the new APIs; they expire by TTL, or purge the domain / bump Version on deploy. Upgrade all cluster nodes together before relying on entity Bus commands.

### Added

- **CacheOrchestrator.EFCore.Invalidation** (optional package) — SaveChanges interceptor that maps CLR types to `(domain, entityKind)` in **code** (`[CacheEntity]`, Fluent `CacheInvalidate`, or `Map<T>`) and calls `InvalidateEntitiesAsync` / `InvalidateEntityKindAsync` after a successful save. Not an EF cache provider. `ExecuteUpdate`/`ExecuteDelete` stay manual.
- **CacheOrchestrator.Bus** (optional package) — HTTP cluster command bus for multi-instance command fan-out
  - Core contracts: `IClusterCommandBus` / `IClusterMembership` / `IClusterCommandHandler` (Null defaults in core)
  - Commands: `InvalidateCommand`, `VersionBumpCommand`, `TtlPatchCommand` (polymorphic JSON)
  - Membership: **Null**, **Static**, **ServiceDiscovery** (`Microsoft.Extensions.ServiceDiscovery`)
  - `HttpClusterCommandBus`; receive endpoints via `MapCacheOrchestratorHttpBus()` (independent of Admin)
  - CommandId **dedupe window** on receive (`Cache:Cluster:Bus:DedupeWindowSeconds`)
  - Single process identity: **`Cache:InstanceId`** (Admin no longer has its own InstanceId)
  - Admin `distribute` flag on invalidate / version / TTL; programmatic invalidator publishes when bus enabled
  - **Admin App**: auto bus-distribute vs HTTP fan-out; Operations UI shows distribution mode; `GET /api/distribution`
  - Metrics: `cache_orchestrator.cluster.commands_*` / `publish_failures` / `command_dedupe_hits`
- **Admin API** (core package, opt-in) — process-local HTTP surface under `/cache-admin/local` via `MapCacheOrchestratorAdmin()` when `Cache:Admin:Enabled` is true
  - Live stats (domains / endpoints) with **request shares** and layer rates, discovered routes, domain config snapshot
  - Health probe: instance id, process start / uptime, lifetime request sum
  - Write ops: domain / entity invalidation, runtime **Version** and **TTL** overlays (process-local)
  - API key guard (`X-Cache-Admin-Key` / `Cache:Admin:ApiKey`); not on the caching hot path
- **CacheOrchestrator.AdminConsole** app — separate fan-out process over configured instances (`AdminConsole:Instances`)
  - Aggregate overview (cluster pipeline, OC hit / origin shares, alerts, `N/M` instance health)
  - Multi-page SPA (hash routes): Overview, Instances, Domains, Endpoints, Hints, Operations
  - Filters / search / sort; Overview **top 5** domains and endpoints ranked over the **full** aggregated sets
  - Instance health columns (status, Req, uptime, latency) from Admin API `/health`
  - Rule-based **recommendation hints** in the Admin App (`RecommendationHints`); UI badges and Hints page
  - Modular static UI (`wwwroot/js/*` ES modules); Scalar OpenAPI in Development

### Fixed

- **Output Cache auth bypass** — `forceClient: Blocked` was ignored when writing response headers, so authenticated / `Authorization` bypass could still emit a public/private `Cache-Control` and `X-Cache client=public` instead of non-cacheable / `client=blocked`. `ApplyHeadersAsync` now honours `Blocked` the same way as `NoStore`.

### Tests

- **Integration tests** now multi-target `net8.0` and `net10.0` (same TFMs as the published libraries); CI runs the suite — including Testcontainers Redis — on both
- Expanded **integration tests** (TestServer + DI, optional Testcontainers Redis): Output Cache HTTP lifecycle, Fusion domain resolution / fail-safe / Version reload, Client Cache Schedule cutover, config reload, multi-node Redis OC/L2, health checks, and related coverage
- Expanded **micro-benchmarks** (BenchmarkDotNet): hot-path coverage for HTTP helpers, Fusion key generation (resource id / route), Client Cache Schedule / `X-Cache` formatting, domain options / templates, Output Cache policy + query keys, ETag factory, Fusion entry-options reuse; unified short job settings and updated `docs/benchmarks/results.md`
- **Admin** unit tests under `tests/CacheOrchestrator.UnitTests/Admin` (registration, in-memory stats collector, fan-out service)

### Documentation

- Rewrote **README** files (GitHub, NuGet packages, samples, Admin App) and **docs/**: shorter classical tone, purpose-first openings, Install / Register / Configure split, Admin API naming, hand-rolled vs CacheOrchestrator [comparison](docs/comparison.md)
- **Deployment.md** — multi-instance topologies; shared configuration across instances (`appsettings.cache.json` / ConfigMap pattern)
- **Invalidation.md** — multi-instance behaviour (local vs Redis backplane vs **CacheOrchestrator.Bus**); Version cutover via shared config
- **Admin** — [docs/admin.md](docs/admin.md) (Admin API + Admin App), [docs/admin-hints.md](docs/admin-hints.md)

## [1.0.0] - 2026-08-08

First stable release.

**CacheOrchestrator** is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

### Added

- **Packages**
  - `CacheOrchestrator` — core library (InMemory backends); targets `net8.0` and `net10.0`
  - `CacheOrchestrator.Redis` — optional Redis Output Cache store, FusionCache L2, backplane, health probes
- **Domain configuration** — `Cache` section, `DomainDefaults` / `Domains`, named `FusionCacheInstances`
- **Output Cache** — per-domain policies, tags, ETag modes (`Version` | `Resource` | `None`), auth flags (`BypassWhenAuthenticated`, `VaryOutputCacheByUser`)
- **FusionCache** — `IDomainFusionCache` (domain resolution, resource id, fail-safe entry options)
- **Client Cache** — `Cache-Control` from domain settings + **Client Cache Schedule** (Calm / Approaching / Hold)
- **Invalidation** — `ICacheOrchestratorInvalidator` (domain, entity, tags), structured results, optional observers (`ICacheInvalidationObserver`)
- **Pluggable backends** — `ICacheBackendRegistrar` / `AddBackend`
- **Observability** — `X-Cache` (toggle with `EmitDiagnosticsHeaders`, default on), metrics, activities, health checks
- **Samples** — Minimal (1-minute InMemory) and interactive playground
- **Docs** — getting started, configuration, domain profiles, Client Cache Schedule, FAQ, comparison, releasing
- **Quality** — unit tests (net8 + net10), integration tests (net10 + Testcontainers Redis), Minimal sample CI smoke, SourceLink + snupkg, MinVer (`v*` tags), custom-backend E2E, config-reload snapshot tests, Fusion fail-safe STALE integration tests

[Unreleased]: https://github.com/amarinsek/CacheOrchestrator/compare/v2.1.0...HEAD
[2.1.0]: https://github.com/amarinsek/CacheOrchestrator/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/amarinsek/CacheOrchestrator/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/amarinsek/CacheOrchestrator/releases/tag/v1.0.0
