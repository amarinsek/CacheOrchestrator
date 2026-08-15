# CacheOrchestrator Sample Playground

A playground for CacheOrchestrator after the [Minimal sample](../CacheOrchestrator.Minimal). You get a browser UI to change TTLs, watch Client Cache Schedule phases, switch to Redis, and try entity invalidation — without writing a new project.

![Sample screenshot](../../docs/assets/sample-playground.png)

## Run

```bash
dotnet run --project samples/CacheOrchestrator.Sample
```

Open the printed URL (http://localhost:5289 by default).

**Disable browser HTTP cache** (header, next to **appsettings.json**, **on by default**) sets Fetch **`cache: 'no-store'`** so the browser always calls the app and you see **server** OC/FC hits. It does **not** send HTTP `Cache-Control: no-store` and does **not** turn off Output/Fusion on the server. Uncheck only to demo client `max-age` / BROWSER-CACHE.

This playground writes `appsettings.json` from the browser. That is for this sample only.

## What to try

- **Domain endpoints** panel: `Demo:Endpoints` from config (catalog, product, search, …). Add a line under `Demo:Endpoints` and restart (or save config) to expose another route.
- **Entity invalidation (CRUD)** panel: fixed `GET /api/crud/products/42` + **Update price (PUT)** — separate from the domain dropdown.
- **appsettings.json** (top right) opens an editor. Change a TTL, save, and the process reloads configuration. Cached domains for the edited entries are invalidated so the new values show on the next request.
- **Client Cache Schedule.** Set `ScheduledUpdateUtc` on a domain and watch the phase on each fetch:
  - **calm** — far from the cutover; client `max-age` is at its maximum
  - **approaching** — `max-age` falls toward the floor
  - **hold** — the scheduled time has passed; `max-age` stays at the floor
- **Disable browser HTTP cache** (header, default on) — bypass browser cache only; uncheck to demo BROWSER-CACHE.
- **Badges** on a response:
  - **BROWSER-CACHE** — client cache served the response (only when **Disable browser HTTP cache** is off)
  - **OC-HIT** — Output Cache served the HTTP response
  - **OC-MISS FC-HIT** — handler ran; FusionCache had the object
  - **OC-MISS FC-STALE** — fail-safe stale from Fusion
  - **OC-MISS FC-MISS FACTORY** — both layers missed; Fusion factory ran (not a “hit”)
- **Extra query params** (optional): e.g. `page=2` usually creates a **different** cache key. Tracking params such as `utm_source=demo` are omitted from keys (same entry as without them) — see [cache-keys.md](../../docs/cache-keys.md).

## CRUD (entity invalidation)

In the playground, open the **Entity invalidation (CRUD)** panel (not the domain endpoint list).

- **Invalidate entity** — purge OC/FC for `products/42` only (in-memory price unchanged).
- **Update price (PUT)** — enter a price, write store + entity invalidate → next Fetch shows that price.

Suggested UI flow: Fetch → Fetch twice (OC-HIT) → Invalidate entity → Fetch (FACTORY, same price) → set Price → Update price → Fetch (FACTORY, new price).

```bash
curl -i http://localhost:5289/api/crud/products/42

curl -i -X PUT http://localhost:5289/api/crud/products/42 \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"Demo Widget\",\"price\":12.5}"

curl -i http://localhost:5289/api/crud/products/42
```

`GET /api/crud/products` (list) is an uncached store dump — curl only. Background: [domain-profiles.md](../../docs/domain-profiles.md).

## Redis

The sample already calls `AddRedisBackend()`. Start Redis and switch providers in the editor:

```bash
docker run -d --name redis-demo -p 6379:6379 redis:7-alpine
```

```json
"OutputCache": { "Provider": "InMemory" },
"FusionCacheInstances": {
  "default": { "Provider": "Redis" }
},
"Redis": { "Configuration": "localhost:6379" }
```

Named Fusion instances and a second Redis: [deployment.md](../../docs/deployment.md).

## Admin API + Prometheus metrics

This sample enables the Local Admin API (`Cache:Admin`) and exports meter `CacheOrchestrator` at **http://localhost:5289/metrics** for Prometheus.

`Cache:Metrics:IncludeEndpointLabel` is **true** so OC/FC series include a stable `route` label (Admin endpoint key shape). Domain detail, instance detail, and endpoint detail in the Admin Console App can show window charts when Metrics storage is connected.

```bash
# Prometheus (sample/dev only — UI http://localhost:9090; not part of NuGet packages)
docker compose -f samples/CacheOrchestrator.Sample/deploy/prometheus/docker-compose.yml up -d

# This playground (scraped at host.docker.internal:5289)
dotnet run --project samples/CacheOrchestrator.Sample

# Traffic (UI or curl), then Admin Console App Metrics page
curl -i http://localhost:5289/api/catalog
dotnet run --project src/CacheOrchestrator.AdminConsole
# open http://localhost:5188/#/metrics
```

Details: [deploy/prometheus/README.md](deploy/prometheus/README.md) · [docs/admin.md](../../docs/admin.md).

## Next

- [Getting started](../../docs/getting-started.md)
- [Client Cache Schedule](../../docs/client-cache-schedule.md)
- [Documentation index](../../docs/README.md)
