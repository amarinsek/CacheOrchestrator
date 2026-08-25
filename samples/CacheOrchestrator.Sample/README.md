# CacheOrchestrator Sample Playground

A playground for CacheOrchestrator after the [Minimal sample](../CacheOrchestrator.Minimal). You get a browser UI to change TTLs, watch Client Cache Schedule phases, try Redis and multi-instance labs, and experiment with entity invalidation — without writing a new project.

<img src="../../docs/assets/sample-playground.png" width="800" />
---

## Choose your path

| Path | Best for |
|------|----------|
| **A. Solo (host)** | Fastest loop; settings editor; single process. See below. |
| **B. Topology labs (Docker)** | Learn **cache layouts** + Admin + Redis with one command. See [labs/README.md](labs/README.md)|

---

## Solo (host)

```bash
dotnet run --project samples/CacheOrchestrator.Sample
```

Open the printed URL (http://localhost:5289 by default).

**Disable browser HTTP cache** (header, next to **appsettings.json**, **on by default**) sets Fetch **`cache: 'no-store'`** so the browser always calls the app and you see **server** OC/DC hits. It does **not** send HTTP `Cache-Control: no-store` and does **not** turn off Output/data cache on the server. Uncheck only to demo client `max-age` / BROWSER-CACHE.

This playground can write `appsettings.json` from the browser. That is for this sample only.

## What to try

- **Domain endpoints** panel: `Demo:Endpoints` from config (catalog, product, search, …). Add a line under `Demo:Endpoints` and restart (or save config) to expose another route.
- **Entity invalidation (CRUD)** panel: fixed `GET /api/crud/products/42` + **Update price (PUT)** — separate from the domain dropdown.
- **POST identity** panel: read-only search POST with a named contract vs create POST without identity (same domain). See [POST identity (playground)](#post-identity-playground).
- **appsettings.json** (top right) opens an editor. Change a Version or TTL, save, and the process reloads configuration so the **next request** uses the new settings. That does **not** purge cache — use **Invalidate domain** (or entity) when you want a separate invalidation.
- **Client Cache Schedule.** Set `ScheduledUpdateUtc` on a domain and watch the phase on each fetch:
  - **calm** — far from the cutover; client `max-age` is at its maximum
  - **approaching** — `max-age` falls toward the floor
  - **hold** — the scheduled time has passed; `max-age` stays at the floor
- **Disable browser HTTP cache** (header, default on) — bypass browser cache only; uncheck to demo BROWSER-CACHE.
- **Badges** on a response (from `X-Cache` `oc=` / `dc=` / `fa=`):
  - **BROWSER-CACHE** — client cache served the response (only when **Disable browser HTTP cache** is off)
  - **OC-HIT** — Output Cache served the HTTP response (`oc=hit`; `dc`/`fa` omitted)
  - **OC-MISS DC-HIT** — handler ran; data cache had the object (`dc=hit`, no `fa`)
  - **OC-MISS DC-STALE FACTORY** — fail-safe stale from data cache (`dc=stale; fa=run`)
  - **OC-MISS DC-MISS FACTORY** — both layers missed; factory ran (`dc=miss; fa=run`)
  - **OC-OFF** / **DC-OFF** — that layer is disabled for the domain. **FACTORY** still appears whenever `dc` is present and is not `hit` (`fa=run`)
- **Extra query params** (optional): e.g. `page=2` usually creates a **different** cache key. Tracking params such as `utm_source=demo` are omitted from keys (same entry as without them) — see [cache-keys.md](../../docs/reference/cache-keys.md).

## CRUD (entity invalidation)

In the playground, open the **Entity invalidation (CRUD)** panel (not the domain endpoint list).

- **Invalidate entity** — purge OC/DC for `products/42` only (in-memory price unchanged).
- **Update price (PUT)** — enter a price, write store + entity invalidate → next Fetch shows that price.

Suggested UI flow: Fetch → Fetch twice (OC-HIT) → Invalidate entity → Fetch (FACTORY, same price) → set Price → Update price → Fetch (FACTORY, new price).

```bash
curl -i http://localhost:5289/api/crud/products/42

curl -i -X PUT http://localhost:5289/api/crud/products/42 \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"Demo Widget\",\"price\":12.5}"

curl -i http://localhost:5289/api/crud/products/42
```

`GET /api/crud/products` (list) is an uncached store dump — curl only. Background: [domain-profiles.md](../../docs/guide/domain-profiles.md).

## POST identity (playground)

Open the **POST identity playground** tab. Ordinary GET catalogues stay on domain-only Url identity; this panel is for **read-only POST** Output Cache (and data-cache keys) via a named contract.

| Control | Role |
|---------|------|
| `q`, `sort`, `page` | Part of cache identity (normalized by the contract) |
| `uiHint` | Sent in the JSON body but **ignored** by identity — changing it alone should still HIT |
| **Search once / twice** | `POST /api/demo/search` + `GetOrSetAsync` |
| **Create (no identity)** | `POST /api/demo/products` — same domain `product-search`, **no** identity binding → not Output Cached |

Suggested UI flow: Search twice (OC-HIT) → change only **uiHint** → Search (still HIT) → change **q** or **page** → MISS → Create (never OC-HIT).

Identity for search is normalized `q` + `sort` + `page` (empty `q` skips caching). Reference: [endpoint cache identity](../../docs/reference/cache-identity.md).

```bash
curl -i -X POST http://localhost:5289/api/demo/search \
  -H "Content-Type: application/json" \
  -d "{\"q\":\"widgets\",\"sort\":\"relevance\",\"page\":1,\"uiHint\":\"a\"}"

# Same identity (uiHint ignored) → expect oc=hit on the second call
curl -i -X POST http://localhost:5289/api/demo/search \
  -H "Content-Type: application/json" \
  -d "{\"q\":\"widgets\",\"sort\":\"relevance\",\"page\":1,\"uiHint\":\"b\"}"

curl -i -X POST http://localhost:5289/api/demo/products \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"New item\"}"
```

Minimal sample has a simpler content-hash demo: `POST /echo` in [CacheOrchestrator.Minimal](../CacheOrchestrator.Minimal).

## Next

- [labs/README.md](labs/README.md) — topology labs 01–05 (main learning path for multi-instance cache)
- [Guide](../../docs/guide/README.md) — concepts, topologies, operations
- [Getting started](../../docs/guide/getting-started.md)
- [Endpoint cache identity](../../docs/reference/cache-identity.md)
- [Client Cache Schedule](../../docs/guide/client-cache-schedule.md)
- [Deployment](../../docs/reference/deployment.md) · [Cluster bus](../../docs/reference/cluster-bus.md)
