# Topologies

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

Which stores and packages to use. This page is a decision table. Package composition: [packages.md](../packages.md). Production wiring: [deployment.md](../deployment.md). Redis registrar: [backends.md](../backends.md). Commands across nodes: [cluster-bus.md](../cluster-bus.md). Try the layouts without writing Compose: [playground labs](../../samples/CacheOrchestrator.Sample/labs/README.md).

## Which package

| Goal | Prefer |
|------|--------|
| Domains, Version, portable `DataCache`, invalidation contracts | **Core** |
| Fusion as data-cache engine | **FusionCache** (`IDataCacheProvider`) |
| HybridCache as data-cache engine | **HybridCache** (replaces Fusion provider) |
| Output Cache + Client Cache-Control + HTTP helpers / Admin API | **AspNetCore** |
| Typical web app (AspNet + Fusion) | **Meta** `CacheOrchestrator` |
| Shared data-cache objects / L1 drop on other nodes (Fusion) | **Redis** (`AddRedisBackend`) |
| Shared full HTTP responses | Redis as Output Cache provider |
| Multi-instance **InMemory** purge / Version / TTL on all nodes | **HttpBus** (`AddHttpClusterBus` + `MapCacheOrchestratorHttpBus`) |
| Cache follows EF `SaveChanges` | **EF Core Invalidation** |
| Dashboard across instances | Admin API on each app + **Admin Console App** |

HttpBus does **not** share cache values. Redis L2 + backplane and HttpBus together are safe but often redundant for Fusion tag purge; HttpBus still matters for InMemory Output Cache and runtime overlays.

Details: [FAQ — Bus vs Redis backplane](../faq.md#bus-vs-redis-backplane--which-do-i-need).

## Layouts

| Layout | Output Cache | Data cache | Coordination | When it fits |
|--------|--------------|------------|--------------|--------------|
| **Single InMemory** | InMemory | L1 only (Fusion or Hybrid) | None | One process; cache dies with the process |
| **Redis data L2 (Fusion)** | InMemory | Redis L2 + backplane | Fusion L1 on other nodes via backplane | You can lose HTTP cache on recycle; expensive objects must survive |
| **Multi-node, OC local** | InMemory per process | Shared Redis L2 | Data cache coherent; **OC not** cleared on peers | Replicas; HTTP cache can stay node-local or short TTL |
| **Multi-node + HttpBus** | InMemory per process | Redis L2 (typical) | HttpBus carries invalidate / Version / TTL | Immediate OC purge and overlays on every node |
| **OC Redis + data Redis** | Redis store | Redis L2 + backplane | Shared payloads + optional HttpBus for commands | Several web nodes; shared HTTP **and** objects |
| **InMemory cluster, no Redis** | InMemory | L1 only | **HttpBus required** for immediate purge | Redis not available; sticky sessions or TTL-only may be enough without HttpBus |

Output and data-cache providers can differ (for example OC InMemory, Fusion Redis). Named engines: root **`DataCacheInstances`**.

Details: [deployment.md](../deployment.md).

## Labs vs production

Playground **topology labs** climb the same ladder (01 observability → 05 dual Redis + bus) so you can see each layer. They are not a production edge: no load balancer, plain HTTP, single-container Redis.

| Lab | Compose | What it teaches |
|-----|---------|-----------------|
| 01 | `01-observability.yml` | InMemory + Prometheus + Admin Console |
| 02 | `02-redis.yml` | Fusion L2 on Redis; OC still InMemory |
| 03 | `03-multi.yml` | Two apps, shared L2; OC gap without bus |
| 04 | `04-bus.yml` | Commands on every node (HttpBus) |
| 05 | `05-dual-redis-bus.yml` | Shared OC store + Fusion Redis + HttpBus |

Details: [labs README](../../samples/CacheOrchestrator.Sample/labs/README.md).

## Invalidation across instances

`ICacheOrchestratorInvalidator` always applies **locally**. What peers see depends on topology:

| Approach | Immediate purge on all nodes? | Use when |
|----------|-------------------------------|----------|
| Bump `Version` in shared config | No — new key space; old entries expire by TTL | Snapshot / catalog cutover |
| Redis Fusion L2 + backplane | Yes for Fusion L1 + shared L2 | Typical production multi-instance (Fusion provider) |
| `CacheOrchestrator.HttpBus` | Yes if every peer has receive endpoints | InMemory multi-node; Admin `distribute` |
| Neither | Calling process only | Single instance |

Details: [invalidation.md](../invalidation.md#multi-instance-invalidation), [cluster-bus.md](../cluster-bus.md).

## Named data-cache instances

Map domains to named `DataCacheInstances` (for example catalog vs PII) with their own Redis connections. Each Fusion instance gets a **keyed** `IDistributedCache` — do not share one global L2. Domains select an instance via `DataCache.Instance`.

Requires `CacheOrchestrator.Redis` + `AddRedisBackend()` for Redis providers.

Details: [deployment.md](../deployment.md#using-multiple-datacache-instances), [FAQ](../faq.md#multiple-redis-clusters--named-data-cache-instances).
