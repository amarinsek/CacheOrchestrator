# Configuration reference

> **Reference.** Product overview: [root README](../../README.md). Orientation: [Guide](../guide/README.md). Catalog: [documentation index](../README.md). Packages: [packages](../guide/packages.md).

Schema for the `Cache` configuration section (or another root you pass to `AddCacheOrchestrator`).

- Section name defaults to **`Cache`**. Override with `AddCacheOrchestrator(config, "MySection")`.
- Domain lifetimes use nested objects (`DataCache`, `OutputCache`, `ClientCache`, Fusion-only `FusionCache`) with **integer seconds** (`TtlSeconds`, …) — not TimeSpan strings.
- Runtime snapshots often expose `TimeSpan` for server TTLs; client max-age fields stay `int` seconds.

For “which package do I need?”, start with [packages](../guide/packages.md), not this page.

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

## Root properties (core package)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Namespace` | string | `app-cache` | Global key prefix; isolates multi-app shared stores **and** cluster command isolation |
| `InstanceId` | string | machine name | Stable process id (Admin, cluster bus anti-echo, diagnostics) |
| `EmitDiagnosticsHeaders` | bool | `true` | When `true`, emit client-visible diagnostic headers (currently `X-Cache`). Set `false` in production if you do not want hit/miss/domain details exposed to clients. Does **not** affect metrics, tracing, or logs. |
| `Metrics` | object | see below | Meter label options (OpenTelemetry / Prometheus) |
| `Distributed` | object | soft 1s / hard 2s / circuit 5s | L2 resilience for **non-InMemory** data-cache providers (Fusion Redis, …) |
| `OutputCache` | object | Provider `InMemory` | Output Cache provider + optional namespace |
| `DataCacheInstances` | map | `default` instance `InMemory` | Named data-cache engines (Fusion L1±L2 today; Hybrid uses a single DI HybridCache) |
| `DomainDefaults` | object | — | Fallbacks for every domain |
| `Domains` | map | — | Per-domain overrides (keys are domain names) |
| `Admin` | object | disabled | Admin API (see [admin.md](admin.md)) |
| `Cluster` | object | bus disabled | Cluster command bus options (see below / [cluster-bus.md](cluster-bus.md)) |

### Metrics (core package)

