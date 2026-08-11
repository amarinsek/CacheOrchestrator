# Changelog

All notable changes to **CacheOrchestrator** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- **Output Cache auth bypass** — `forceClient: Blocked` was ignored when writing response headers, so authenticated / `Authorization` bypass could still emit a public/private `Cache-Control` and `X-Cache client=public` instead of non-cacheable / `client=blocked`. `ApplyHeadersAsync` now honours `Blocked` the same way as `NoStore`.

### Tests

- Expanded **integration tests** (TestServer + DI, optional Testcontainers Redis): Output Cache HTTP lifecycle, Fusion domain resolution / fail-safe / Version reload, Client Cache Schedule cutover, config reload, multi-node Redis OC/L2, health checks, and related coverage

### Documentation

- Polished the main **README.md**, sample docs, and fixed minor typos
- **Deployment.md** — multi-instance topologies; shared configuration across instances (`appsettings.cache.json` / ConfigMap pattern; do not hand-edit per machine)
- **Invalidation.md** — multi-instance behaviour (local vs Redis backplane); Version cutover via shared config; full fan-out sample with `ICacheInvalidationObserver` + message bus (loop-safe)

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
