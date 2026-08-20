# Configuration reference

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide](guide/README.md). Catalog: [documentation index](README.md).

Every setting under the `Cache` section (or another root you pass to `AddCacheOrchestrator`). Tables below are the schema: property, type or default, and meaning.

Root section name defaults to **`Cache`**. Override with `AddCacheOrchestrator(config, "MySection")`.

## Root shape

```json
{
  "Cache": {
    "Namespace": "app-cache",
    "InstanceId": "",
    "Distributed": { },
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "DomainDefaults": { },
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
| `Distributed` | object | soft 1s / hard 2s / circuit 5s | L2 resilience for **non-InMemory** Fusion providers |
| `OutputCache` | object | Provider `InMemory` | Output Cache provider + optional namespace |
| `FusionCacheInstances` | map | `default` instance `InMemory` | Named FusionCache instances |
| `DomainDefaults` | object | — | Fallbacks for every domain |
| `Domains` | map | — | Per-domain overrides (keys are domain names) |
| `Admin` | object | disabled | Admin API (see [admin.md](admin.md)) |
| `Cluster` | object | bus disabled | Cluster command bus options (see below / [cluster-bus.md](cluster-bus.md)) |

### Metrics (core package)

Bound from `Cache:Metrics`. Controls labels on the `CacheOrchestrator` meter (not Admin Console App storage).

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeEndpointLabel` | bool | `true` | When `true`, OC/FC instruments include a stable `route` tag (`METHOD` + route template, same shape as Admin endpoint keys). Set `false` to emit only `domain` / `result` (lower Prometheus cardinality). Keep the same value on all cluster nodes. |

Endpoint time series need a scrape of the meter and Admin Console App Metrics store; empty charts mean no samples in range (traffic, flag off for part of the window, or label mismatch)—not a separate “feature bit” from Prometheus history.

**Redis connection settings are not part of core options.** They are owned by **CacheOrchestrator.Redis** (see below).

Effective namespaces:

- Output: `OutputCache.Namespace` ?? `{Namespace}-oc`
- Fusion **`default`** instance: `FusionCacheInstances.default.Namespace` ?? `{Namespace}-fc`  
  (**no** `-default` suffix — keys look like `app-cache-fc:…`, not `app-cache-fc-default:…`)
- Fusion **named** instance (e.g. `pii`): `…Namespace` ?? `{Namespace}-fc-{name}`

## Provider options (`OutputCache` / `FusionCacheInstances` entry)

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
| `Cache:FusionCacheInstances:{name}:Redis` | Override for one Fusion instance |

| Property | Default | Description |
|----------|---------|-------------|
| `Configuration` | — | StackExchange.Redis connection string |
| `ConnectTimeout` | 5000 | ms |
| `SyncTimeout` | 5000 | ms |
| `KeepAliveSeconds` | 60 | TCP keep-alive |

## Distributed resilience (`Cache:Distributed`)

Core setting. Applied only when a FusionCache instance `Provider` is **not** `InMemory`.

| Property | Default | Description |
|----------|---------|-------------|
| `SoftTimeoutSeconds` | 1 | Fusion distributed soft timeout |
| `HardTimeoutSeconds` | 2 | Fusion distributed hard timeout |
| `CircuitBreakerSeconds` | 5 | Distributed circuit breaker |

## Domain settings (`DomainDefaults` and each `Domains` entry)

Nullable fields **inherit** from defaults (then hard-coded library defaults).

### Feature flags

