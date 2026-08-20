# Topologies

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

Which stores and packages to use. This page is a decision table. Production wiring: [deployment.md](../deployment.md). Redis registrar: [backends.md](../backends.md). Commands across nodes: [cluster-bus.md](../cluster-bus.md). Try the layouts without writing Compose: [playground labs](../../samples/CacheOrchestrator.Sample/labs/README.md).

## Which package

| Goal | Prefer |
|------|--------|
| Single process, in-memory only | Core package |
| Shared Fusion objects / L1 drop on other nodes | **Redis** (`AddRedisBackend`) |
| Shared full HTTP responses | Redis as Output Cache provider |
| Multi-instance **InMemory** purge / Version / TTL on all nodes | **Bus** (`AddHttpClusterBus` + `MapCacheOrchestratorHttpBus`) |
| Cache follows EF `SaveChanges` | **EF Core Invalidation** |
| Dashboard across instances | Admin API on each app + **Admin Console App** |

Bus does **not** share cache values. Redis L2 + backplane and Bus together are safe but often redundant for Fusion tag purge; Bus still matters for InMemory Output Cache and runtime overlays.

Details: [FAQ — Bus vs Redis backplane](../faq.md#bus-vs-redis-backplane--which-do-i-need).

## Layouts

| Layout | Output Cache | Fusion | Coordination | When it fits |
|--------|--------------|--------|----------------|--------------|
| **Single InMemory** | InMemory | L1 only | None | One process; cache dies with the process |
| **Redis Fusion L2** | InMemory | Redis L2 + backplane | Fusion L1 on other nodes via backplane | You can lose HTTP cache on recycle; expensive objects must survive |
| **Multi-node, OC local** | InMemory per process | Shared Redis L2 | Fusion coherent; **OC not** cleared on peers | Replicas; HTTP cache can stay node-local or short TTL |
| **Multi-node + Bus** | InMemory per process | Redis L2 (typical) | Bus carries invalidate / Version / TTL | Immediate OC purge and overlays on every node |
| **OC Redis + FC Redis** | Redis store | Redis L2 + backplane | Shared payloads + optional Bus for commands | Several web nodes; shared HTTP **and** objects |
| **InMemory cluster, no Redis** | InMemory | L1 only | **Bus required** for immediate purge | Redis not available; sticky sessions or TTL-only may be enough without Bus |

Output and Fusion providers can differ (for example OC InMemory, Fusion Redis).

Details: [deployment.md](../deployment.md).

## Labs vs production

Playground **topology labs** climb the same ladder (01 observability → 05 dual Redis + bus) so you can see each layer. They are not a production edge: no load balancer, plain HTTP, single-container Redis.

| Lab | Compose | What it teaches |
|-----|---------|-----------------|
| 01 | `01-observability.yml` | InMemory + Prometheus + Admin Console |
| 02 | `02-redis.yml` | Fusion L2 on Redis; OC still InMemory |
| 03 | `03-multi.yml` | Two apps, shared L2; OC gap without bus |
| 04 | `04-bus.yml` | Commands on every node |
| 05 | `05-dual-redis-bus.yml` | Shared OC store + Fusion Redis + bus |

Details: [labs README](../../samples/CacheOrchestrator.Sample/labs/README.md).

## Invalidation across instances

`ICacheOrchestratorInvalidator` always applies **locally**. What peers see depends on topology:

| Approach | Immediate purge on all nodes? | Use when |
|----------|-------------------------------|----------|
| Bump `Version` in shared config | No — new key space; old entries expire by TTL | Snapshot / catalog cutover |
| Redis Fusion L2 + backplane | Yes for Fusion L1 + shared L2 | Typical production multi-instance |
| `CacheOrchestrator.Bus` | Yes if every peer has receive endpoints | InMemory multi-node; Admin `distribute` |
| Neither | Calling process only | Single instance |

Details: [invalidation.md](../invalidation.md#multi-instance-invalidation), [cluster-bus.md](../cluster-bus.md).

## Named Fusion instances

Map domains to named `FusionCacheInstances` (for example catalog vs PII) with their own Redis connections. Each instance gets a **keyed** `IDistributedCache` — do not share one global L2.

Requires `CacheOrchestrator.Redis` + `AddRedisBackend()`.

Details: [deployment.md](../deployment.md#using-multiple-fusioncache-instances), [FAQ](../faq.md#multiple-redis-clusters--named-fusion-instances).
