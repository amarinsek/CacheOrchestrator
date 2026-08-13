# CacheOrchestrator Sample Playground

A playground for CacheOrchestrator after the [Minimal sample](../CacheOrchestrator.Minimal). You get a browser UI to change TTLs, watch Client Cache Schedule phases, switch to Redis, and try entity invalidation — without writing a new project.

![Sample screenshot](../../docs/assets/sample-playground.png)

## Run

```bash
dotnet run --project samples/CacheOrchestrator.Sample
```

Open the printed URL (http://localhost:5289 by default).

In DevTools → Network, enable **Disable cache**. The library sends ordinary `Cache-Control` headers; without that checkbox the browser answers from its own store and you will not see Output Cache or FusionCache hits. Setting `ClientTtlSeconds` to `1` in the editor has the same effect.

This playground writes `appsettings.json` from the browser. That is for this sample only.

## What to try

- **Endpoints** such as `/api/catalog` take their cache rules from `appsettings.json`. Add a line under `Demo:Endpoints` and restart to expose another route.
- **appsettings.json** (top right) opens an editor. Change a TTL, save, and the process reloads configuration. Cached domains for the edited entries are invalidated so the new values show on the next request.
- **Client Cache Schedule.** Set `ScheduledUpdateUtc` on a domain and watch the phase on each fetch:
  - **calm** — far from the cutover; client `max-age` is at its maximum
  - **approaching** — `max-age` falls toward the floor
  - **hold** — the scheduled time has passed; `max-age` stays at the floor
- **Badges** on a response:
  - **BROWSER-CACHE** — the browser did not go to the network
  - **OC-HIT** — Output Cache served the HTTP response
  - **OC-MISS FC-HIT** — the handler ran; FusionCache had the object
  - **MISS** — both layers missed; the factory ran
- **Append `utm_source=demo`** still hits. Known tracking parameters are omitted from cache keys.
- **Send `Cache-Control: no-store`** misses, as the request asked.

`fetch` reports HTTP 200 even when the browser used a 304 or a local copy. The Network tab shows the real exchange.

## CRUD

`GET /api/crud/products/{id}` caches one product. `PUT` updates it and invalidates that entity. `GET /api/crud/products` lists the in-memory store.

```bash
curl -i http://localhost:5289/api/crud/products/42

curl -i -X PUT http://localhost:5289/api/crud/products/42 \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"Demo Widget\",\"price\":12.5}"

curl -i http://localhost:5289/api/crud/products/42
```

The third request should miss and show the new price. Background: [domain-profiles.md](../../docs/domain-profiles.md).

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