| Property | Default* | Description |
|----------|----------|-------------|
| `OutputCacheEnabled` | true | Enable HTTP output cache for domain |
| `FusionCacheEnabled` | true | Enable FusionCache for domain |
| `FusionCacheInstance` | `default` | Which named FusionCache instance to use |
| `AuthBypassMode` | `AuthenticatedOrAuthorization` | Prefer this: `Never` / `AuthenticatedIdentityOnly` / `AuthorizationHeaderOnly` / `AuthenticatedOrAuthorization`. Unset → derived from the obsolete bool (default `true` → this mode). |
| `BypassWhenAuthenticated` | true | **Obsolete** — used only when `AuthBypassMode` is unset (`true` → `AuthenticatedOrAuthorization`, `false` → `Never`) |
| `VaryOutputCacheByUser` | true | When auth is not bypassed, vary OC (and Fusion when intentional) by user / claims / API-key hash |
| `TreatAuthorizationAsAuthSignal` | true | `Authorization` counts as auth signal for OR-mode |
| `AuthVaryIncludeAuthorizationHash` | true | Hash `Authorization` into auth-user when no identity |
| `VaryByAuthClaims` | null | Claim types for auth-user material |
| `FusionRespectAuthBypass` | **true** | Fusion skips cache when auth bypass would fire (set `false` for 2.1-like Fusion-under-Authorization) |
| `ClientForcePrivateWhenAuthenticated` | true | Force client Private for signed-in Identity + Public |
| `VaryByAccept` / `VaryByAcceptLanguage` | false | Content negotiation / locale vary (Accept planned `true` in 3.0) |
| `AcceptNormalizationList` / `AcceptLanguageNormalizationList` | null | Prefer-lists when those vary flags are on — [vary.md](vary.md) |
| `VaryByHeaders` / `VaryByCookies` | null | Header/cookie **name** allowlists — [vary.md](vary.md) |
| `VaryByQueryKeys` | null | `null` = all non-tracking query keys; `[]` = none; non-empty = allowlist |
| `IgnoreQueryKeys` | null | Extra deny list on top of built-in tracking prefixes |
| `EmitResponseVary` | **true** | Emit HTTP response `Vary` for non-secret headers (set `false` to omit) |
| `OutputCacheVaryByHost` | **true** | Output Cache `VaryByHost` (host + port) |

\*After merge with defaults.

### Versioning & ETag

| Property | Description |
|----------|-------------|
| `Version` | Bulk invalidation stamp string (e.g. "v1", "2026-08"). Missing → stable default "1" + warning log (stable keys, no auto-invalidate on restart) |
| `ETagMode` | `Version` (default), `None`, or `Resource`. How Output Cache policy sets the HTTP `ETag` header. See [domain-profiles.md](domain-profiles.md). |

### Output Cache

| Property | Default* | Description |
|----------|----------|-------------|
| `OutputCacheTtlSeconds` | 3700 | Server-side output entry TTL |
| `CacheableStatusCodes` | `[200]` | Status codes allowed to store |
| `EncodingNormalizationList` | `br`, `gzip` | Prefer these Accept-Encoding values |

### FusionCache

| Property | Default* | Description |
|----------|----------|-------------|
| `FusionCacheSoftTtlSeconds` | 3800 | Soft duration (`Duration`) |
| `FusionCacheHardTtlSeconds` | 43200 | Caps soft duration if soft &gt; hard |
| `FusionCacheFailSafeSeconds` | 86400 | Fail-safe max duration |
| `FusionCacheEagerRefreshRatio` | 0.9 | Eager refresh threshold. **`0` = disabled**; values in `(0, 1)` allowed; `>= 1` fails validation |
| `FusionCacheJitterSeconds` | 60 | Max jitter on duration |
| `FusionCacheFactorySoftTimeoutSeconds` | 1 | Factory soft timeout |
| `FusionCacheFactoryHardTimeoutSeconds` | 5 | Factory hard timeout |
| `FusionCacheMaxItemBytes` | 0 | Memory size limit; 0 = unlimited |
| `FusionCacheRespectNoStore` | true | Skip FC when request has `Cache-Control: no-store` |
| `FusionCacheAllowBackgroundDistributed` | true | Fusion may complete L2 I/O in the background |
| `FusionCacheAllowBackgroundBackplane` | true | Fusion may publish backplane messages in the background |
| `FusionCacheVaryOnEncoding` | true | Include Accept-Encoding in key |
| `FusionCacheVaryOnPublicAddress` | true | Include scheme + host in key |

