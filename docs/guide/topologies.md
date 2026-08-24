# Topologies

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

Which stores and coordination to use when you leave the single-process InMemory happy path.

- NuGet choices: [packages](packages.md)
- Copy-paste wiring: [composition](../how-to/composition.md)
- Production detail: [deployment](../reference/deployment.md)
- Try without writing Compose yourself: [playground labs](../../samples/CacheOrchestrator.Sample/labs/README.md)

---

## Layouts

| Layout | Output Cache | Data cache | Coordination | When it fits |
|--------|--------------|------------|--------------|--------------|
| **Single InMemory** | InMemory | L1 only (Fusion or Hybrid) | None | One process; cache dies with the process |
| **Redis data L2 (Fusion)** | InMemory | Redis L2 + backplane | Fusion L1 drop on peers via backplane | Expensive objects must survive recycle; HTTP cache can stay local |
| **Multi-node, OC local** | InMemory per process | Shared Redis L2 | Data coherent; **OC not** cleared on peers | Replicas with short or sticky HTTP cache |
| **Multi-node + HttpBus** | InMemory per process | Redis L2 (typical) | Bus carries invalidate / Version / TTL | Immediate OC purge and overlays on every node |
| **OC Redis + data Redis** | Redis store | Redis L2 + backplane | Shared payloads (+ optional bus for commands) | Several web nodes; shared HTTP **and** objects |
| **InMemory cluster, no Redis** | InMemory | L1 only | **HttpBus required** for immediate purge | No Redis; sticky sessions or TTL-only may be enough without the bus |

Output and data-cache providers can differ (for example OC InMemory, Fusion Redis). Named engines live under root **`DataCacheInstances`**.

HttpBus does **not** share cache values. Redis L2 + backplane and HttpBus together are safe but often redundant for Fusion tag purge; the bus still matters for InMemory Output Cache and runtime overlays.

FAQ: [Bus vs Redis backplane](faq.md#bus-vs-redis-backplane--which-do-i-need).

---

## Labs vs production

Playground **topology labs** climb the same ladder so you can see each layer. They are not a production edge: no load balancer, plain HTTP, single-container Redis.

| Lab | Compose | What it teaches |
|-----|---------|-----------------|
| 01 | `01-observability.yml` | InMemory + Prometheus + Admin Console |
| 02 | `02-redis.yml` | Fusion L2 on Redis; OC still InMemory |
| 03 | `03-multi.yml` | Two apps, shared L2; OC gap without bus |
| 04 | `04-bus.yml` | Commands on every node (HttpBus) |
| 05 | `05-dual-redis-bus.yml` | Shared OC store + Fusion Redis + HttpBus |

Details: [labs README](../../samples/CacheOrchestrator.Sample/labs/README.md).

---

## Invalidation across instances

`ICacheOrchestratorInvalidator` always applies **locally**. What peers see depends on topology:

| Approach | Immediate purge on all nodes? | Use when |
|----------|-------------------------------|----------|
| Bump `Version` in shared config | No — new key space; old entries expire by TTL | Snapshot / catalog cutover |
| Redis Fusion L2 + backplane | Yes for Fusion L1 + shared L2 | Typical multi-instance Fusion |
| `CacheOrchestrator.HttpBus` | Yes if every peer has receive endpoints | InMemory multi-node; Admin `distribute` |
| Neither | Calling process only | Single instance |

Details: [invalidation](../reference/invalidation.md#multi-instance-invalidation) · [cluster bus](../reference/cluster-bus.md).

---

## Named data-cache instances

Map domains to named `DataCacheInstances` (catalog vs PII) with their own Redis connections. Each Fusion instance gets a **keyed** `IDistributedCache` — do not share one global L2. Domains select an instance via `DataCache.Instance`.

Requires `CacheOrchestrator.Redis` + `AddRedisBackend()`.

Details: [deployment](../reference/deployment.md#using-multiple-datacache-instances) · [FAQ](faq.md#multiple-redis-clusters--named-data-cache-instances).
