# Cache Backends

**CacheOrchestrator** separates *caching policy* (domains, TTLs, invalidation, client headers) from *storage*.  
Providers are pluggable via `ICacheBackendRegistrar`.

> **Not a drop-in SQL/Memcached switch:** only **InMemory** (core) and **Redis** (`CacheOrchestrator.Redis`)
> ship first-party. Any other `"Provider": "…"` name requires you to implement and register a backend.
> See [comparison.md](comparison.md) and [faq.md](faq.md).

**Reference test:** `tests/CacheOrchestrator.UnitTests/Backends/CustomBackendEndToEndTests.cs` — a Fusion-only
`FakeDb` registrar with keyed in-process L2, proving `AddBackend` + `GetOrSetAsync` across two hosts (empty L1, shared L2).

## First-party backends

| Provider | Package | Registration | Output Cache | FusionCache L2 |
|----------|---------|--------------|--------------|----------------|
| **InMemory** | `CacheOrchestrator` | Automatic | ASP.NET in-process store (+ size limits) | None (L1 only) |
| **Redis** | `CacheOrchestrator.Redis` | `o.AddRedisBackend()` | Redis Output Cache store | Keyed Redis L2 + backplane |

### Redis package

```bash
dotnet add package CacheOrchestrator.Redis
```

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;

builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddRedisBackend();
    // Optional: further OutputCacheOptions after backend defaults
    o.ConfigureOutputCache(oc => oc.DefaultExpirationTimeSpan = TimeSpan.FromMinutes(5));
});
```

```json
"Cache": {
  "OutputCache": { "Provider": "InMemory" },
  "FusionCacheInstances": {
    "default": { "Provider": "Redis" }
  },
  "Redis": { "Configuration": "localhost:6379" },
  "Distributed": {
    "SoftTimeoutSeconds": 1,
    "HardTimeoutSeconds": 2,
    "CircuitBreakerSeconds": 5
  }
}
```

`Cache:Redis` (and optional `…:OutputCache:Redis` / `…:FusionCacheInstances:{name}:Redis`) is read by the **Redis package**, not by core `CacheOrchestratorOptions`.  
Without `AddRedisBackend()`, `"Provider": "Redis"` fails validation.

## What a backend must implement

| Responsibility | Required? | How |
|----------------|-----------|-----|
| **Output Cache store** | Only if used as `OutputCache.Provider` | `SupportsOutputCacheStore = true`, implement `RegisterOutputCache` |
| **FusionCache L2** | Only if used under `FusionCacheInstances` | `RegisterFusionCache` — **keyed** `IDistributedCache` per instance name |
| **Health probes** | Optional | `RegisterHealthProbes` → `ICacheOrchestratorHealthProbe` |

### Output Cache registration rules

1. Prefer `context.Configure(o => …)` for `OutputCacheOptions` (size limits, policies).  
   Do **not** call `services.AddOutputCache` yourself — the host does that once.  
2. Prefer `context.RegisterStore(() => …)` for store packages that must run **after** `AddOutputCache`  
   (e.g. `AddStackExchangeRedisOutputCache`).  
3. If the technology has **no** Output Cache store (common for SQL Server), set  
   `SupportsOutputCacheStore => false` and use that provider **only** for FusionCache.  
   Keep Output Cache on `InMemory` or `Redis`.

### FusionCache L2 rules (multi-instance safe)

1. Register a **keyed** `IDistributedCache` with key = `context.InstanceName`.  
2. Call `context.FusionBuilder.WithRegisteredKeyedDistributedCache(context.InstanceName)`.  
3. **Never** use a single global `AddDistributedSqlServerCache` / `AddStackExchangeRedisCache`  
   for multiple named Fusion instances — the last registration would win.  
4. Optional backplane: attach on `context.FusionBuilder` (Redis package does this).  
5. Bind settings from `context.BackendSection`  
   (`Cache:FusionCacheInstances:{instance}:{Provider}`).

### Config path convention (`BackendConfiguration`)

| Surface | Path |
|---------|------|
| Output backend | `{section}:OutputCache:{Provider}` e.g. `Cache:OutputCache:SqlServer` |
| Fusion backend | `{section}:FusionCacheInstances:{instance}:{Provider}` e.g. `Cache:FusionCacheInstances:default:SqlServer` |

Helpers: `BackendConfiguration.GetOutputBackendSection` / `GetFusionBackendSection`,  
or `context.BackendSection` on registration contexts.

### Distributed resilience (L2)

| Setting | Config | Applied when |
|---------|--------|----------------|
| Soft / hard timeout, circuit breaker | `Cache:Distributed:*` (core) | Fusion provider ≠ `InMemory` |

---

## Recommended custom pattern: Fusion L2 only (SQL Server)

ASP.NET Core has **no** first-party SQL Output Cache store. The blessed approach:

- **Output Cache:** `InMemory` (or Redis)  
- **FusionCache L2:** your SQL / Memcached / Cosmos registrar  

```csharp
using CacheOrchestrator.Backends;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ZiggyCreatures.Caching.Fusion;

