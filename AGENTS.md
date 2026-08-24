# AGENTS.md — CacheOrchestrator

Context for AI coding agents working in this repository.

## What this project is

**CacheOrchestrator** configures and coordinates three layers — Output Cache (OC), **data cache** (DC; FusionCache or HybridCache), and client Cache-Control (CC) — under one **domain** model. Define the rules once in configuration, then apply them on endpoints with a single attribute or extension. It does not replace those systems or own a store: ASP.NET still holds the HTTP response, the data engine holds the object, and the browser or CDN still honours `Cache-Control`. Package composition: `docs/packages.md`.

Internally it wires:

1. **Output Cache** — full HTTP response caching (ASP.NET Core)  
2. **Data cache** — application object caching via `IDataCacheProvider` (FusionCache L1/L2 ± backplane, or HybridCache)  
3. **Client Cache-Control** — browser/CDN headers (+ optional Client Cache Schedule)

Domains are named groups of data that share TTLs, providers, client headers, and version stamps.

- Packages: `src/CacheOrchestrator.Core` (Http-free), `src/CacheOrchestrator.FusionCache`, `src/CacheOrchestrator.HybridCache` (Microsoft HybridCache data provider; subset of Fusion), `src/CacheOrchestrator.AspNetCore` (HTTP host; InMemory backend)  
- Meta NuGet: `src/CacheOrchestrator` (PackageId `CacheOrchestrator` → AspNetCore + FusionCache)  
- Redis package: `src/CacheOrchestrator.Redis` (`AddRedisBackend`)  
- HttpBus package: `src/CacheOrchestrator.HttpBus` (`AddHttpClusterBus` / `MapCacheOrchestratorHttpBus`) — optional multi-instance command fan-out  
- EF invalidation package: `src/CacheOrchestrator.EFCore.Invalidation` (`AddCacheOrchestratorEfCoreInvalidation` / `AddCacheOrchestratorInvalidation`)  
- Admin Console App: `src/CacheOrchestrator.AdminConsole` (fan-out UI/API; not a NuGet package; **net10.0 only**)  
- Target frameworks: libraries `net8.0` + `net10.0`; Admin Console App `net10.0` only; samples typically net10  
- Version: **MinVer** from Git tags `v*` (do not hardcode `<Version>` in Directory.Build.props)  
- Samples: `samples/CacheOrchestrator.Minimal` (1-minute InMemory), `samples/CacheOrchestrator.Sample` (playground; Redis package)  
- Tests: `tests/CacheOrchestrator.Core.UnitTests`, `tests/CacheOrchestrator.AspNetCore.UnitTests`, `tests/CacheOrchestrator.FusionCache.UnitTests`, `tests/CacheOrchestrator.HybridCache.UnitTests`, `tests/CacheOrchestrator.Redis.UnitTests`, `tests/CacheOrchestrator.HttpBus.UnitTests`, `tests/CacheOrchestrator.EFCore.Invalidation.UnitTests` (net8+net10), `tests/CacheOrchestrator.AdminConsole.UnitTests` (net10 only), `IntegrationTests` (net8+net10 + Testcontainers Redis), `Benchmarks`

## Non-goals

- Not a replacement for FusionCache / HybridCache features — it **configures and scopes** them  
- Does not own Redis topology/ops beyond connection options  
- Libraries prefer `ICacheOrchestrator` (Core); web happy path may use AspNetCore `IDomainDataCache`

## Mental model

```
Domain (config name)
  → DomainCacheOptions (resolved snapshot)
      → DomainOutputCachePolicy (HTTP)
      → ICacheOrchestrator / IDomainDataCache get-or-set (data)
      → EntityFootprint tags (domain + entity / entitykind; optional members / dependsOn / aliases)
```

**Domain for data cache** (`IDomainDataCache.GetOrSetAsync` HTTP path):

1. Explicit overload `GetOrSetAsync(http, domain, factory)` — same name reuses the request snapshot; a different name **replaces** it.  
2. Else options already on request (Output Cache policy usually set them via `.CacheOutputWithDomain` / `[CacheDomain]`).  
3. Else resolve domain from endpoint metadata (`DomainOutputCachePolicy` / `CacheDomainAttribute`) and `EnsureDomainOptions`.  
4. Else factory runs **uncached**.

Happy path: **no** manual `EnsureDomainOptions` when OC domain is on the endpoint.  
**Data-cache-only** endpoints: use domain overload or `EnsureDomainOptions`.