Bound from `Cache:Metrics`. Controls labels on the `CacheOrchestrator` meter (not Admin Console App storage).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeEndpointLabel` | bool | `true` | When `true`, OC/DC instruments include a stable `route` tag (`METHOD` + route template, same shape as Admin endpoint keys). Set `false` to emit only `domain` / `result` (lower Prometheus cardinality). Keep the same value on all cluster nodes. |

Endpoint time series need a scrape of the meter and Admin Console App Metrics store; empty charts mean no samples in range (traffic, flag off for part of the window, or label mismatch)—not a separate “feature bit” from Prometheus history.

**Redis connection settings are not part of core options.** They are owned by **CacheOrchestrator.Redis** (see below).

Effective namespaces:

- Output: `OutputCache.Namespace` ?? `{Namespace}-oc`
- Data-cache **`default`** instance: `DataCacheInstances.default.Namespace` ?? `{Namespace}-fc`  
  (**no** `-default` suffix — keys look like `app-cache-fc:…`, not `app-cache-fc-default:…`. The `-fc` suffix is historical.)
- Data-cache **named** instance (e.g. `pii`): `…Namespace` ?? `{Namespace}-fc-{name}`

## Provider options (`OutputCache` / `DataCacheInstances` entry)

| Property | Description |
|----------|-------------|
| `Provider` | Must match a registered backend (`InMemory` always; `Redis` after `AddRedisBackend()`; custom via `AddBackend`) |
| `Namespace` | Optional key prefix override |
| `{Backend}.*` | Backend-specific block (read by the backend package, e.g. `Redis`, `SqlServer`) |

## Redis connection (`CacheOrchestrator.Redis` package)

Bound **only** when you call `AddRedisBackend()`. Types: `RedisConnectionOptions` / `RedisConfiguration` in assembly **CacheOrchestrator.Redis**.

| Section | Role |
|---------|------|
| `Cache:Redis` | Global fallback connection |
| `Cache:OutputCache:Redis` | Override for Output Cache store |
| `Cache:DataCacheInstances:{name}:Redis` | Override for one data-cache instance |

| Property | Default | Description |
|----------|---------|-------------|
| `Configuration` | — | StackExchange.Redis connection string |
| `ConnectTimeout` | 5000 | ms |
| `SyncTimeout` | 5000 | ms |
| `KeepAliveSeconds` | 60 | TCP keep-alive |

## Distributed resilience (`Cache:Distributed`)

Core setting. Applied when a data-cache instance `Provider` is **not** `InMemory` (Fusion L2 path).

| Property | Default | Description |
|----------|---------|-------------|
| `SoftTimeoutSeconds` | 1 | Distributed soft timeout |
| `HardTimeoutSeconds` | 2 | Distributed hard timeout |
| `CircuitBreakerSeconds` | 5 | Distributed circuit breaker |

## Domain settings (`DomainDefaults` and each `Domains` entry)

Nullable fields **inherit** from defaults (then hard-coded library defaults). Nested sections merge the same way.

### Nested sections

| JSON section | Portable? | Meaning |
|--------------|-----------|---------|
| `DataCache` | Yes (Core) | Enable, instance name, TTL, vary / no-store — Fusion **or** Hybrid |
| `OutputCache` | AspNet | HTTP response cache TTL and OC knobs |
| `ClientCache` | AspNet | Browser / CDN `Cache-Control` (+ schedule) |
| `FusionCache` | Fusion package only | Hard TTL, fail-safe, factory timeouts, jitter, … |

### Feature flags and vary (domain root)

| Property | Default* | Description |
|----------|----------|-------------|
| `AuthBypassMode` | `AuthenticatedOrAuthorization` | Prefer this: `Never` / `AuthenticatedIdentityOnly` / `AuthorizationHeaderOnly` / `AuthenticatedOrAuthorization` |
| `VaryOutputCacheByUser` | true | When auth is not bypassed, vary OC (and data cache when intentional) by user / claims / API-key hash |
| `TreatAuthorizationAsAuthSignal` | true | `Authorization` counts as auth signal for OR-mode |
| `AuthVaryIncludeAuthorizationHash` | true | Hash `Authorization` into auth-user when no identity |
| `VaryByAuthClaims` | null | Claim types for auth-user material |
| `DataCacheRespectAuthBypass` | **true** | Data cache skips when auth bypass would fire (set `false` for 2.1-like data-cache-under-Authorization) |
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

### `DataCache` (portable)

| Property | Default* | Description |
|----------|----------|-------------|
| `Enabled` | true | Enable data cache for the domain |
| `Instance` | `default` | Key in `DataCacheInstances` |
| `TtlSeconds` | `3800` | Logical data-cache TTL in seconds (Fusion soft/`Duration`; Hybrid expiration) |
| `RespectNoStore` | true | Skip data cache when request has `Cache-Control: no-store` |
| `VaryOnEncoding` | true | Include Accept-Encoding in the data-cache key |
| `VaryOnPublicAddress` | true | Include scheme + host in the data-cache key |

### `OutputCache` (nested under domain)

| Property | Default* | Description |
|----------|----------|-------------|
| `Enabled` | true | Enable HTTP output cache for domain |
| `TtlSeconds` | `3700` | Server-side output entry TTL in seconds |
| `VaryByHost` | **true** | Output Cache `VaryByHost` (host + port) |
| `CacheableStatusCodes` | `[200]` | Status codes allowed to store |
| `EncodingNormalizationList` | `br`, `gzip` | Prefer these Accept-Encoding values |
| `ETagMode` | `Version` | `Version`, `None`, or `Resource`. How Output Cache policy sets the HTTP `ETag` header. See [domain-profiles.md](../guide/domain-profiles.md). |

### `ClientCache` — [Client Cache Schedule](../guide/client-cache-schedule.md)

| Property | Default* | Description |
|----------|----------|-------------|
| `Cacheability` | `Public` | `Public`, `Private`, `NoStore` |
| `TtlSeconds` | `3600` | Target max-age (seconds) far from schedule (Calm) |
| `TtlMinSeconds` | `60` | Floor max-age (seconds) near/at update and during post-version hold |
| `ScheduledUpdateUtc` | null | Planned cutover; linear ramp of max-age toward min |
| `MustRevalidateNearUpdate` | false | Append `must-revalidate` at min floor |
| `ForcePrivateWhenAuthenticated` | true | Force client Private for signed-in Identity + Public |

See **[client-cache-schedule.md](../guide/client-cache-schedule.md)** for phases, formula, and operational playbook.

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
| `DedupeWindowSeconds` | `60` | Receive-side `CommandId` window (`0` = off) |
| `ApiKey` | empty | `X-Cache-Admin-Key` for receive endpoints; falls back to `Admin:ApiKey` |
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

`CacheOrchestratorOptionsValidator` runs on start (`ValidateOnStart`):

- `DataCacheInstances` must contain **`default`**
- Each domain `DataCache.Instance` must name a registered instance (default `"default"`)
- Provider must be a registered backend (`InMemory`, `Redis` after `AddRedisBackend()`, custom via `AddBackend`)
- Output Cache provider must support an OC store (`SupportsOutputCacheStore`)
- Redis provider requires a connection string (`Cache:Redis:Configuration` or the scoped `OutputCache:Redis` / `DataCacheInstances:{name}:Redis` override)
- Negative TTLs fail; `FusionCache.EagerRefreshRatio` must be in `[0, 1)` when present
- Allowlists have max lengths (headers, cookies, query, claims, Accept lists)
- `AuthBypassMode` must be a defined enum value  

## Runtime model

Resolved settings are **`DomainCacheOptions`** (immutable snapshot). Nested JSON seconds map to:

| JSON | Runtime |
|------|---------|
| `OutputCache.TtlSeconds` | `OutputTtl` (`TimeSpan`) |
| `DataCache.TtlSeconds` | `DataCacheTtl` (`TimeSpan`) |
| `ClientCache.TtlSeconds` / `TtlMinSeconds` | `ClientTtlSeconds` / `ClientTtlMinSeconds` (`int`) |
| `DataCache.Instance` | `DataCacheInstanceName` |
| `DataCache.Enabled` / `OutputCache.Enabled` | `DataCacheEnabled` / `OutputCacheEnabled` |
| `ClientCache.Cacheability` | `ClientCacheability` |

Fusion-only knobs stay on `DomainFusionCacheSettings` (Fusion package), not on Core `DomainCacheOptions`.

## Domain name normalization

`DomainName.Normalize` (public helper):

- lowercases  
- allows `a-z`, `0-9`, `-`, `:`, `_`, `@`  
- replaces other chars with `-`, collapses dashes  
- empty → `default`  

**Prefer already-normalized names** in `Domains` keys, `[CacheDomain("…")]`, and `.CacheOutputWithDomain("…")` (lowercase + allowed characters only). That hits the zero-allocation `IsNormalized` fast path on every request.

Case-only variants such as `MyStore` / `Products` still work: `Domains` is `OrdinalIgnoreCase`, and startup validation does **not** fail the host. Options validation logs a **warning** once at startup (and on options reload) recommending the normalized form. Keys that change beyond case after normalization (spaces, invalid characters, collapsed dashes) **fail** validation, because runtime lookup uses the normalized name and would miss that dictionary entry. Keys that normalize to `default` unintentionally (for example `!!!`) also fail.

Resource ids: `DomainName.NormalizeResourceId` (same character rules; null/whitespace or values with no usable characters such as `!!!` → empty string, **not** `default`).

Entity kinds: `DomainName.NormalizeEntityKind` (same as resource ids). Unusable kinds do not share the domain name `default`.

## Example domains

See **[domain-profiles.md](../guide/domain-profiles.md)** for full **osm-tiles** (snapshot) and **product-detail** (CRUD) recipes.

## Related

- [packages.md](../guide/packages.md)  
- [Guide](../guide/README.md)  
- [cache-keys.md](cache-keys.md) — Namespace and key composition  
- [architecture.md](../contributor/architecture.md)  
- [domain-profiles.md](../guide/domain-profiles.md)  
- [invalidation.md](invalidation.md)  