public sealed class SqlServerFusionBackendRegistrar : ICacheBackendRegistrar
{
    public string Name => "SqlServer";

    // Fusion-only: do not select as OutputCache.Provider
    public bool SupportsOutputCacheStore => false;

    public void RegisterOutputCache(OutputCacheRegistrationContext context)
    {
        // Never called if SupportsOutputCacheStore is false and validation is correct.
        throw new NotSupportedException("SqlServer backend does not provide an Output Cache store.");
    }

    public void RegisterFusionCache(FusionCacheRegistrationContext context)
    {
        string? connectionString = context.BackendSection["ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"FusionCacheInstances['{context.InstanceName}']: SqlServer:ConnectionString is required.");

        string table = context.BackendSection["TableName"] ?? $"FusionCache_{context.InstanceName}";

        // One SqlServer cache instance per FusionCache instance name (keyed).
        context.Services.TryAddKeyedSingleton<IDistributedCache>(context.InstanceName, (_, _) =>
        {
            var options = Options.Create(new SqlServerCacheOptions
            {
                ConnectionString = connectionString,
                SchemaName = context.BackendSection["SchemaName"] ?? "dbo",
                TableName = table
            });
            return new SqlServerCache(options);
        });

        context.FusionBuilder.WithRegisteredKeyedDistributedCache(context.InstanceName);
        // No Redis-style backplane unless you add one yourself.
    }

    public void RegisterHealthProbes(BackendHealthRegistrationContext context)
    {
        // Optional: register a SQL connectivity probe via ICacheOrchestratorHealthProbe
        // or Microsoft.Extensions.Diagnostics.HealthChecks.
    }
}
```

Startup:

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddBackend(new SqlServerFusionBackendRegistrar());
});
```

Config:

```json
"Cache": {
  "OutputCache": { "Provider": "InMemory" },
  "FusionCacheInstances": {
    "default": {
      "Provider": "SqlServer",
      "SqlServer": {
        "ConnectionString": "Server=.;Database=Cache;Trusted_Connection=True;",
        "SchemaName": "dbo",
        "TableName": "FusionCache"
      }
    }
  },
  "Distributed": {
    "SoftTimeoutSeconds": 2,
    "HardTimeoutSeconds": 5,
    "CircuitBreakerSeconds": 10
  }
}
```

---

## Builder API summary

| API | Purpose |
|-----|---------|
| `AddBackend(ICacheBackendRegistrar)` | Register / replace a provider by `Name` |
| `AddRedisBackend()` | From `CacheOrchestrator.Redis` |
| `ConfigureOutputCache(Action<OutputCacheOptions>)` | App-level Output Cache options (after backend defaults) |

## Related

- [configuration.md](configuration.md) — `Distributed`, Redis, providers  
- [deployment.md](deployment.md) — multi-instance Redis  
- [architecture.md](architecture.md) — public API surface  
