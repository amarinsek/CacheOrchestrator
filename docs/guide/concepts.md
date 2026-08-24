# Concepts

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

How the pieces fit. Schema and APIs live on the reference pages linked at the end of each section.

## A domain is a policy group

A **domain** is a name in configuration (`catalog`, `osm-tiles`, `product-detail`). It holds TTLs, Version, client headers, vary rules, and which **data-cache instance** to use. You attach it to HTTP with `.CacheOutputWithDomain` or `[CacheDomain]`. `IDomainDataCache.GetOrSetAsync` (and Core’s `ICacheOrchestrator`) use the same options.

Different data wants different rules. The handler stays the same shape; the domain is what differs.

CacheOrchestrator stays **domain-based**: domains are the unit of configuration. **Entity identity** (`entityKind` + resource id, plus optional members / dependsOn / aliases) is optional and lives **inside** a domain. It shapes per-row data-cache keys and `entity:` / `entitykind:` tags so CRUD invalidation can target one row (or a related set) without bumping the whole domain `Version`. It is not a second settings root next to `Cache:Domains`.

EF Core invalidation (and any future ORM hook) only maps successful writes onto those same tags. The read-side entity APIs stay in the core library because they are generic, not ORM-specific.

Details: [domain-profiles.md](../domain-profiles.md), [configuration.md](../configuration.md), [fusion-cache.md](../fusion-cache.md#entity-identity), [entity-footprint.md](../entity-footprint.md).

## Three layers, one snapshot

| Layer | Stores | You enable it by |
|-------|--------|------------------|
| **Client Cache-Control** | Browser / CDN | Nested `ClientCache` (cacheability, TTLs, optional schedule). You do not set those headers by hand. |
| **Output Cache** | Full HTTP GET/HEAD response | Domain on the endpoint + nested `OutputCache` |
| **Data cache** | The object your factory produced (L1 memory, optional L2 via Fusion or Hybrid) | Calling `IDomainDataCache` / `ICacheOrchestrator`; nested `DataCache` (+ optional `FusionCache` engine knobs) |

All three resolve the same `DomainCacheOptions`. If lifetimes and invalidation disagree, one layer undoes the other.

Libraries take **`ICacheOrchestrator`** from Core and pass the domain (and entity identity) in the call. Web endpoints usually use AspNetCore’s **`IDomainDataCache`** + `.CacheOutputWithDomain` / `[CacheDomain]` — the HTTP projection over the same orchestrator. Which engine backs the data layer is an `IDataCacheProvider` (Fusion by default; Hybrid optional).

The same domain name in configuration can define DataCache, OutputCache, and ClientCache together. Http-free libraries take a host-supplied **`CacheDomainContext`** (domain, optional entity kind) on each call; the host applies Output Cache / client headers with the same domain string. Endpoint and library examples: [packages.md](../packages.md).

The core package is Http-free policy and contracts. ASP.NET Output Cache / client headers, Redis, the cluster HttpBus, and EF hooks are separate packages — [packages.md](../packages.md), [topologies](topologies.md).

Details: [output-cache.md](../output-cache.md), [fusion-cache.md](../fusion-cache.md), [architecture.md](../architecture.md).

## Version, TTL, and tags

Three ways a request becomes a miss (fresh data from the factory):

| Mechanism | What it does | Typical use |
|-----------|----------------|-------------|
| **TTL** | Entry expires; factory runs again | Short-lived or slowly changing rows |
| **Tag purge** | Explicit delete of a domain, a kind, or one id | CRUD: `InvalidateEntityAsync` |
| **Version** | Generation stamp in keys; old entries never match | Snapshot cutover (map tiles, monthly extract) |

`Version` is a stamp for the **whole domain**, not the version of one product. If a row changes under the same Version and you neither wait for TTL nor invalidate, caches keep serving the old body. That is caching working as designed.

Details: [invalidation.md](../invalidation.md), [cache-keys.md](../cache-keys.md).

## How the data cache finds the domain

`IDomainDataCache.GetOrSetAsync` looks for options in this order:

1. Explicit overload `GetOrSetAsync(http, domain, factory)` — same name reuses the request snapshot; a different name **replaces** it.
2. Options already on the request (Output Cache policy usually set them).
3. Endpoint metadata (`.CacheOutputWithDomain` / `[CacheDomain]`), then options are loaded.
4. Else the factory runs **uncached** (Warning log, metric `result=unresolved`).

Happy path: domain on the endpoint; no manual `EnsureDomainOptions`. Data-cache-only endpoints: pass the domain argument or call `EnsureDomainOptions`.

Details: [fusion-cache.md](../fusion-cache.md), [FAQ](../faq.md#fusion-runs-uncached--why).

## Client Cache Schedule

For datasets that update on a known schedule (like monthly map exports), `ScheduledUpdateUtc` automatically ramps down the **browser/CDN** `Cache-Control` `max-age` as the cutover approaches (Calm → Approaching → Hold). This guarantees timely client refreshes without sacrificing months of cache hits. It does **not** change Output Cache or data-cache TTLs.

Phase is on `X-Cache` as `phase=calm|approaching|hold|n/a`.

Details: [client-cache-schedule.md](../client-cache-schedule.md).

## Auth and vary (defaults)

Default: authenticated users **or** an `Authorization` header → Output Cache **bypassed**, client cache **blocked** (`AuthBypassMode: AuthenticatedOrAuthorization`). Prefer `AuthBypassMode` over the obsolete `BypassWhenAuthenticated` bool.

Domain vary (Accept, language, headers, cookies, query allowlists) is shared between Output Cache and the data cache where it makes sense.

**Domain Output Cache is opt-in.** ASP.NET’s **base** Output Cache policy still applies to other GET routes unless you set `NoStore`.

Details: [vary.md](../vary.md), [output-cache.md](../output-cache.md), [FAQ](../faq.md#why-is-a-route-cached-when-i-never-set-a-domain).

## Packages

| Package / app | When you need it |
|---------------|------------------|
| `CacheOrchestrator` (meta) | Typical web app: AspNetCore + Fusion data provider |
| `CacheOrchestrator.Core` | Policy, `ICacheOrchestrator`, invalidation / cluster **contracts** |
| `CacheOrchestrator.AspNetCore` | Output Cache, Client Cache-Control, Admin API, `IDomainDataCache` |
| `CacheOrchestrator.FusionCache` | Fusion as `IDataCacheProvider` (default with meta / AspNetCore) |
| `CacheOrchestrator.HybridCache` | HybridCache as `IDataCacheProvider` (replace Fusion) |
| `CacheOrchestrator.Redis` | Shared OC store and/or Fusion L2 + backplane. `"Provider": "Redis"` fails validation without it. |
| `CacheOrchestrator.HttpBus` | Commands (invalidate, Version, TTL) to every instance. Does not share cache payloads. |
| `CacheOrchestrator.EFCore.Invalidation` | Purge after a successful `SaveChanges`. |
| Admin Console App | Operator UI across instances. Not a NuGet package. |

Named engines live under root **`DataCacheInstances`**. Details: [packages.md](../packages.md), [topologies](topologies.md), [FAQ — Redis package vs core](../faq.md#redis-package-vs-core).

## Namespace

Root `Namespace` (default `app-cache`) prefixes store keys so apps that share Redis do not collide. Output uses `{Namespace}-oc`; data-cache **`default`** uses `{Namespace}-fc` (historical `-fc` suffix; no `-default`). It is not a domain and not per-endpoint.

Details: [cache-keys.md](../cache-keys.md).