**Entity identity:** declare once on `.CacheOutputWithDomain` / `[CacheDomain]` (`resourceRouteKey` + `entityKind` for detail, or `entityKind` alone for collections). `GetOrSetEntityAsync(http, factory)` / `GetOrSetEntitySetAsync` consume it. Extend tags with `EntityCache` / `EntitySet`. Data-cache-only: `SetEntityIdentity`. Explicit kind/id overloads are obsolete.

### Client Cache Schedule (important product feature)

Feature name: **Client Cache Schedule**.  
Pure logic: `ClientCacheHeaderGenerator` + `ClientCacheSchedulePhase`.

- `ScheduledUpdateUtc` + `ClientTtlSeconds` / `ClientTtlMinSeconds` → long client `max-age` in **Calm**, linear ramp-down in **Approaching**, floor in **Hold**.  
- Affects **client** `Cache-Control` only, not server Output/Fusion TTLs.  
- Phase is exposed on **`X-Cache` (`phase=`)** and metrics **`cache_orchestrator.client.schedule`** (tags `domain`, `phase`).  
- Human docs: `docs/client-cache-schedule.md`, README section “Client Cache Schedule”.

## Public entry points (do not invent alternate names)

| API | Namespace |
|-----|-----------|
| `AddCacheOrchestrator` (meta = AspNet + Fusion) / `AddCacheOrchestratorAspNetCore` / `UseCacheOrchestrator` | `CacheOrchestrator.DependencyInjection` |
| `ICacheOrchestratorBuilder` / `ICacheBackendRegistrar` (OC) | `CacheOrchestrator.DependencyInjection` / `CacheOrchestrator.Backends` |
| `AddCacheOrchestratorFusionCache` / `IFusionCacheBackendRegistrar` | `CacheOrchestrator.DependencyInjection` / `CacheOrchestrator.FusionCache.Backends` |
| `AddCacheOrchestratorHybridCache` | `CacheOrchestrator.DependencyInjection` (HybridCache package) |
| `AddRedisBackend` / `RedisCacheBackendRegistrar` | `CacheOrchestrator.Redis` |
| `CacheOutputWithDomain` / `CacheOutputWithDomainTemplate` / `CacheOutputWithDomainAttribute` | `CacheOrchestrator.OutputCache` |
| `[CacheDomain("…")]` | `CacheOrchestrator.OutputCache` |
| `IDomainDataCache` | `CacheOrchestrator.DataCache` (HTTP API in AspNetCore) |
| `EntityCache` / `EntitySet` / `EntityFootprint` | `CacheOrchestrator.Entity` (Core) |
| `ICacheVaryContributor` / `CacheVaryMaterializer` / `ICacheVaryBuilder` | `CacheOrchestrator.Vary` |
| `AuthBypassMode` / `DomainAuthEvaluator` | `CacheOrchestrator.Configuration` |
| `IDomainCacheOptionsProvider` / `DomainCacheOptions` / `DomainName` | `CacheOrchestrator.Configuration` |
| `ICacheOrchestratorInvalidator` / `ICacheInvalidationObserver` / `CacheInvalidationResult` | `CacheOrchestrator.Invalidation` |
| `CacheTags` | `CacheOrchestrator.Configuration` |
| Health: `AddCacheOrchestrator` on `IHealthChecksBuilder` | `CacheOrchestrator.Diagnostics` |
| `MapCacheOrchestratorAdmin` / Admin API | `CacheOrchestrator.DependencyInjection` / `CacheOrchestrator.Admin` |
| `AddHttpClusterBus` / `MapCacheOrchestratorHttpBus` | `CacheOrchestrator.HttpBus` |
| `AddCacheOrchestratorEfCoreInvalidation` / `AddCacheOrchestratorInvalidation` / `[CacheEntity]` | `CacheOrchestrator.EFCore` |
| `IClusterCommandBus` / `IClusterMembership` / `IInstanceIdProvider` | `CacheOrchestrator.Cluster` |
| Admin Console App fan-out host | `src/CacheOrchestrator.AdminConsole` (`AdminConsole` config) |

There is **no** `CacheOrchestrator.Abstractions` folder — interfaces sit beside implementations (`Backends`, `FusionCache`, `Diagnostics`, …).

**Visibility:** default implementations (`DomainDataCacheService`, `DomainCacheOptionsProvider`, `CacheOrchestratorInvalidator`, health check types, options validator, MVC convention) are **`internal`**. Apps use interfaces + DI. Unit tests use `InternalsVisibleTo`.

## Config vs runtime naming

Nested JSON (TimeSpan strings) under `DataCache` / `OutputCache` / `ClientCache` / optional `FusionCache`. Runtime snapshot on Core:

