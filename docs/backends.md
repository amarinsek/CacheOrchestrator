# Cache backends

Policy (domains, TTLs, invalidation, client headers) is separate from **storage**. InMemory ships in the core package. Redis is `CacheOrchestrator.Redis`. Any other provider name is a registrar you implement and pass to `AddBackend`. See [comparison.md](comparison.md) and [faq.md](faq.md).

## First-party backends

| Provider | Package | Registration | Output Cache | FusionCache L2 |
|----------|---------|--------------|--------------|----------------|
| **InMemory** | `CacheOrchestrator` | Automatic | ASP.NET in-process store (+ size limits) | None (L1 only) |
| **Redis** | `CacheOrchestrator.Redis` | `o.AddRedisBackend()` | Redis Output Cache store | Keyed Redis L2 + backplane |

## Install

```bash
dotnet add package CacheOrchestrator.Redis
```

## Register

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Redis;

builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddRedisBackend();
    o.ConfigureOutputCache(oc => oc.DefaultExpirationTimeSpan = TimeSpan.FromMinutes(5));
});
```

## Configure

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

## What a backend implements

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
3. If the store has no ASP.NET Output Cache adapter, set `SupportsOutputCacheStore` to `false` and use that provider only for FusionCache. Keep Output Cache on `InMemory` or `Redis`. The SQL Server example below does this.

### FusionCache L2 rules (multi-instance safe)

1. Register a **keyed** `IDistributedCache` with key = `context.InstanceName`.  
2. Call `context.FusionBuilder.WithRegisteredKeyedDistributedCache(context.InstanceName)`.  
3. Register a **keyed** `IDistributedCache` per Fusion instance name. A single global `AddDistributedSqlServerCache` or `AddStackExchangeRedisCache` would let the last instance overwrite the others.
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

## Example: Fusion L2 on SQL Server

ASP.NET Core does not ship an Output Cache store for SQL Server. This example therefore keeps Output Cache on `InMemory` (or Redis) and uses SQL Server only as FusionCache L2. The same shape works for Memcached, Cosmos, or another `IDistributedCache`.

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

    public bool SupportsOutputCacheStore => false;

    public void RegisterOutputCache(OutputCacheRegistrationContext context)
    {
        throw new NotSupportedException("This registrar is Fusion L2 only.");
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