### Client cache (`Cache-Control`) — [Client Cache Schedule](client-cache-schedule.md)

| Property | Default* | Description |
|----------|----------|-------------|
| `ClientCacheability` | `Public` | `Public`, `Private`, `NoStore` |
| `ClientTtlSeconds` | 3600 | Target max-age far from schedule (Calm) |
| `ClientTtlMinSeconds` | 60 | Floor near/after schedule and during post-version hold |
| `ScheduledUpdateUtc` | null | Planned cutover; linear ramp of max-age toward min |
| `ClientMustRevalidateNearUpdate` | false | Append `must-revalidate` at min floor |

See **[client-cache-schedule.md](client-cache-schedule.md)** for phases, formula, and operational playbook.

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

Optional multi-instance **command** distribution. Requires package **`CacheOrchestrator.Bus`** + `AddHttpClusterBus()` + `MapCacheOrchestratorHttpBus()`.  
Without the package, core registers a Null bus (no peer traffic). Details: **[cluster-bus.md](cluster-bus.md)**.

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `false` | When false, no peer publish even if the Bus package is registered |
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

- `FusionCacheInstances` must contain **`default`**
- Each domain `FusionCacheInstance` must name a registered instance (JSON `FusionCacheInstance` → runtime `FusionCacheInstanceName`, default `"default"`)
- Provider must be a registered backend (`InMemory`, `Redis` after `AddRedisBackend()`, custom via `AddBackend`)
- Output Cache provider must support an OC store (`SupportsOutputCacheStore`)
- Redis provider requires a connection string (`Cache:Redis:Configuration` or the scoped `OutputCache:Redis` / `FusionCacheInstances:{name}:Redis` override)
- Negative TTLs fail; `FusionCacheEagerRefreshRatio` must be in `[0, 1)`
- Allowlists have max lengths (headers, cookies, query, claims, Accept lists)
- `AuthBypassMode` must be a defined enum value  

## Runtime model

Resolved settings are **`DomainCacheOptions`** (immutable snapshot). JSON `*Seconds` integers become `TimeSpan` only for:

| JSON | Runtime |
|------|---------|
| `OutputCacheTtlSeconds` | `OutputTtl` |
| `FusionCacheSoftTtlSeconds` | `FusionCacheSoftTtl` |
| `FusionCacheHardTtlSeconds` | `FusionCacheHardTtl` |
| `FusionCacheFailSafeSeconds` | `FusionCacheFailSafe` |

Client TTL, Fusion jitter, and factory timeouts stay **`int` seconds** on the snapshot (`ClientTtlSeconds`, `FusionCacheJitterSeconds`, …). JSON `FusionCacheInstance` is `FusionCacheInstanceName` at runtime.

## Domain name normalization

`DomainName.Normalize` (public helper):

- lowercases  
- allows `a-z`, `0-9`, `-`, `:`, `_`, `@`  
- replaces other chars with `-`, collapses dashes  
- empty → `default`  

Resource ids: `DomainName.NormalizeResourceId` (same character rules; null/whitespace or values with no usable characters such as `!!!` → empty string, **not** `default`).

Entity kinds: `DomainName.NormalizeEntityKind` (same as resource ids). Unusable kinds do not share the domain name `default`.

## Example domains

See **[domain-profiles.md](domain-profiles.md)** for full **osm-tiles** (snapshot) and **product-detail** (CRUD) recipes.

## Related

- [Guide](guide/README.md)  
- [cache-keys.md](cache-keys.md) — Namespace and key composition  
- [architecture.md](architecture.md)  
- [domain-profiles.md](domain-profiles.md)  
- [invalidation.md](invalidation.md)  

