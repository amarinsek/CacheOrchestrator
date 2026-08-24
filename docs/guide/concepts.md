# Concepts

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

How CacheOrchestrator thinks about caching. Schema and APIs live on the [reference](../README.md#reference) pages.

---

## A domain is a policy group

A **domain** is a name in configuration (`catalog`, `osm-tiles`, `product-detail`). It holds TTLs, Version, client headers, vary rules, and which **data-cache instance** to use.

You attach it to HTTP with `.CacheOutputWithDomain` or `[CacheDomain]`. Data-cache calls — `IDomainDataCache` on the web, or `ICacheOrchestrator` in libraries — resolve the **same** options.

Different data wants different rules. The handler stays the same shape; the domain is what differs. That is the point of the library.

**Entity identity** (`entityKind` + resource id, plus optional footprint members) is optional and lives *inside* a domain. It shapes per-row keys and `entity:` / `entitykind:` tags so CRUD can purge one row without bumping the whole domain `Version`. It is not a second settings root next to `Cache:Domains`.

When to use snapshot vs CRUD profiles: [domain profiles](domain-profiles.md). Entity cookbook: [entity footprint](../reference/entity-footprint.md).

---

## Three layers, one snapshot

| Layer | Stores | You enable it by |
|-------|--------|------------------|
| **Client Cache-Control** | Browser / CDN | Nested `ClientCache` (cacheability, TTLs, optional schedule) |
| **Output Cache** | Full HTTP GET/HEAD response | Domain on the endpoint + nested `OutputCache` |
| **Data cache** | The object your factory produced | `IDomainDataCache` / `ICacheOrchestrator` + nested `DataCache` |

All three resolve the same `DomainCacheOptions`. If lifetimes and invalidation disagree, one layer undoes the other — the problem this library exists to prevent.

Web apps usually call AspNetCore’s **`IDomainDataCache`**. Libraries and workers take Core’s **`ICacheOrchestrator`** and pass a `CacheDomainContext`. The data engine behind both is an **`IDataCacheProvider`** (Fusion by default with the meta package; Hybrid optional).

Which packages to install: [packages](packages.md). Copy-paste stacks: [composition](../how-to/composition.md).

---

## Version, TTL, and tags

Three ways a request becomes a miss (fresh data from the factory):

| Mechanism | What it does | Typical use |
|-----------|----------------|-------------|
| **TTL** | Entry expires; factory runs again | Short-lived or slowly changing rows |
| **Tag purge** | Explicit delete of a domain, a kind, or one id | CRUD: `InvalidateEntityAsync` |
| **Version** | Generation stamp in keys; old entries never match | Snapshot cutover (map tiles, monthly extract) |

`Version` is a stamp for the **whole domain**, not the version of one product. If a row changes under the same Version and you neither wait for TTL nor invalidate, caches keep serving the old body. That is caching working as designed.

Details: [invalidation](../reference/invalidation.md) · [cache keys](../reference/cache-keys.md) · [domain profiles](domain-profiles.md).

---

## How the data cache finds the domain

Happy path: put the domain on the endpoint; `GetOrSetAsync(http, factory)` reuses that snapshot.

Data-cache-only routes pass the domain name (or call `EnsureDomainOptions`). Without a domain the factory runs **uncached** (Warning log, `dc=unresolved`).

Full resolution order: [data cache](../reference/data-cache.md#how-the-data-cache-finds-the-domain). FAQ: [Fusion runs uncached](faq.md#fusion-runs-uncached--why).

---

## Client Cache Schedule

For datasets that update on a known date (monthly map exports, annual imagery), `ScheduledUpdateUtc` ramps down the **browser/CDN** `max-age` as cutover approaches (Calm → Approaching → Hold). Clients refresh on time without living on a tiny TTL all year.

It does **not** change Output Cache or data-cache TTLs. Phase appears on `X-Cache` as `phase=calm|approaching|hold|n/a`.

Guide: [Client Cache Schedule](client-cache-schedule.md).

---

## Auth and vary (defaults)

Default: authenticated users **or** an `Authorization` header → Output Cache **bypassed**, client cache **blocked** (`AuthBypassMode: AuthenticatedOrAuthorization`).

Domain Output Cache is **opt-in**. Routes without a domain use the base policy `NoCache`.

Full matrix: [vary](../reference/vary.md). FAQ: [authenticated traffic](faq.md#authenticated-requests-and-api-keys).

---

## Namespace

Root `Namespace` (default `app-cache`) prefixes store keys so apps that share Redis do not collide. Output uses `{Namespace}-oc`; the data-cache **`default`** instance uses `{Namespace}-fc` (historical suffix). It is not a domain and not per-endpoint.

Details: [cache keys](../reference/cache-keys.md).
