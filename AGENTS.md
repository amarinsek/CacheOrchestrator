# AGENTS.md — CacheOrchestrator

Context for AI coding agents working in this repository.

## What this project is

**CacheOrchestrator** is domain-based caching for ASP.NET Core: define rules once per domain in configuration, then apply them on endpoints with a single attribute or extension. It orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

Internally it wires:

1. **Output Cache** — full HTTP response caching (ASP.NET Core)  
2. **FusionCache** — application object caching (ZiggyCreatures; L1 memory ± L2 distributed cache via pluggable backends)  
3. **Client Cache-Control** — browser/CDN headers (+ optional Client Cache Schedule)

Domains are named groups of data that share TTLs, providers, client headers, and version stamps.

- Package / project: `src/CacheOrchestrator` (core; InMemory only)  
- Redis package: `src/CacheOrchestrator.Redis` (`AddRedisBackend`)  
- Admin App: `src/CacheOrchestrator.Admin` (fan-out UI/API; not a NuGet package)  
- Target frameworks: `net8.0` and `net10.0` (multi-target, see `.csproj`)  
- Version: **MinVer** from Git tags `v*` (do not hardcode `<Version>` in Directory.Build.props)  
- Samples: `samples/CacheOrchestrator.Minimal` (1-minute InMemory), `samples/CacheOrchestrator.Sample` (playground; Redis package)  
- Tests: `tests/CacheOrchestrator.UnitTests` (net8+net10), `IntegrationTests` (net10 + Testcontainers Redis), `Benchmarks`

## Non-goals

- Not a generic cache façade for non-HTTP apps without `HttpContext`  
- Not a replacement for FusionCache features — it **configures and scopes** them  
- Does not own Redis topology/ops beyond connection options  

## Mental model

```
Domain (config name)
  → DomainCacheOptions (resolved snapshot)
      → DomainOutputCachePolicy (HTTP)
      → IDomainFusionCache.GetOrSetAsync (data)
      → tags domain:{name} for invalidation
```

**Domain for FusionCache** (`IDomainFusionCache.GetOrSetAsync`):

1. Options already on request (Output Cache policy usually set them via `.CacheOutputWithDomain` / `[CacheDomain]`).  
2. Else explicit overload `GetOrSetAsync(http, domain, factory)`.  
3. Else resolve domain from endpoint metadata (`DomainOutputCachePolicy` / `CacheDomainAttribute`) and `EnsureDomainOptions`.  
4. Else factory runs **uncached**.

Happy path: **no** manual `EnsureDomainOptions` when OC domain is on the endpoint.  
**Fusion-only** endpoints: use domain overload or `EnsureDomainOptions`.

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
| `AddRedisBackend` / `RedisCacheBackendRegistrar` | `CacheOrchestrator.Redis` |
| `CacheOutputWithDomain` / `CacheOutputWithDomainTemplate` / `CacheOutputWithDomainAttribute` | `CacheOrchestrator.OutputCache` |
| `[CacheDomain("…")]` | `CacheOrchestrator.OutputCache` |
| `IDomainFusionCache` | `CacheOrchestrator.FusionCache` |
| `IDomainCacheOptionsProvider` / `DomainCacheOptions` / `DomainName` | `CacheOrchestrator.Configuration` |
| `ICacheOrchestratorInvalidator` / `ICacheInvalidationObserver` / `CacheInvalidationResult` | `CacheOrchestrator.Invalidation` |
| `CacheTags` | `CacheOrchestrator.Configuration` |
| Health: `AddCacheOrchestrator` on `IHealthChecksBuilder` | `CacheOrchestrator.Diagnostics` |
| `MapCacheOrchestratorAdmin` / Local Admin API | `CacheOrchestrator.DependencyInjection` / `CacheOrchestrator.Admin` |
| Admin App fan-out host | `src/CacheOrchestrator.Admin` (`CacheAdmin` config) |

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
src/CacheOrchestrator/          core (InMemory only; no Redis packages)
  Configuration/     options, domain resolution, headers
  OutputCache/       policy, attributes, endpoint extensions
  FusionCache/       data cache API + key gen
  Backends/          ICacheBackendRegistrar, registration contexts, InMemory
  Invalidation/      tag purge
  Diagnostics/       metrics, activities, health
  Admin/             Local Admin API (feature-flagged; stats, invalidate, version/TTL overlay)
  DependencyInjection/ AddCacheOrchestrator, MapCacheOrchestratorAdmin, ICacheOrchestratorBuilder
  Utilities/
src/CacheOrchestrator.Redis/    Redis package: registrar, RedisConnectionOptions, config resolve, validation
src/CacheOrchestrator.Admin/    Admin App host (fan-out, UI, Scalar; not packable)
tests/
samples/
docs/                human technical docs
```

## Docs for humans

- Quick start: `README.md` + `docs/getting-started.md` + Minimal sample
- Doc index (paths): `docs/README.md`; FAQ: `docs/faq.md`
- Contributor / security: `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`
- Keep docs in sync when renaming public types or config keys

## Safe change checklist

1. Build solution (`CacheOrchestrator.slnx`)  
2. Run unit tests (`tests/CacheOrchestrator.UnitTests`)  
3. Update sample if public API or config surface changes  
4. Avoid introducing `CacheOrchestrator.Abstractions` again  
5. Avoid reintroducing Slovenian comments or `ct` as public parameter names  

## Out of scope unless asked

- Rewriting README style with marketing fluff  
- Changing NuGet package metadata placeholders without explicit request  
- Committing secrets or real Redis production endpoints  
