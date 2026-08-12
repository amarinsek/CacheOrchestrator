# CacheOrchestrator Admin

Technical reference for the **Local Admin** API (in-process, on each app) and the separate **Admin App** (fan-out + SPA).

| Component | Location | Role |
|-----------|----------|------|
| Local Admin | `src/CacheOrchestrator/Admin/` | Opt-in HTTP API on each process: live stats, health, invalidation, runtime overlays |
| Admin App | `src/CacheOrchestrator.Admin/` | Standalone process: configure instance list, fan-out, aggregate, host UI |

Quick start for operators: [src/CacheOrchestrator.Admin/README.md](../src/CacheOrchestrator.Admin/README.md).

---

## Architecture

```
┌────────────────────┐     HTTP fan-out      ┌──────────────────────────┐
│  Admin App         │ ──────────────────►   │ App instance A           │
│  - CacheAdmin:*    │   X-Cache-Admin-Key   │ MapCacheOrchestratorAdmin│
│  - /api/* aggregate│                       │ /cache-admin/local/*     │
│  - wwwroot SPA     │ ──────────────────►   │ App instance B           │
└────────────────────┘                       └──────────────────────────┘
```

- Admin App is **never** on the request hot path for end-user traffic.  
- Each instance keeps its own L1 Output Cache / FusionCache counters.  
- Aggregation is **sum / recompute shares** (see `StatsAggregator`, `AdminStatsMath`).  
- Runtime **version** and **TTL** overlays applied via Admin are **process-local** unless you fan-out to every node.

---

## Local Admin (library)

### Enable

```json
"Cache": {
  "Admin": {
    "Enabled": true,
    "ApiKey": "…",
    "InstanceId": "optional-stable-id"
  }
}
```

```csharp
app.MapCacheOrchestratorAdmin(); // typically after UseCacheOrchestrator
```

Default path prefix: `/cache-admin/local`. Authentication: header `X-Cache-Admin-Key` must match `ApiKey` when configured.

### Important endpoints

| Method | Path (under prefix) | Purpose |
|--------|---------------------|---------|
| GET | `/health` | `AdminHealthDto`: Healthy, InstanceId, StartedAtUtc, UptimeSeconds, Requests |
| GET | `/stats` | Live domain/endpoint counters with request shares + layer rates |
| GET | `/endpoints` | Discovered + counted routes |
| GET | `/domains` | Effective domain options snapshot |
| POST | `/invalidate` | Domain / entity invalidation |
| POST | `/domains/{name}/version` | Runtime version overlay |
| PATCH | `/domains/{name}/ttl` | Runtime TTL overlay |

### Health fields used by Admin App

| Field | Use in UI |
|-------|-----------|
| `Healthy` | Healthy vs Degraded when HTTP succeeds |
| `StartedAtUtc` / `UptimeSeconds` | Instances table + detail |
| `Requests` | Req column (process lifetime request sum from stats) |

If the HTTP call fails (timeout, 401, connection refused), Admin App marks the instance **Down** and clears uptime/request fields for that probe.

### Stats model (shares vs rates)

Counters are layered:

1. **Output Cache** events (hit / miss / bypass) on HTTP responses.  
2. **FusionCache** events (hit / miss / stale / bypass, factory runs/failures) on data cache.  

**Request shares** answer: “of all requests, what fraction was OC hit vs origin?”  
**Layer rates** answer: “of traffic that reached this layer, what fraction was hit?”  

Primary Admin UI and recommendation rules prefer **shares**. Treating FC miss **rate** as cluster health when OC hit share is ~100% is misleading.

Implementation: `AdminStatsMath`, `InMemoryAdminStatsCollector`, DTOs in `AdminDtos.cs`.

---

## Admin App (process)

### Configuration

Section: `CacheAdmin` → `CacheAdminOptions`.

| Key | Description |
|-----|-------------|
| `ApiKey` | Sent to every instance |
| `RequestTimeoutMs` | HttpClient timeout (validated 1–120000) |
| `Parallelism` | Max concurrent instance calls (1–64) |
| `LocalPathPrefix` | Combined with instance base URL |
| `Instances` | `{ id, url }[]` |

### Server modules

| Type | Role |
|------|------|
| `LocalAdminClient` | Typed HTTP to one instance |
| `AdminFanOutService` | Parallel fan-out, overview, endpoints list, ops |
| `StatsAggregator` | Merge multi-instance snapshots |
| `RecommendationHints` | Rule-based Critical / Warning / Info |
| `Program.cs` | `/api/*` + static files + Scalar (Development) |

### Overview payload highlights

- Instance list with health + optional per-instance `HintSummary`  
- Cluster pipeline / OC hit share / origin share  
- `TopDomains` / `TopEndpoints` — **full** aggregated domain/endpoint lists; Overview UI sorts by the selected key, then shows **top 5**  

- `HintSummary` + sample `TopHints`  
- Alerts (down/degraded/multi-instance notes)

### Recommendation hints

Server: `RecommendationHints.cs` (traffic thresholds, origin share, stale, invalidation ratio, instance spread, …).  
Client: render-only in `wwwroot/js/hints.js`.  

**Mock mode** (`hints-mock.js`, toggle on Hints page) injects catalog hints for UI design; remove when live density is enough (`REMOVE LATER — Hint mockup`).

---

## SPA structure

Hash router, no build step. Entry: `wwwroot/js/app.js` (`type="module"` from `index.html`).

| Module | Responsibility |
|--------|----------------|
| `dom.js` | `$`, `main()` |
| `api.js` | `fetch` + JSON error handling |
| `format.js` | Escape, %, thin-space units, pipeline bar |
| `hints.js` / `hints-mock.js` | Hint UI + optional mock catalog |
| `filters.js` | Multi-select All/none/filter, sort keys, client sort/search |
| `tables.js` | Endpoint / domain / instance tables, empty states |
| `router.js` | `#/path?query` parse & navigate |
| `shell.js` | Header metrics strip + auto-refresh timers |
| `views.js` | Page renderers + `route()` |

### Unit formatting

Always use thin space (U+2009) between number and unit in the UI: `5 m`, `11 ms`, `3 h`. Counts without units use locale formatting only.

### Filters

- Endpoints: search, multi instance/domain, sort (server list API).  
- Domains: search name, multi instance, sort (client).  
- Instances: search id/url, sort (client).  
- Overview: independent sort controls for instances table and top-5 endpoints.

---

## Security notes

- Treat Admin endpoints as **internal**. Prefer network policy + API key; do not expose Local Admin publicly without additional auth.  
- Admin App and instances should share a strong key in non-dev environments.  
- Operations (invalidate / version / TTL) mutate cache behaviour — audit access accordingly.

---

## Out of scope

- Time-series / “last 1h” charts (use Prometheus / OTLP dashboards).  
- Owning Redis topology beyond what target apps already configure.  
- Shipping Admin App as a NuGet package.

---

## Related docs

- [observability.md](observability.md) — metrics / `X-Cache` / health checks  
- [invalidation.md](invalidation.md) — domain/entity invalidation model  
- [configuration.md](configuration.md) — domain options binding  
- [architecture.md](architecture.md) — library layers  
