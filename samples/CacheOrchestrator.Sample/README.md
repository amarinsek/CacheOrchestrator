# CacheOrchestrator Sample Playground

The interactive companion to the [Minimal sample](../CacheOrchestrator.Minimal). Use the browser UI to follow the Getting started flow, inspect cache decisions, change domain settings, and explore Redis and multi-instance topologies without writing a new project.

<img src="../../docs/assets/sample-playground.png" alt="CacheOrchestrator Sample Playground showing cache controls and response diagnostics" width="800" />

---

## Choose your path

| Path | Best for |
|------|----------|
| **A. Solo (host)** | Fastest loop; settings editor; single process. See below. |
| **B. Topology labs (Docker)** | Learn **cache layouts** + Admin + Redis with one command. See [labs/README.md](labs/README.md). |

---

## Solo (host)

```bash
dotnet run --project samples/CacheOrchestrator.Sample
```

Open the printed URL (http://localhost:5289 by default).

Keep **Disable browser HTTP cache** enabled to observe server Output Cache and Data Cache decisions. Uncheck it when you want to see the browser serve a response from client cache.

This playground can write `appsettings.json` from the browser. That is for this sample only.

## What to try

- **Getting started** tab: the complete guide flow — `GET /api/promotions`, then generic `GET`/`PUT /api/products/42` with a visible price update and entity invalidation. Follow [getting-started.md](../../docs/guide/getting-started.md) alongside the UI.
- **Vary playground** tab: separate entries for JSON/XML and the allowlisted `lang` query value, while tracking parameters stay outside the key.
- **POST identity** tab: read-only search POST with a named contract vs create POST without identity. See [POST identity](#post-identity-playground).
- **appsettings.json** (top right) opens an editor. Change a Version or TTL, save, and the process reloads configuration so the **next request** uses the new settings. That does **not** purge cache — use **Invalidate domain** (or entity) when you want a separate invalidation.
- **Client Cache Schedule.** Set `ScheduledUpdateUtc` on a domain and watch the phase on each fetch:
  - **calm** — far from the cutover; client `max-age` is at its maximum
  - **approaching** — `max-age` falls toward the floor
  - **hold** — the scheduled time has passed; `max-age` stays at the floor

## Reading responses

Each playground tab keeps its own response history and request count. Switching tabs preserves the other histories; **Clear log** clears only the active tab.

**Disable browser HTTP cache** is enabled by default and uses Fetch `cache: 'no-store'`. This bypasses browser cache only; it does not disable Output Cache or Data Cache on the server. Uncheck it to demonstrate client `max-age` and **BROWSER-CACHE**.

The Playground sends a unique demo request ID with each request. The app echoes it on every real network response, including an Output Cache hit. If the returned ID is older, the response came from browser cache; the UI reports that the server was not contacted instead of repeating the cached response's old `X-Cache` value.

Response badges summarize `X-Cache` fields (`oc=`, `dc=`, and `fa=`):

- **BROWSER-CACHE** — client cache served the response (only when **Disable browser HTTP cache** is off)
- **OC-HIT** — Output Cache served the HTTP response (`oc=hit`; `dc`/`fa` omitted)
- **OC-MISS DC-HIT** — handler ran; Data Cache had the object (`dc=hit`, no `fa`)
- **OC-MISS DC-STALE FACTORY** — fail-safe stale from Data Cache (`dc=stale; fa=run`)
- **OC-MISS DC-MISS FACTORY** — both layers missed; factory ran (`dc=miss; fa=run`)
- **OC-OFF** / **DC-OFF** — that layer is disabled for the domain. **FACTORY** appears whenever `fa=run`.

## Getting started playground

Open the **Getting started** tab and work through its three numbered steps:

- Fetch promotions twice to see the Output Cache hit.
- Fetch product `42` twice to see Data Cache and Output Cache working together.
- Enter a new price and select **Update price**. The `PUT` writes the value and invalidates `products/42`; the next GET misses the server caches and returns the new price.

Suggested UI flow: promotions twice → product twice → update price → product once.

```bash
curl -i http://localhost:5289/api/products/42

curl -i -X PUT http://localhost:5289/api/products/42 \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"Demo Widget\",\"price\":12.5}"

curl -i http://localhost:5289/api/products/42
```

The additional `/api/crud/products` endpoints are available as a separate sample, including an uncached list at `GET /api/crud/products`. Background: [domain-profiles.md](../../docs/guide/domain-profiles.md).

## Vary playground

Open the **Vary playground** tab. The `vary-demo` domain uses `VaryByAccept` and allowlists `lang` with `VaryByQueryKeys`.

The allowlisted `lang` value creates a different cache entry. Tracking parameters such as `utm_source=demo` are omitted from keys; see [cache-keys.md](../../docs/reference/cache-keys.md).

Suggested UI flow: fetch twice → change `lang` → MISS → switch `Accept` to XML → MISS → add only `utm_source` without changing `lang` → HIT for the existing variant.

## POST identity (playground)

Open the **POST identity playground** tab. This panel demonstrates **read-only POST** Output Cache (and data-cache keys) via a named contract.

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
