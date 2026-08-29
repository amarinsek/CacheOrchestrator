# Configuration reference

> **Reference.** Product overview: [root README](../../README.md). Orientation: [Guide](../guide/README.md). Catalog: [documentation index](../README.md). Packages: [packages](../guide/packages.md).

Schema for the `Cache` configuration section (or another root you pass to `AddCacheOrchestrator`).

- Section name defaults to **`Cache`**. Override with a named argument, e.g. `services.AddCacheOrchestrator(configuration, configSection: "MySection")` (same for `AddCacheOrchestratorAspNetCore`).
- Domain lifetimes use nested objects (`DataCache`, `OutputCache`, `ClientCache`, Fusion-only `FusionCache`) with **integer seconds** (`TtlSeconds`, …) — not TimeSpan strings.
- Runtime snapshots often expose `TimeSpan` for server TTLs; client max-age fields stay `int` seconds.

For “which package do I need?”, start with [packages](../guide/packages.md), not this page.

## Table of Contents

- [Root shape](#root-shape)
- [Root properties and package ownership](#root-properties-and-package-ownership)
- [Provider options (`OutputCache` / `DataCacheInstances` entry)](#provider-options-outputcache-datacacheinstances-entry)
- [Redis connection (`CacheOrchestrator.Redis` package)](#redis-connection-cacheorchestratorredis-package)
- [Distributed resilience (`Cache:Distributed`)](#distributed-resilience-cachedistributed)
- [Domain settings (`DomainDefaults` and each `Domains` entry)](#domain-settings-domaindefaults-and-each-domains-entry)
- [Admin API (`Cache:Admin`)](#admin-api-cacheadmin)
- [Cluster bus (`Cache:Cluster:Bus`)](#cluster-bus-cacheclusterbus)
- [EF Core invalidation (`Cache:EFCore:Invalidation`)](#ef-core-invalidation-cacheefcoreinvalidation)
- [Validation](#validation)
- [Runtime model](#runtime-model)
- [Domain name normalization](#domain-name-normalization)
- [Example domains](#example-domains)

## Root shape

```json
{
  "Cache": {
    "Namespace": "app-cache",
    "InstanceId": "",
    "Distributed": { },
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "DomainDefaults": {
      "DataCache": { "Enabled": true, "TtlSeconds": 3800 },
      "OutputCache": { "Enabled": true, "TtlSeconds": 3700 },
      "ClientCache": { "Cacheability": "Public", "TtlSeconds": 3600, "TtlMinSeconds": 60 },
      "FusionCache": { "HardTtlSeconds": 43200, "FailSafeSeconds": 86400 }
    },
    "Domains": {
      "products": { }
    },
    "Admin": { },
    "Cluster": { "Bus": { } }
  }
}
```

## Root properties and package ownership

| Property | Owner | Type | Default | Description |
|----------|-------|------|---------|-------------|
| `Namespace` | Core + consuming adapters | string | `app-cache` | Global key prefix; isolates multi-app shared stores **and** cluster command isolation |
| `InstanceId` | Core | string | machine name | Stable process id (management, cluster anti-echo, diagnostics) |
| `EmitDiagnosticsHeaders` | ASP.NET Core | bool | `true` | When `true`, emit client-visible diagnostic headers (`X-Cache`). Set `false` in production if you do not want hit/miss/domain details exposed to clients. Does **not** affect metrics, tracing, or logs. |
| `Metrics` | ASP.NET Core | object | see below | HTTP meter label options (OpenTelemetry / Prometheus) |
| `Distributed` | Core / Data Cache provider | object | soft 1s / hard 2s / circuit 5s | L2 resilience for **non-InMemory** Data Cache providers (Fusion Redis, …) |
| `OutputCache` | ASP.NET Core | object | Provider `InMemory` | Output Cache provider + optional namespace |
| `DataCacheInstances` | Core / Data Cache provider | map | `default` instance `InMemory` | Named Data Cache engines (Fusion L1±L2; Hybrid supports only `default`) |
| `DomainDefaults` | Core + feature packages | object | — | Fallbacks for every domain; each package binds its owned nested settings |
| `Domains` | Core + feature packages | map | — | Per-domain overrides (keys are domain names) |
| `Admin` | Core + ASP.NET Core / HttpBus adapters | object | disabled | Management policy plus Admin API route/auth settings (see [admin.md](admin.md)) |
| `Cluster` | Core command handling + HttpBus transport | object | bus disabled | Cluster command and optional HttpBus settings (see below / [cluster-bus.md](cluster-bus.md)) |

The JSON tree is stable even though no single public Core options type owns every row. Core, ASP.NET Core, FusionCache, and HttpBus bind package-specific projections from the same section.

### Metrics (ASP.NET Core package)

Bound from `Cache:Metrics`. Controls labels on the `CacheOrchestrator` meter (not Admin Console App storage).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeEndpointLabel` | bool | `true` | When `true`, Output Cache and Data Cache instruments include a stable `route` tag (`METHOD` + route template, the same shape as Admin endpoint keys). Set `false` to emit only `domain` / `result` and lower Prometheus cardinality. Keep the same value on all cluster nodes. |

Endpoint time series need a scrape of the meter and Admin Console App Metrics store; empty charts mean no samples in range (traffic, flag off for part of the window, or label mismatch)—not a separate “feature bit” from Prometheus history.

**Redis connection settings are not part of core options.** They are owned by **CacheOrchestrator.Redis** (see below).

Effective namespaces:

- Output: `OutputCache.Namespace` ?? `{Namespace}-oc`
- Data Cache **`default`** instance: `DataCacheInstances.default.Namespace` ?? `{Namespace}-fc`
  (**no** `-default` suffix — keys look like `app-cache-fc:…`, not `app-cache-fc-default:…`.)
- Data Cache **named** instance (e.g. `pii`): `…Namespace` ?? `{Namespace}-fc-{name}`

## Provider options (`OutputCache` / `DataCacheInstances` entry)

| Property | Description |
|----------|-------------|
| `Provider` | Must match a registered backend (`InMemory` always; `Redis` after `AddRedisBackend()`; custom via `AddOutputCacheBackend` or `AddFusionCacheBackend`) |
| `Namespace` | Optional key prefix override |
| `{Backend}.*` | Backend-specific block (read by the backend package, e.g. `Redis`, `SqlServer`) |

## Redis connection (`CacheOrchestrator.Redis` package)

Read **only** after a Redis backend is registered. The binding implementation lives in the transitive `CacheOrchestrator.Redis.Shared` support package and is intentionally not public API.

| Section | Role |
|---------|------|
| `Cache:Redis` | Global fallback connection |
| `Cache:OutputCache:Redis` | Override for Output Cache store |
| `Cache:DataCacheInstances:{name}:Redis` | Override for one Data Cache instance |

| Property | Default | Description |
|----------|---------|-------------|
| `Configuration` | — | StackExchange.Redis connection string |
| `ConnectTimeout` | 5000 | ms |
| `SyncTimeout` | 5000 | ms |
| `KeepAliveSeconds` | 60 | TCP keep-alive |

## Distributed resilience (`Cache:Distributed`)

Core setting. Applied when a Data Cache instance `Provider` is **not** `InMemory` (Fusion L2 path).

| Property | Default | Description |
|----------|---------|-------------|
| `SoftTimeoutSeconds` | 1 | Distributed soft timeout |
| `HardTimeoutSeconds` | 2 | Distributed hard timeout |
| `CircuitBreakerSeconds` | 5 | Distributed circuit breaker |

## Domain settings (`DomainDefaults` and each `Domains` entry)

Nullable fields **inherit** from defaults (then hard-coded library defaults). Nested sections merge the same way.

### Resolution precedence

For one domain, the Core and ASP.NET Core options providers resolve values in this order, from highest to lowest priority:

1. Runtime overlay created through the Admin API, for settings that support overlays.
2. `Cache:Domains:{name}`.
3. `Cache:DomainDefaults`.
4. Library defaults.

Core produces an immutable `DomainCacheOptions` snapshot for domain identity and Data Cache policy. ASP.NET Core composes it into `DomainHttpCacheOptions` for Output Cache, Client Cache, authentication, vary, ETag, and HTTP Data Cache key policy. A request reuses its HTTP snapshot; configuration reloads and later overlays affect newly resolved requests, not values already attached to the current request. Provider and connection sections such as `OutputCache`, `DataCacheInstances`, and `Redis` are host composition settings and do not participate in this per-domain merge.

### Nested sections

| JSON section | Portable? | Meaning |
|--------------|-----------|---------|
| `DataCache` | Core + AspNet | Core: enable, instance, TTL. AspNet HTTP keys under the same object: `RespectNoStore`, `VaryOnEncoding`, `VaryOnPublicAddress` — Fusion **or** Hybrid |
| `OutputCache` | AspNet | HTTP response cache TTL and Output Cache behavior |
| `ClientCache` | AspNet | Browser / CDN `Cache-Control` (+ schedule) |
| `FusionCache` | Fusion package only | Hard TTL, fail-safe, factory timeouts, jitter, … |

### Feature flags and vary (domain root)

| Property | Default* | Description |
|----------|----------|-------------|
| `AuthBypassMode` | `AuthenticatedOrAuthorization` | `Never` / `AuthenticatedIdentityOnly` / `AuthorizationHeaderOnly` / `AuthenticatedOrAuthorization` |
| `VaryOutputCacheByUser` | true | When authentication is not bypassed, vary Output Cache (and Data Cache when intentional) by user, claims, or API-key hash |
| `TreatAuthorizationAsAuthSignal` | true | `Authorization` counts as auth signal for OR-mode |
| `AuthVaryIncludeAuthorizationHash` | true | Hash `Authorization` into auth-user when no identity |
| `VaryByAuthClaims` | null | Claim types for auth-user material |
| `DataCacheRespectAuthBypass` | **true** | Data Cache skips when the Output Cache authentication bypass would fire. Set `false` only for caller-independent shared data. |
| `VaryByAccept` / `VaryByAcceptLanguage` | true / false | Content negotiation / locale vary |
| `AcceptNormalizationList` / `AcceptLanguageNormalizationList` | null | Prefer-lists when those vary flags are on — [vary.md](vary.md) |
| `VaryByHeaders` / `VaryByCookies` | null | Header/cookie **name** allowlists — [vary.md](vary.md) |
| `VaryByQueryKeys` | null | `null` = all non-tracking query keys; `[]` = none; non-empty = allowlist |
| `IgnoreQueryKeys` | null | Extra deny list on top of built-in tracking prefixes |
| `EmitResponseVary` | **true** | Emit HTTP response `Vary` for non-secret headers (set `false` to omit) |

\*After merge with defaults.

### Versioning

| Property | Description |
|----------|-------------|
| `Version` | Bulk invalidation stamp string (e.g. "v1", "2026-08"). Missing → stable default "1" + warning log (stable keys, no auto-invalidate on restart) |

### `DataCache` (portable Core)

| Property | Default* | Description |
|----------|----------|-------------|
| `Enabled` | true | Enable Data Cache for the domain |
| `Instance` | `default` | Key in `DataCacheInstances` |
| `TtlSeconds` | `3800` | Logical Data Cache TTL in seconds (Fusion soft/`Duration`; Hybrid expiration) |

### `DataCache` (HTTP-only AspNetCore)

Same JSON object as portable `DataCache`; bound by ASP.NET Core:

| Property | Default* | Description |
|----------|----------|-------------|
| `RespectNoStore` | true | Skip Data Cache when request has `Cache-Control: no-store` |
| `VaryOnEncoding` | true | Include Accept-Encoding in the Data Cache key |
| `VaryOnPublicAddress` | true | Include scheme + host in the Data Cache key |

### `OutputCache` (nested under domain)

| Property | Default* | Description |
|----------|----------|-------------|
| `Enabled` | true | Enable Output Cache for the domain |
| `TtlSeconds` | `3700` | Server-side output entry TTL in seconds |
| `VaryByHost` | **true** | Output Cache `VaryByHost` (host + port) |
| `CacheableStatusCodes` | `[200]` | Status codes allowed to store |
| `EncodingNormalizationList` | `br`, `gzip` | Prefer these Accept-Encoding values |
| `ETagMode` | `Version` | `Version`, `None`, or `Resource`. How Output Cache policy sets the HTTP `ETag` header. See [domain-profiles.md](../guide/domain-profiles.md). |

### `ClientCache` — [Client Cache Schedule](../guide/client-cache-schedule.md)

| Property | Default* | Description |
|----------|----------|-------------|
| `Cacheability` | `Public` | `Public`, `Private`, `NoStore` |
| `TtlSeconds` | `3600` | Target max-age far from schedule; `0` emits `max-age=0` and disables the schedule ramp |
| `TtlMinSeconds` | `60` | Floor max-age near/at update and during hold; `0` is valid and the value is ignored when `TtlSeconds` is `0` |
| `ScheduledUpdateUtc` | null | Planned cutover; linear ramp of max-age toward min |
| `MustRevalidateNearUpdate` | false | Append `must-revalidate` at min floor |
| `ForcePrivateWhenAuthenticated` | true | Force client Private for signed-in Identity + Public |

See **[Client Cache Schedule](../guide/client-cache-schedule.md)** for phases, formula, and operational playbook.

### `FusionCache` (Fusion package only)

Bound from `Cache:DomainDefaults:FusionCache` / `Cache:Domains:{name}:FusionCache` by **CacheOrchestrator.FusionCache**. Ignored when Hybrid is the `IDataCacheProvider`.

| Property | Default* | Description |
|----------|----------|-------------|
| `HardTtlSeconds` | `43200` | Caps soft/`DataCache.TtlSeconds` if soft &gt; hard |
| `FailSafeSeconds` | `86400` | Fail-safe max duration (seconds) |
| `EagerRefreshRatio` | 0.9 | Eager refresh threshold. **`0` = disabled**; values in `(0, 1)` allowed; `>= 1` fails validation |
| `JitterSeconds` | `60` | Max jitter on duration (seconds) |
| `FactorySoftTimeoutSeconds` | `1` | Factory soft timeout (seconds) |
| `FactoryHardTimeoutSeconds` | `5` | Factory hard timeout (seconds) |
| `MaxItemBytes` | 0 | Memory size limit; 0 = unlimited |
| `AllowBackgroundDistributed` | true | Fusion may complete L2 I/O in the background |
| `AllowBackgroundBackplane` | true | Fusion may publish backplane messages in the background |

## Admin API (`Cache:Admin`)

Opt-in ops API on each application process. **Disabled by default** (no routes, no live counters).  
Details: [admin.md](admin.md). Map with `MapCacheOrchestratorAdmin()`.

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `false` | When false, Null stats collector and no admin routes |
| `ApiKey` | empty | `X-Cache-Admin-Key`; empty + Enabled = open (dev only) |
| `RoutePrefix` | `/cache-admin/local` | Base path for Admin API (and cluster receive path prefix) |
| `TrackEndpoints` | `true` | Per-route counters when Enabled |
| `TrackLatency` | `false` | Sum/count factory latency on Admin `/stats` (extra cost) |
| `TrackResultSize` | `false` | Sum/count factory result size on Admin `/stats` (string / byte[] / seekable stream). Does **not** gate the OTEL `factory.result_size` histogram. |

Process id is **`Cache:InstanceId`** (root), not under `Admin`.

Admin Console App (`AdminConsole` section) is configured only in `src/CacheOrchestrator.AdminConsole` — see [admin.md](admin.md#admin-console-app-process).

## Cluster bus (`Cache:Cluster:Bus`)

Optional multi-instance **command** distribution. Requires package **`CacheOrchestrator.HttpBus`** + `AddHttpClusterBus()` + `MapCacheOrchestratorHttpBus()`.  
Without the package, core registers a Null bus (no peer traffic). Details: **[cluster-bus.md](cluster-bus.md)**.

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `false` | When false, no peer publish even if the HttpBus package is registered |
| `Membership` | `Null` | `Null` · `Static` · `ServiceDiscovery` |
| `PeerTimeoutMs` | `2000` | Per-peer HTTP timeout (clamped **100–120_000** ms at publish) |
| `MaxParallelism` | `32` | Max concurrent peer deliveries (clamped **1–64**) |
| `DedupeWindowSeconds` | `330` | Receive-side `CommandId` window; must cover `CommandMaxAgeSeconds + ClockSkewSeconds` while HttpBus is enabled |
| `ApiKey` | empty | `X-Cache-Admin-Key` for receive endpoints; falls back to `Admin:ApiKey`; required when enabled unless unauthenticated mode is explicitly allowed |
| `AllowUnauthenticated` | `false` | Explicit opt-in for an open bus on an isolated development network |
| `CommandMaxAgeSeconds` | `300` | Reject commands older than this receive-side freshness window |
| `ClockSkewSeconds` | `30` | Maximum accepted future clock skew |
| `Static.Instances[]` | `[]` | `{ Id, Url }` peers when Membership is Static |
| `ServiceDiscovery.ServiceName` | empty | Logical name for SD (normalized to `http://{name}` when bare) |
| `ServiceDiscovery.DefaultScheme` | `http` | Scheme for peer URLs |
| `ServiceDiscovery.CacheSeconds` | `15` | In-process peer list cache (clamped **0–300**; **`0` = no cache**) |

ServiceDiscovery also needs host config endpoints under `Services:{name}` (see [cluster-bus.md](cluster-bus.md#servicediscovery-k8s--aspire--config-endpoints)).

## EF Core invalidation (`Cache:EFCore:Invalidation`)

Optional. Requires package **`CacheOrchestrator.EFCore.Invalidation`**. Type → `(domain, entityKind)` mapping is **code** (`[CacheEntity]`, Fluent `CacheInvalidate`, or `Map<T>`), not this section. Details: [ef-core-invalidation.md](ef-core-invalidation.md).

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `true` | Master switch |
| `BulkThreshold` | `20` | Id count that triggers `OnBulk` |
| `OnBulk` | `Kind` | `Entities` · `Kind` · `Domain` (domain wipes every kind in that policy group) |

## Validation

Package-owned validators run on start (`ValidateOnStart`). Core validates portable domain and Data Cache configuration; ASP.NET Core validates Output Cache, Client Cache, HTTP Data Cache behavior, authentication, vary, and ETag settings; provider packages validate their own sections.

- `DataCacheInstances` must contain **`default`**
- `DomainDefaults.DataCache.Instance` and each domain `DataCache.Instance` must name a registered instance (default `"default"`)
- Output Cache provider must match an `IOutputCacheBackendRegistrar` (`InMemory`, `Redis`, or custom via `AddOutputCacheBackend`)
- Fusion instance providers must match an `IFusionCacheBackendRegistrar` (`InMemory`, `Redis`, or custom via `AddFusionCacheBackend`)
- Redis provider requires a connection string (`Cache:Redis:Configuration` or the scoped `OutputCache:Redis` / `DataCacheInstances:{name}:Redis` override)
- Negative TTLs and Fusion durations fail; `FusionCache.EagerRefreshRatio` must be in `[0, 1)` when present
- Effective Fusion factory timeouts must be positive with soft &lt; hard; fail-safe must be disabled (`0`) or cover the effective Data Cache duration
- Effective `ClientCache.TtlMinSeconds` must not exceed a positive `TtlSeconds`, including inherited default/domain combinations; it is ignored when `TtlSeconds` is `0`
- An enabled HttpBus requires an API key unless unauthenticated mode is explicitly allowed; command freshness and clock-skew windows must be valid
- Allowlists have max lengths (headers, cookies, query, claims, Accept lists)
- `AuthBypassMode` must be a defined enum value  

## Runtime model

Resolved settings use two immutable runtime snapshots:

- Core `DomainCacheOptions` contains `Domain`, `Version`, `VersionHex`, `DataCacheEnabled`, `DataCacheInstanceName`, `DataCacheTtl`, and `DataCacheNamespace`.
- ASP.NET Core `DomainHttpCacheOptions` exposes the Core snapshot through `CoreOptions` and adds the HTTP-owned settings.

Nested JSON seconds map to:

| JSON | Runtime |
|------|---------|
| `OutputCache.TtlSeconds` | `DomainHttpCacheOptions.OutputTtl` (`TimeSpan`) |
| `DataCache.TtlSeconds` | `DomainCacheOptions.DataCacheTtl` (`TimeSpan`) |
| `ClientCache.TtlSeconds` / `TtlMinSeconds` | `DomainHttpCacheOptions.ClientTtlSeconds` / `ClientTtlMinSeconds` (`int`) |
| `DataCache.Instance` | `DomainCacheOptions.DataCacheInstanceName` |
| `DataCache.Enabled` | `DomainCacheOptions.DataCacheEnabled` |
| `OutputCache.Enabled` | `DomainHttpCacheOptions.OutputCacheEnabled` |
| `ClientCache.Cacheability` | `DomainHttpCacheOptions.ClientCacheability` |

Fusion-only knobs stay on `DomainFusionCacheSettings` (Fusion package), not on Core `DomainCacheOptions`.

The JSON shape remains unified. `Cache:DomainDefaults` and `Cache:Domains:{domain}` are bound to package-owned models from the same configuration section:

- Core owns `Version` and portable `DataCache.Enabled`, `Instance`, and `TtlSeconds`.
- ASP.NET Core owns `OutputCache`, `ClientCache`, authentication/vary/ETag settings, and HTTP-only `DataCache.RespectNoStore`, `VaryOnPublicAddress`, and `VaryOnEncoding`.
- FusionCache owns the nested `FusionCache` tuning section.

This split does not change any `appsettings.json` key.

## Domain name normalization

`DomainName.Normalize` (public helper):

- lowercases  
- allows `a-z`, `0-9`, `-`, `:`, `_`, `@`  
- replaces other chars with `-`, collapses dashes  
- empty → `default`  

**Prefer already-normalized names** in `Domains` keys, `[CacheDomain("…")]`, and `.CacheOutputWithDomain("…")` (lowercase + allowed characters only). That hits the zero-allocation `IsNormalized` fast path on every request.

Case-only variants such as `MyStore` / `Products` still work: `Domains` is `OrdinalIgnoreCase`, and startup validation does **not** fail the host. Options validation logs a **warning** once at startup (and on options reload) recommending the normalized form. Keys that change beyond case after normalization (spaces, invalid characters, collapsed dashes) **fail** validation, because runtime lookup uses the normalized name and would miss that dictionary entry. Keys that normalize to `default` unintentionally (for example `!!!`) also fail.

Resource ids: `DomainName.NormalizeResourceId` trims surrounding whitespace but otherwise preserves opaque identity material, including case and punctuation. GUID input is canonicalized to lowercase `D` format. Null/whitespace becomes empty, **not** `default`. Visible key and tag segments are percent-encoded by their builders.

Entity kinds: `DomainName.NormalizeEntityKind` uses the restricted normalized-name rules because kinds are schema names rather than opaque identifiers. Unusable kinds do not share the domain name `default`.

## Example domains

See **[domain-profiles.md](../guide/domain-profiles.md)** for full **osm-tiles** (snapshot) and **product-detail** (CRUD) recipes.

## Related

- [packages.md](../guide/packages.md)  
- [Guide](../guide/README.md)  
- [cache-keys.md](cache-keys.md) — Namespace and key composition  
- [architecture.md](../contributor/architecture.md)  
- [domain-profiles.md](../guide/domain-profiles.md)  
- [invalidation.md](invalidation.md)  
