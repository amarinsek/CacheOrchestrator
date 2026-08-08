# Deployment scenarios

Multi-instance and distributed deployment guidance for CacheOrchestrator.

**Redis package:** any topology below that uses `"Provider": "Redis"` requires:

```bash
dotnet add package CacheOrchestrator.Redis
```

```csharp
services.AddCacheOrchestrator(configuration, o => o.AddRedisBackend());
```

---

## Single instance (in-memory only)

The simplest topology. One process, no Redis, no cross-process coordination.

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": { 
      "default": { "Provider": "InMemory" }
    }
  }
}
```

**Limitations:**
- Output Cache and FusionCache data are **not shared** between process restarts or replicas.
- No invalidation signal reaches other instances -- `InvalidateDomainAsync` only clears the calling process.

---

## Multiple instances with Redis

> **Custom backends:** Redis is the only first-party distributed backend (`CacheOrchestrator.Redis`).
> SQL Server, Memcached, Cosmos DB, and similar stores require a **custom** `ICacheBackendRegistrar`
> that you register with `AddBackend(...)`. Setting `"Provider": "SqlServer"` alone is **not** a drop-in;
> see [backends.md](backends.md) and [comparison.md](comparison.md).

Multiple replicas share both Output Cache data and FusionCache data through Redis.
FusionCache also receives **backplane** invalidation signals so L1 (in-memory) is cleared on all nodes
when any node invalidates a tag.

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { 
      "Provider": "Redis",
      "Redis": { "Configuration": "redis-primary:6379" }
    },
    "FusionCacheInstances": { 
      "default": { 
        "Provider": "Redis",
        "Redis": { "Configuration": "redis-primary:6379" }
      } 
    }
  }
}
```

```
Instance A (ASP.NET)          Instance B (ASP.NET)
  L1 (memory)                   L1 (memory)
     |                              |
     +--------- Redis L2 -----------+
     |                              |
     +------- Redis Backplane ------+
              (pub/sub channel)

Output Cache:
  Instance A writes -> Redis -> Instance B reads (shared store)

FusionCache invalidation:
  Instance A calls InvalidateDomainAsync
    -> removes tag from Redis L2
    -> publishes backplane message
    -> Instance B receives signal, clears L1 entries for that tag
```

**What the backplane does:** Without a backplane, `InvalidateDomainAsync` would clear Redis L2 but
Instance B's L1 memory would still hold stale data until its TTL expired. The Redis backplane
(pub/sub) delivers the invalidation signal so L1 is cleared immediately on all nodes.

**Backplane channel:** `{FusionNamespace}:backplane` (e.g. `my-app-fc:backplane`).
Each `Namespace` / `FusionCache.Namespace` gets its own channel, so multiple apps on the same
Redis cluster do not interfere.

---

## Multiple instances without Redis (InMemory FC, no backplane)

Possible when Redis is not available, at the cost of stale L1 data across instances.

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": { 
      "default": { "Provider": "InMemory" }
    }
  }
}
```

| Scenario | Behaviour |
|----------|-----------|
| Request hits Instance A | A serves from its own L1 |
| Request hits Instance B | B serves from its own L1 (may be stale) |
| `InvalidateDomainAsync` on A | Only A's cache cleared; B unaffected |
| Output Cache | Each instance stores its own copy; no cross-sharing |

**Acceptable when:** Load balancer uses sticky sessions, or domains are truly static
(TTL-based expiry is the only invalidation strategy).

---

## Mixed backends (Output Cache InMemory + FusionCache Redis)

A common hybrid: Output Cache stays in-process for maximum response speed,
while FusionCache uses Redis so object data is shared and invalidation propagates.

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": { 
      "default": { 
        "Provider": "Redis",
        "Redis": { "Configuration": "redis-primary:6379" }
      } 
    }
  }
}
```

| Layer | Storage | Cross-instance? |
|-------|---------|-----------------|
| Output Cache (HTTP responses) | Per-process memory | No |
| FusionCache L1 | Per-process memory | No (but invalidated via backplane) |
| FusionCache L2 | Redis | Yes |

Output Cache will eventually diverge between instances (until TTL expires or endpoint is not hit on that instance yet).
FusionCache object data stays consistent because Redis is the shared source of truth and the backplane keeps L1 in sync.

---

## Using multiple FusionCache instances

By default, CacheOrchestrator uses a single `default` FusionCache instance for all domains. This provides isolation via keys and tags, which is sufficient for most applications.

However, you might need separate FusionCache instances for:
- Regulatory isolation (GDPR: user PII must not touch product cache Redis)
- Scale isolation (high-write domain should not evict low-write domains)
- Geographic isolation (domain A served from EU Redis, domain B from US Redis)

You can configure multiple named instances in `FusionCacheInstances` and map them to specific domains:

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "FusionCacheInstances": {
      "default": {
        "Provider": "Redis",
        "Redis": { "Configuration": "global-redis:6379" }
      },
      "pii": {
        "Provider": "Redis",
        "Redis": { "Configuration": "secure-redis:6379" }
      }
    },
    "Domains": {
      "Products": { "FusionCacheInstance": "default" },
      "UserProfiles": { "FusionCacheInstance": "pii" }
    }
  }
}
```

Each named instance maintains:

| Component | Isolation |
|-----------|-----------|
| `IConnectionMultiplexer` | Keyed singleton per instance name |
| `IDistributedCache` (L2) | **Keyed** singleton per instance name (not a single global registration) |
| Redis backplane | Same multiplexer + channel prefix `{FusionNamespace}:backplane` |

This means two domains can safely map to **different Redis clusters** (e.g. GDPR isolation) without the last-registered L2 overwriting the first.

## Related

- [configuration.md](configuration.md) — namespaces, providers  
- [backends.md](backends.md) — Redis package and custom registrars  
- [fusion-cache.md](fusion-cache.md)  
- [invalidation.md](invalidation.md)  
- [observability.md](observability.md)  
- [faq.md](faq.md) — multi-instance and InMemory limitations  
- [comparison.md](comparison.md) — when Redis OC alone is enough
