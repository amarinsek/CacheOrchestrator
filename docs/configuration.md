# Configuration reference

Root section name defaults to **`Cache`** (override via `AddCacheOrchestrator(config, "MySection")`).

## Root shape

```json
{
  "Cache": {
    "Namespace": "app-cache",
    "Distributed": { },
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": {
      "default": { "Provider": "InMemory" }
    },
    "DomainDefaults": { },
    "Domains": {
      "products": { }
    }
  }
}
```

## Root properties (core package)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Namespace` | string | `app-cache` | Global key prefix; isolates multi-app shared stores |
| `EmitDiagnosticsHeaders` | bool | `true` | When `true`, emit client-visible diagnostic headers (currently `X-Cache`). Set `false` in production if you do not want hit/miss/domain details exposed to clients. Does **not** affect metrics, tracing, or logs. |
| `Distributed` | object | soft 1s / hard 2s / circuit 5s | L2 resilience for **non-InMemory** Fusion providers |
| `OutputCache` | object | Provider `InMemory` | Output Cache provider + optional namespace |
| `FusionCacheInstances` | map | `default` instance `InMemory` | Named FusionCache instances |
| `DomainDefaults` | object | — | Fallbacks for every domain |
| `Domains` | map | — | Per-domain overrides (keys are domain names) |

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
| `BypassWhenAuthenticated` | true | Skip Output Cache for signed-in users / `Authorization` header |
| `VaryOutputCacheByUser` | true | When auth is not bypassed, vary OC by user / API-key hash |

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
| `FusionCacheEagerRefreshRatio` | 0.9 | Eager refresh threshold (0–1 exclusive) |
| `FusionCacheJitterSeconds` | 60 | Max jitter on duration |
| `FusionCacheFactorySoftTimeoutSeconds` | 1 | Factory soft timeout |
| `FusionCacheFactoryHardTimeoutSeconds` | 5 | Factory hard timeout |
| `FusionCacheMaxItemBytes` | 0 | Memory size limit; 0 = unlimited |
| `FusionCacheRespectNoStore` | true | Skip FC when request has `Cache-Control: no-store` |
| `FusionCacheAllowBackgroundDistributed` | true | |
| `FusionCacheAllowBackgroundBackplane` | true | |
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

## Validation

`CacheOrchestratorOptionsValidator` runs on start (`ValidateOnStart`):

- Provider must be one of the dynamically registered backends (e.g., `InMemory`, `Redis`, `SqlServer`)
- Redis provider requires a connection string  
- Negative TTLs fail  

## Runtime model

Resolved settings are **`DomainCacheOptions`** (immutable snapshot): TimeSpan fields named without a misleading `Seconds` suffix (`OutputTtl`, `FusionCacheSoftTtl`, …).  
Bound JSON still uses `*Seconds` integers.

## Domain name normalization

`DomainName.Normalize` (public helper):

- lowercases  
- allows `a-z`, `0-9`, `-`, `:`, `_`, `@`  
- replaces other chars with `-`, collapses dashes  
- empty → `default`  

Resource ids: `DomainName.NormalizeResourceId` (same rules; empty input → empty string).

## Example domains

See **[domain-profiles.md](domain-profiles.md)** for full **osm-tiles** (snapshot) and **product-detail** (CRUD) recipes.

## Related

- [cache-keys.md](cache-keys.md) — Namespace and key composition  
- [architecture.md](architecture.md)  
- [domain-profiles.md](domain-profiles.md)  
- [invalidation.md](invalidation.md)  

