# Changelog

All notable changes to **CacheOrchestrator** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Admin App Metrics** — optional Prometheus-compatible time series (`CacheAdmin:Metrics`: typically `Enabled`, `Provider`, `BaseUrl`)
  - BFF: `GET /api/metrics/status|catalog|series|summary` (allowlisted panels; no free-form PromQL from the browser)
  - SPA page `#/metrics` (range / domain filters, window KPIs, SVG charts) plus Overview “last 1h” embed when connected
  - Graceful `NotConfigured` / `Disconnected` / `Connected` (no fake zeros when storage is missing)
  - Local dev stack: `deploy/prometheus` (Docker) scrapes **Playground** `/metrics`; docs in Admin / observability
- Playground sample: OpenTelemetry Prometheus scrape endpoint (`/metrics`, Output Cache NoStore), Local Admin enabled for fan-out demos
- Admin App: `InstanceReachabilityCache` + `DownReprobeSeconds` — skip HTTP to known-down instances until re-probe (avoids stacking timeouts)
- Admin App unit tests for metrics catalog / query service and fan-out skip-down behaviour

- Admin App hints: approaching schedule (Info), hold older than 24 h (Warning), factory failure rate, runtime overlay reminder, Fusion hard TTL shorter than soft, schedule that cannot ramp.

### Changed

- **Admin App SPA soft refresh** — auto-refresh and header refresh repaint without a full “Loading…” flash; concurrent soft runs coalesce; GET `/api/overview` is deduped in-flight
- Admin App instance health: KPI / header show error styling unless **all** configured instances are healthy; JSON enums as strings (`JsonStringEnumConverter`) for SPA status handling
- Admin App fan-out: stats + domains load in parallel; overview uses a single stats pass with `ByInstance` (no second full stats fetch for hints)
- Documentation revised: root README, package and sample READMEs, and `docs/` aligned with the same tone and structure; Admin Metrics store and Prometheus dev guide

### Fixed

- Admin App: partial instance outage no longer blocks the whole dashboard on repeated request timeouts (down targets are marked and re-probed on an interval)

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
- **CacheOrchestrator.Admin** app — separate fan-out process over configured instances (`CacheAdmin:Instances`)
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


[Unreleased]: https://github.com/amarinsek/CacheOrchestrator/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/amarinsek/CacheOrchestrator/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/amarinsek/CacheOrchestrator/releases/tag/v1.0.0
