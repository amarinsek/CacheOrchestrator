# Changelog

All notable changes to **CacheOrchestrator** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0-rc.1] - 2026-08-08

Initial public release candidate.

**CacheOrchestrator** is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

### Added

- **Packages**
  - `CacheOrchestrator` — core library (InMemory backends); targets `net8.0` and `net10.0`
  - `CacheOrchestrator.Redis` — optional Redis Output Cache store, FusionCache L2, backplane, health probes
- **Domain configuration** — `Cache` section, `DomainDefaults` / `Domains`, named `FusionCacheInstances`
- **Output Cache** — per-domain policies, tags, ETag modes (`Version` | `Resource` | `None`), auth flags (`BypassWhenAuthenticated`, `VaryOutputCacheByUser`)
- **FusionCache** — `IDomainFusionCache` (domain resolution, resource id, fail-safe entry options)
- **Client Cache** — `Cache-Control` from domain settings + **Client Cache Schedule** (Calm / Approaching / Hold)
- **Invalidation** — `ICacheOrchestratorInvalidator` (domain, entity, tags), structured results, optional observers
- **Pluggable backends** — `ICacheBackendRegistrar` / `AddBackend`
- **Observability** — `X-Cache` (toggle with `EmitDiagnosticsHeaders`, default on), metrics, activities, health checks
- **Samples** — Minimal (1-minute InMemory) and interactive playground
- **Docs** — getting started, configuration, domain profiles, Client Cache Schedule, FAQ, comparison, releasing
- **Quality** — unit tests (net8 + net10), integration tests (net10 + Testcontainers Redis), Minimal sample CI smoke, SourceLink + snupkg, MinVer (`v*` tags)

### Notes for consumers

- Redis is **not** in the core package: install `CacheOrchestrator.Redis` and call `AddRedisBackend()`.
- Fusion without a resolved domain runs the factory **uncached** (Warning + `result=unresolved`).
- Default Fusion namespace for the `default` instance is `{Namespace}-fc` (no `-default` suffix).

[Unreleased]: https://github.com/amarinsek/CacheOrchestrator/compare/v1.0.0-rc.1...HEAD
[1.0.0-rc.1]: https://github.com/amarinsek/CacheOrchestrator/releases/tag/v1.0.0-rc.1
