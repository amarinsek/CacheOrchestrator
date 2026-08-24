# AGENTS.md — CacheOrchestrator

Context for AI coding agents working in this repository.

## What this project is

**CacheOrchestrator** configures and coordinates three existing layers in ASP.NET Core — Output Cache (OC), FusionCache (L1/L2), and client Cache-Control (CC) — under one **domain** model. Define the rules once in configuration, then apply them on endpoints with a single attribute or extension. It does not replace those systems or own a store: ASP.NET still holds the HTTP response, FusionCache still holds the object, and the browser or CDN still honours `Cache-Control`.

Internally it wires:

1. **Output Cache** — full HTTP response caching (ASP.NET Core)  
2. **FusionCache** — application object caching (ZiggyCreatures; L1 memory ± L2 distributed cache via pluggable backends)  
3. **Client Cache-Control** — browser/CDN headers (+ optional Client Cache Schedule)

Domains are named groups of data that share TTLs, providers, client headers, and version stamps.

- Packages: `src/CacheOrchestrator.Core` (Http-free), `src/CacheOrchestrator.FusionCache`, `src/CacheOrchestrator.HybridCache` (Microsoft HybridCache data provider; subset of Fusion), `src/CacheOrchestrator.AspNetCore` (HTTP host; InMemory backend)  
- Meta NuGet: `src/CacheOrchestrator` (PackageId `CacheOrchestrator` → AspNetCore + FusionCache)  
- Redis package: `src/CacheOrchestrator.Redis` (`AddRedisBackend`)  
- Bus package: `src/CacheOrchestrator.Bus` (`AddHttpClusterBus` / `MapCacheOrchestratorHttpBus`) — optional multi-instance command fan-out  
- EF invalidation package: `src/CacheOrchestrator.EFCore.Invalidation` (`AddCacheOrchestratorEfCoreInvalidation` / `AddCacheOrchestratorInvalidation`)  
- Admin Console App: `src/CacheOrchestrator.AdminConsole` (fan-out UI/API; not a NuGet package; **net10.0 only**)  
- Target frameworks: libraries `net8.0` + `net10.0`; Admin Console App `net10.0` only; samples typically net10  
- Version: **MinVer** from Git tags `v*` (do not hardcode `<Version>` in Directory.Build.props)  
- Samples: `samples/CacheOrchestrator.Minimal` (1-minute InMemory), `samples/CacheOrchestrator.Sample` (playground; Redis package)  
- Tests: `tests/CacheOrchestrator.UnitTests` (core, net8+net10), `tests/CacheOrchestrator.Redis.UnitTests`, `tests/CacheOrchestrator.Bus.UnitTests`, `tests/CacheOrchestrator.EFCore.Invalidation.UnitTests` (net8+net10), `tests/CacheOrchestrator.AdminConsole.UnitTests` (net10 only; Admin Console App), `IntegrationTests` (net8+net10 + Testcontainers Redis), `Benchmarks`

## Non-goals

- Not a generic cache façade for non-HTTP apps without `HttpContext`  
- Not a replacement for FusionCache features — it **configures and scopes** them  
- Does not own Redis topology/ops beyond connection options  

## Mental model

```
Domain (config name)
  → DomainCacheOptions (resolved snapshot)
      → DomainOutputCachePolicy (HTTP)
      → IDomainFusionCache.GetOrSetAsync / GetOrSetEntityAsync / GetOrSetEntitySetAsync (data)
      → EntityFootprint tags (domain + entity / entitykind; optional members / dependsOn / aliases)
```

**Domain for FusionCache** (`IDomainFusionCache.GetOrSetAsync`):

1. Explicit overload `GetOrSetAsync(http, domain, factory)` — same name reuses the request snapshot; a different name **replaces** it.  
2. Else options already on request (Output Cache policy usually set them via `.CacheOutputWithDomain` / `[CacheDomain]`).  
3. Else resolve domain from endpoint metadata (`DomainOutputCachePolicy` / `CacheDomainAttribute`) and `EnsureDomainOptions`.  
4. Else factory runs **uncached**.

Happy path: **no** manual `EnsureDomainOptions` when OC domain is on the endpoint.  
**Fusion-only** endpoints: use domain overload or `EnsureDomainOptions`.

**Entity identity:** declare once on `.CacheOutputWithDomain` / `[CacheDomain]` (`resourceRouteKey` + `entityKind` for detail, or `entityKind` alone for collections). `GetOrSetEntityAsync(http, factory)` / `GetOrSetEntitySetAsync` consume it. Extend tags with `EntityCache` / `EntitySet`. Fusion-only: `SetEntityIdentity`. Explicit kind/id overloads are obsolete.

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
| `AddCacheOrchestrator` / `UseCacheOrchestrator` | `CacheOrchestrator.DependencyInjection` |
| `ICacheOrchestratorBuilder` / `ICacheBackendRegistrar` | `CacheOrchestrator.DependencyInjection` / `CacheOrchestrator.Backends` |
| `AddCacheOrchestratorFusionCache` | `CacheOrchestrator.DependencyInjection` (FusionCache package) |
| `AddCacheOrchestratorHybridCache` | `CacheOrchestrator.DependencyInjection` (HybridCache package) |
| `AddRedisBackend` / `RedisCacheBackendRegistrar` | `CacheOrchestrator.Redis` |
| `CacheOutputWithDomain` / `CacheOutputWithDomainTemplate` / `CacheOutputWithDomainAttribute` | `CacheOrchestrator.OutputCache` |
| `[CacheDomain("…")]` | `CacheOrchestrator.OutputCache` |
| `IDomainFusionCache` / `EntityCache` / `EntitySet` / `EntityFootprint` | `CacheOrchestrator.FusionCache` |
| `ICacheVaryContributor` / `CacheVaryMaterializer` / `ICacheVaryBuilder` | `CacheOrchestrator.Vary` |
| `AuthBypassMode` / `DomainAuthEvaluator` | `CacheOrchestrator.Configuration` |
| `IDomainCacheOptionsProvider` / `DomainCacheOptions` / `DomainName` | `CacheOrchestrator.Configuration` |
| `ICacheOrchestratorInvalidator` / `ICacheInvalidationObserver` / `CacheInvalidationResult` | `CacheOrchestrator.Invalidation` |
| `CacheTags` | `CacheOrchestrator.Configuration` |
| Health: `AddCacheOrchestrator` on `IHealthChecksBuilder` | `CacheOrchestrator.Diagnostics` |
| `MapCacheOrchestratorAdmin` / Admin API | `CacheOrchestrator.DependencyInjection` / `CacheOrchestrator.Admin` |
| `AddHttpClusterBus` / `MapCacheOrchestratorHttpBus` | `CacheOrchestrator.Bus` |
| `AddCacheOrchestratorEfCoreInvalidation` / `AddCacheOrchestratorInvalidation` / `[CacheEntity]` | `CacheOrchestrator.EFCore` |
| `IClusterCommandBus` / `IClusterMembership` / `IInstanceIdProvider` | `CacheOrchestrator.Cluster` |
| Admin Console App fan-out host | `src/CacheOrchestrator.AdminConsole` (`AdminConsole` config) |

