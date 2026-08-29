# Topologies

> **Guide path:** [Packages](packages.md) → **Topologies** → [Client Cache Schedule](client-cache-schedule.md) · [Guide index](README.md)

A domain says how data should be cached. A topology says where cached values live and how application instances learn about changes.

Start with one process and in-memory stores. Add Redis or the HTTP cluster bus only when a deployment requirement calls for it.

## Table of Contents

- [Separate storage from coordination](#separate-storage-from-coordination)
- [Topology 1: one process, all in memory](#topology-1-one-process-all-in-memory)
- [Topology 2: several instances, both stores in Redis](#topology-2-several-instances-both-stores-in-redis)
- [Topology 3: local Output Cache, shared Data Cache](#topology-3-local-output-cache-shared-data-cache)
- [Topology 4: several in-memory instances with HttpBus](#topology-4-several-in-memory-instances-with-httpbus)
- [Choose Redis, HttpBus, or both](#choose-redis-httpbus-or-both)
- [Named Data Cache instances isolate workloads](#named-data-cache-instances-isolate-workloads)
- [Namespace every application](#namespace-every-application)
- [Learn the layouts in the topology labs](#learn-the-layouts-in-the-topology-labs)

## Separate storage from coordination

Multi-instance caching has two independent questions:

1. **Where is the value stored?** In each process, in Redis Output Cache, or in FusionCache Redis L2.
2. **How does a change reach peers?** Through a shared store, the FusionCache backplane, the HTTP cluster bus, or not at all until TTL expiry.

These components have distinct jobs:

| Component | Shares values | Sends invalidation | Sends runtime Version/TTL/settings changes |
|-----------|---------------|--------------------|-------------------------------------------|
| In-memory store | No | No | No |
| Redis Output Cache store | Yes, HTTP responses | Eviction operates on the shared store | No |
| FusionCache Redis L2 | Yes, data objects | Shared L2 is purged | No |
| FusionCache Redis backplane | No | Clears Fusion L1 entries on peers | No |
| CacheOrchestrator HttpBus | No | Tells peers to run the same local purge | Yes, for distributed Admin operations |

The bus never handles normal cache reads and never transports cached payloads.

## Topology 1: one process, all in memory

```text
Client → ASP.NET app
             ├─ Output Cache: memory
             └─ Data Cache: Fusion L1 memory
```

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": {
      "default": { "Provider": "InMemory" }
    }
  }
}
```

This is the getting-started topology and the right default for one application process.

- It has no external infrastructure.
- All entries disappear on restart.
- Programmatic invalidation clears both server layers in that process.
- A second replica would have an independent cache and would not receive the invalidation.

Stay here until you need replicas, cache survival across process restarts, or shared capacity.

## Topology 2: several instances, both stores in Redis

```text
App A ─┬─ Redis Output Cache
       └─ Fusion L1 ─┬─ Redis L2
                     └─ Redis backplane

App B ─┬─ Redis Output Cache
       └─ Fusion L1 ─┴─ Redis L2 + backplane
```

```json
{
  "Cache": {
    "Namespace": "my-app",
    "OutputCache": {
      "Provider": "Redis",
      "Redis": { "Configuration": "redis-primary:6379" }
    },
    "DataCacheInstances": {
      "default": {
        "Provider": "Redis",
        "Redis": { "Configuration": "redis-primary:6379" }
      }
    }
  }
}
```

This is the most complete shared-store layout supplied by the project:

- every web instance reads and writes the same Output Cache store;
- FusionCache keeps a fast L1 in each process and a shared Redis L2;
- tag invalidation removes matching L2 entries;
- the Redis backplane tells other FusionCache instances to clear matching L1 entries.

Use `CacheOrchestrator.Redis` and register `AddRedisBackend()`. Redis availability, persistence, clustering, TLS, and failover remain deployment responsibilities outside CacheOrchestrator.

The HTTP bus is not required for ordinary Fusion tag invalidation in this topology. It may still be useful for distributing runtime Version, TTL, and settings overlays.

## Topology 3: local Output Cache, shared Data Cache

```text
App A ── Output Cache A (memory)
     └── Fusion L1 A ─┐
                      ├─ Redis L2 + backplane
App B ── Output Cache B (memory)
     └── Fusion L1 B ─┘
```

```json
{
  "Cache": {
    "OutputCache": { "Provider": "InMemory" },
    "DataCacheInstances": {
      "default": {
        "Provider": "Redis",
        "Redis": { "Configuration": "redis-primary:6379" }
      }
    }
  }
}
```

This layout keeps HTTP responses close to each process while sharing expensive data objects.

The FusionCache side is coordinated through Redis and its backplane. Output Cache is not: invalidating on App A clears App A's HTTP responses, but App B can retain its local response until TTL expiry unless the HTTP bus also sends the purge to App B.

Use this topology when local Output Cache speed matters and one of these is acceptable:

- Output Cache TTLs are short enough to bound divergence;
- traffic is sticky to an instance;
- the HTTP bus is enabled to purge local Output Cache on peers.

## Topology 4: several in-memory instances with HttpBus

```text
App A: local Output + local data ── invalidate command ──► App B: local purge
                                  ◄──────────────────────► App C: local purge
```

Use `CacheOrchestrator.HttpBus` when instances keep values in memory but still need immediate cross-instance commands.

Every instance must:

- register `AddHttpClusterBus`;
- use static or service-discovery membership;
- map `MapCacheOrchestratorHttpBus`;
- authenticate and reach the peer endpoints.

With the bus enabled, a programmatic `Invalidate*` call applies locally and publishes an invalidation command. Peers apply the same purge locally without republishing it.

Admin changes to runtime Version, TTL, or settings are distributed only when the operation requests `distribute: true`. The Admin Console chooses between bus distribution and direct fan-out.

This topology coordinates invalidation but does not share warm entries. Each process still fills and stores its own copy, and all entries disappear when that process restarts.

## Choose Redis, HttpBus, or both

| Requirement | Prefer |
|-------------|--------|
| One process | InMemory only |
| Share HTTP response payloads | Redis Output Cache |
| Share FusionCache data objects and clear peer L1 entries | Redis L2 + backplane |
| Purge in-memory Output Cache on every node | HttpBus |
| Coordinate nodes without Redis | HttpBus |
| Apply runtime Version/TTL/settings overlays on every node | HttpBus or Admin Console fan-out |
| Redis Fusion L2 plus local Output Cache | Redis + optionally HttpBus for the Output Cache gap |

Using the Redis backplane and HttpBus together for the same Fusion tag purge is safe because purges are idempotent, but it is often redundant. Add both only when the bus has another job, such as local Output Cache eviction or runtime overlays.

Detailed setup and failure behaviour: [Deployment](../reference/deployment.md) · [Cluster bus](../reference/cluster-bus.md) · [Invalidation](../reference/invalidation.md).

## Named Data Cache instances isolate workloads

Most applications use one `default` Data Cache instance. Add named instances when domains require different connections or operational boundaries:

```json
{
  "Cache": {
    "DataCacheInstances": {
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
      "products": {
        "DataCache": { "Instance": "default" }
      },
      "user-profiles": {
        "DataCache": { "Instance": "pii" }
      }
    }
  }
}
```

Good reasons include regulatory isolation, separate Redis capacity, different regions, or protection from a high-churn workload. Domains already isolate keys and tags, so do not create a physical instance for every domain by default.

Each FusionCache instance receives its own keyed distributed-cache registration and namespace. See [Multiple Data Cache instances](../reference/deployment.md#using-multiple-data-cache-instances).

## Namespace every application

Set a stable root `Cache:Namespace` when applications share infrastructure. CacheOrchestrator derives Output Cache and Data Cache namespaces from it unless a provider-specific namespace overrides them.

Two applications using the same Redis connection and the same namespace can collide even when their domain names look different. Treat the namespace as part of deployment identity.

## Learn the layouts in the topology labs

The playground includes five Docker Compose stages:

| Lab | Adds | Lesson |
|-----|------|--------|
| 01 | InMemory app, Prometheus, Admin Console | Observe one process |
| 02 | FusionCache Redis L2 | Share data objects while Output Cache stays local |
| 03 | A second app instance | Expose the local Output Cache gap |
| 04 | HttpBus | Send commands to every node |
| 05 | Redis Output Cache + Fusion Redis + HttpBus | Inspect the complete shared layout |

The labs are teaching environments, not production blueprints: they omit production load balancing, transport security, and Redis operations. Run them from the [Topology labs README](../../samples/CacheOrchestrator.Sample/labs/README.md), then use the [Deployment reference](../reference/deployment.md) for production details.

Next: learn how a snapshot domain prepares clients for a planned cutover with [Client Cache Schedule](client-cache-schedule.md).
