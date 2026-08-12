# Changelog

All notable changes to **CacheOrchestrator** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
- **Local Admin API** (core package, opt-in) — process-local HTTP surface under `/cache-admin/local` via `MapCacheOrchestratorAdmin()` when `Cache:Admin:Enabled` is true
  - Live stats (domains / endpoints) with **request shares** and layer rates, discovered routes, domain config snapshot
  - Health probe: instance id, process start / uptime, lifetime request sum
  - Write ops: domain / entity invalidation, runtime **Version** and **TTL** overlays (process-local)
  - API key guard (`X-Cache-Admin-Key` / `Cache:Admin:ApiKey`); not on the caching hot path
- **CacheOrchestrator.Admin** app — separate fan-out process over configured instances (`CacheAdmin:Instances`)
  - Aggregate overview (cluster pipeline, OC hit / origin shares, alerts, `N/M` instance health)
  - Multi-page SPA (hash routes): Overview, Instances, Domains, Endpoints, Hints, Operations
  - Filters / search / sort; Overview **top 5** domains and endpoints ranked over the **full** aggregated sets
  - Instance health columns (status, Req, uptime, latency) from Local Admin `/health`
  - Rule-based **recommendation hints** in the Admin App (`RecommendationHints`); UI badges and Hints page
  - Modular static UI (`wwwroot/js/*` ES modules); Scalar OpenAPI in Development

### Fixed

- **Output Cache auth bypass** — `forceClient: Blocked` was ignored when writing response headers, so authenticated / `Authorization` bypass could still emit a public/private `Cache-Control` and `X-Cache client=public` instead of non-cacheable / `client=blocked`. `ApplyHeadersAsync` now honours `Blocked` the same way as `NoStore`.

### Tests

- Expanded **integration tests** (TestServer + DI, optional Testcontainers Redis): Output Cache HTTP lifecycle, Fusion domain resolution / fail-safe / Version reload, Client Cache Schedule cutover, config reload, multi-node Redis OC/L2, health checks, and related coverage
- Expanded **micro-benchmarks** (BenchmarkDotNet): hot-path coverage for HTTP helpers, Fusion key generation (resource id / route), Client Cache Schedule / `X-Cache` formatting, domain options / templates, Output Cache policy + query keys, ETag factory, Fusion entry-options reuse; unified short job settings and updated `docs/benchmarks/results.md`
- **Admin** unit tests under `tests/CacheOrchestrator.UnitTests/Admin` (registration, in-memory stats collector, fan-out service)

### Documentation

- Polished the main **README.md**, sample docs, and fixed minor typos
- **Deployment.md** — multi-instance topologies; shared configuration across instances (`appsettings.cache.json` / ConfigMap pattern; do not hand-edit per machine)
- **Invalidation.md** — multi-instance behaviour (local vs Redis backplane vs **CacheOrchestrator.Bus**); Version cutover via shared config
- **Admin** — [docs/admin.md](docs/admin.md) (Local Admin + Admin App architecture), [docs/admin-hints.md](docs/admin-hints.md) (hint rules / how to add), `src/CacheOrchestrator.Admin/README.md`

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


[Unreleased]: https://github.com/amarinsek/CacheOrchestrator/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/amarinsek/CacheOrchestrator/releases/tag/v1.0.0