There is **no** `CacheOrchestrator.Abstractions` folder — interfaces sit beside implementations (`Backends`, `FusionCache`, `Diagnostics`, …).

**Visibility:** default implementations (`DomainFusionCacheService`, `DomainCacheOptionsProvider`, `CacheOrchestratorInvalidator`, health check types, options validator, MVC convention) are **`internal`**. Apps use interfaces + DI. Unit tests use `InternalsVisibleTo`.

## Config vs runtime naming

| JSON / options binding | Runtime `DomainCacheOptions` |
|------------------------|------------------------------|
| `OutputCacheTtlSeconds` (int) | `OutputTtl` (TimeSpan) |
| `FusionCacheSoftTtlSeconds` (int) | `FusionCacheSoftTtl` (TimeSpan) |
| `FusionCacheHardTtlSeconds` (int) | `FusionCacheHardTtl` (TimeSpan) |
| `FusionCacheFailSafeSeconds` (int) | `FusionCacheFailSafe` (TimeSpan) |

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
src/CacheOrchestrator/          core (InMemory only; no Redis/Bus packages)
  Configuration/     options, domain resolution, headers
  OutputCache/       policy, attributes, endpoint extensions
  FusionCache/       data cache API + key gen
  Vary/              shared OC↔Fusion vary materializer + ICacheVaryContributor
  Backends/          ICacheBackendRegistrar, registration contexts, InMemory
  Invalidation/      tag purge
  Cluster/           Null bus/membership, InstanceId, command handler (HTTP in Bus package)
  Diagnostics/       metrics, activities, health
  Admin/             Admin API (feature-flagged; stats, invalidate, version/TTL overlay)
  DependencyInjection/ AddCacheOrchestrator, MapCacheOrchestratorAdmin, ICacheOrchestratorBuilder
  Utilities/
src/CacheOrchestrator.Redis/    Redis package: registrar, RedisConnectionOptions, config resolve, validation
src/CacheOrchestrator.Bus/      HTTP cluster bus + Static membership + cluster receive endpoints
src/CacheOrchestrator.EFCore.Invalidation/  SaveChanges interceptor (not an EF cache provider)
src/CacheOrchestrator.AdminConsole/    Admin Console App host (fan-out, UI, Scalar; not packable)
tests/CacheOrchestrator.UnitTests/          core library unit tests
tests/CacheOrchestrator.Redis.UnitTests/
tests/CacheOrchestrator.Bus.UnitTests/
tests/CacheOrchestrator.EFCore.Invalidation.UnitTests/
tests/CacheOrchestrator.AdminConsole.UnitTests/
tests/CacheOrchestrator.IntegrationTests/
tests/CacheOrchestrator.Benchmarks/
samples/
docs/                human technical docs
```

## Docs for humans

Three tiers (do not put reference into the root README):

- **Product:** `README.md` — overview and quick start only
- **Guide:** `docs/guide/` (concepts, topologies, operations) + `docs/getting-started.md` + FAQ + Minimal sample
- **Reference:** topic pages under `docs/` (configuration, keys, deployment, …); hub: `docs/README.md`

Contributor / security: `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`.  
Keep docs in sync when renaming public types or config keys. Put a change in the matching tier.

Branch worklog: copy `docs/templates/worklog-template.md` (do not commit the filled copy). Summary → PR title and description; the rest is the PR appendix. Record **net outcomes** only — no chat, no rejected alternatives, no draft paths. A work item must still make sense a month later without the conversation.

Do **not** edit `CHANGELOG.md` unless the user asks. User-facing notes go in the worklog Changelog. The maintainer updates `CHANGELOG.md` from merged PR worklogs.

## Safe change checklist

1. Build solution (`CacheOrchestrator.slnx`)  
2. Run unit tests (`tests/CacheOrchestrator.UnitTests` plus Redis / Bus / EFCore.Invalidation unit-test projects when those packages change)  
3. Update sample if public API or config surface changes  
4. Avoid introducing `CacheOrchestrator.Abstractions` again  
5. Avoid reintroducing Slovenian comments or `ct` as public parameter names  

## Out of scope unless asked

- Rewriting README style with marketing fluff  
- Changing NuGet package metadata placeholders without explicit request  
- Committing secrets or real Redis production endpoints  
