# CacheOrchestrator.Admin

Separate **Admin App** that fans out to **Local Admin** APIs on your application instances.

It does **not** sit on the caching hot path. Instances remain independent; this process only aggregates HTTP results and hosts a lightweight SPA.

| Item | Path |
|------|------|
| Project | `src/CacheOrchestrator.Admin` |
| UI (static) | `wwwroot/` — modular ES modules under `wwwroot/js/` |
| Tech docs | [docs/admin.md](../../docs/admin.md) |
| Local Admin (library) | `src/CacheOrchestrator/Admin/` |

## Prerequisites

On **each** app instance enable Local Admin and map the endpoints:

```json
"Cache": {
  "Admin": {
    "Enabled": true,
    "ApiKey": "dev-admin-key",
    "InstanceId": "app-1"
  }
}
```

```csharp
app.MapCacheOrchestratorAdmin();
```

The Admin App’s `CacheAdmin:ApiKey` must match each instance’s `Cache:Admin:ApiKey` (sent as `X-Cache-Admin-Key`).

## Configure instances

`appsettings.json` → `CacheAdmin`:

```json
{
  "CacheAdmin": {
    "ApiKey": "dev-admin-key",
    "RequestTimeoutMs": 3000,
    "Parallelism": 8,
    "LocalPathPrefix": "/cache-admin/local",
    "Instances": [
      { "id": "app-1", "url": "http://localhost:5290" },
      { "id": "app-2", "url": "http://localhost:5291" }
    ]
  }
}
```

| Option | Meaning |
|--------|---------|
| `ApiKey` | Shared secret for Local Admin calls |
| `RequestTimeoutMs` | Per-instance HTTP timeout |
| `Parallelism` | Max concurrent fan-out calls |
| `LocalPathPrefix` | Path prefix on each instance (default `/cache-admin/local`) |
| `Instances[].id` | Stable id used in the UI and `scope=instance:{id}` |
| `Instances[].url` | Base URL of the target app (no path) |

## Run

```bash
dotnet run --project src/CacheOrchestrator.Admin
```

| URL | Purpose |
|-----|---------|
| http://localhost:5188/ | SPA UI |
| http://localhost:5188/scalar/v1 | OpenAPI (Development) |
| http://localhost:5188/health | Process health |

Typical local pairing: run **Minimal** sample with Local Admin enabled, then point `CacheAdmin:Instances` at that port.

## UI (hash routes)

Chrome layout (top → bottom):

1. App brand  
2. **Header metrics strip** (same bar style as menu): `N/M up`, hints, pipeline, OC hit, Origin, Req, Inv  
3. **Menu strip**: Overview · Instances · Domains · Endpoints · Hints · refresh · Operations · API  

| Route | Page |
|-------|------|
| `#/overview` | KPIs, pipeline, alerts, instances; domains/endpoints sorted over **all** rows then **top 5** shown |
| `#/endpoints` | Endpoint list (search, multi-instance/domain, sort, page) |
| `#/endpoints?route=GET+%2Fhello` | Endpoint detail (OC/FC, by-instance, hints) |
| `#/domains` | Domain list (search, instance filter, sort) |
| `#/domains?name=hello` | Domain detail + config + nested endpoints |
| `#/instances` | Instance health (search, sort) — status, Req, uptime, latency |
| `#/instances?id=…` | Instance detail |
| `#/hints` | Flattened recommendations (+ optional **mock** toggle) |
| `#/operations` | Invalidate / version / TTL fan-out |

**Auto-refresh** (Grafana-style) lives in the menu strip; interval is stored in `localStorage`.

### Metrics mental model

- Prefer **request shares** (`hitShare` of total requests) in the primary UI.  
- Layer **rates** (`hitRate` among OC-only or FC-only traffic) are secondary — a high FC miss rate with a low origin share is usually not a cluster-wide problem.  
- Instance health comes from Local Admin `GET …/health` (uptime, request sum, Healthy flag). Fan-out maps: HTTP OK + Healthy → **Healthy**, HTTP OK + !Healthy → **Degraded**, failure/timeout → **Down**.

## Frontend modules

No bundler. The browser loads ES modules:

```
wwwroot/js/
  app.js           entry (bootstrap)
  dom.js           $ / main
  api.js           fetch wrapper for /api/*
  format.js        esc, pct, units, pipeline bar
  hints.js         badges, lists, collectHintRows
  hints-mock.js    REMOVE LATER — mock recommendation catalog
  filters.js       multi-select, sort options, client sort/search
  tables.js        entity tables + empty states
  router.js        hash parse / navigate
  shell.js         header metrics + auto-refresh
  views.js         all pages + route()
```

Comments in code are English-only. Search **`REMOVE LATER — Hint mockup`** to find temporary mock UI.

## Admin App API

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/overview` | Cluster overview + instances + top endpoints + hint summary |
| GET | `/api/instances` | Health probe fan-out |
| GET | `/api/stats?scope=all\|instance:{id}&groupByInstance=` | Aggregated live stats |
| GET | `/api/endpoints?sort=&take=&skip=&search=&instances=&domains=` | Endpoint list |
| GET | `/api/domains` | Domain config snapshots (fan-out) |
| POST | `/api/invalidate` | Fan-out invalidation (`target`: `all` \| `instance:{id}`) |
| POST | `/api/domains/{domain}/version` | Fan-out version overlay |
| PATCH | `/api/domains/{domain}/ttl` | Fan-out TTL overlay |

Partial success is returned per instance in `results[]`.

## Notes

- Runtime Version/TTL overlays are **process-local** on each instance — fan-out applies the same change to every selected target.  
- History / “last 1h” trends are out of scope here (use OTLP/Prometheus).  
- This project is **not** packed as a NuGet library (`IsPackable=false`).  
- Deeper architecture and Local Admin contract: [docs/admin.md](../../docs/admin.md).