| JSON | Runtime `DomainCacheOptions` |
|------|------------------------------|
| `OutputCache:Ttl` | `OutputTtl` |
| `DataCache:Ttl` | `DataCacheTtl` |
| `ClientCache:Ttl` / `TtlMin` | `ClientTtlSeconds` / `ClientTtlMinSeconds` (still int seconds on snapshot) |

Fusion-only knobs (`HardTtl`, `FailSafe`, factory timeouts, …) bind in the **FusionCache** package (`DomainFusionCacheSettings`), not on Core `DomainCacheOptions`. Root engines: `DataCacheInstances` (not `FusionCacheInstances`).

Do not rename config property names without a breaking-change plan (bound from appsettings).

## Conventions (follow when editing)

- English only for code comments and XML docs  
- `CancellationToken` parameter name: **`cancellationToken`**  
- Prefer `ArgumentNullException.ThrowIfNull`  
- Public concrete types: **`sealed`** where possible  
- Library awaits: **`ConfigureAwait(false)`**  
- Interfaces next to implementations  
- One primary public type per file when practical  
- Log cache HIT/MISS at **Debug**; STALE / failures higher  
- **Do not** “complete” switches with `throw new NotImplementedException()` for enum defaults that intentionally fall through to `_ =>` — `dotnet format` / IDE0010 has introduced that bug before  

## Folder map

```
src/CacheOrchestrator.Core/         Http-free: options, Entity footprint, orchestration, invalidation, cluster contracts
src/CacheOrchestrator.FusionCache/  Fusion IDataCacheProvider + named Ziggy instances + L2 registrars
src/CacheOrchestrator.HybridCache/  HybridCache IDataCacheProvider
src/CacheOrchestrator.AspNetCore/   HTTP: OutputCache, Vary, Admin API, IDomainDataCache (Core only)
src/CacheOrchestrator/              meta NuGet: AspNetCore + FusionCache (`AddCacheOrchestrator` wires both)
src/CacheOrchestrator.Redis/        Redis OC + Fusion L2/backplane (refs AspNetCore + FusionCache)
src/CacheOrchestrator.HttpBus/      HTTP cluster command bus + membership
src/CacheOrchestrator.EFCore.Invalidation/  SaveChanges → invalidator (Core only)
src/CacheOrchestrator.AdminConsole/ Admin Console App (not packable)
tests/CacheOrchestrator.Core.UnitTests/
tests/CacheOrchestrator.AspNetCore.UnitTests/
tests/CacheOrchestrator.FusionCache.UnitTests/
tests/CacheOrchestrator.HybridCache.UnitTests/
tests/CacheOrchestrator.Redis.UnitTests/
tests/CacheOrchestrator.HttpBus.UnitTests/
tests/CacheOrchestrator.EFCore.Invalidation.UnitTests/
tests/CacheOrchestrator.AdminConsole.UnitTests/
tests/CacheOrchestrator.IntegrationTests/
tests/CacheOrchestrator.Benchmarks/
samples/
docs/
```

## Docs for humans

Three tiers (do not put reference into the root README):

- **Product:** `README.md` — overview and quick start only
- **Guide:** `docs/guide/` (concepts, topologies, operations) + `docs/getting-started.md` + `docs/packages.md` + FAQ + Minimal sample
- **Reference:** topic pages under `docs/` (configuration, keys, deployment, …); hub: `docs/README.md`

Contributor / security: `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`.  
Keep docs in sync when renaming public types or config keys. Put a change in the matching tier.

Branch worklog: copy `docs/templates/worklog-template.md` (do not commit the filled copy). Summary → PR title and description; the rest is the PR appendix. Record **net outcomes** only — no chat, no rejected alternatives, no draft paths. A work item must still make sense a month later without the conversation.

Do **not** edit `CHANGELOG.md` unless the user asks. User-facing notes go in the worklog Changelog. The maintainer updates `CHANGELOG.md` from merged PR worklogs.

## Safe change checklist

1. Build solution (`CacheOrchestrator.slnx`)  
2. Run unit tests for touched packages (`Core` / `AspNetCore` / `FusionCache` / `HybridCache` / `Redis` / `HttpBus` / `EFCore.Invalidation`)  
3. Update sample if public API or config surface changes  
4. Avoid introducing `CacheOrchestrator.Abstractions` again  
5. Avoid reintroducing Slovenian comments or `ct` as public parameter names  

## Out of scope unless asked

- Rewriting README style with marketing fluff  
- Changing NuGet package metadata placeholders without explicit request  
- Committing secrets or real Redis production endpoints  
