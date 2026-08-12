# CacheOrchestrator.Admin

Separate **Admin App** that fans out to Local Admin APIs on your application instances.

It does **not** sit on the caching hot path. Instances remain independent; this app only aggregates HTTP results and hosts a lightweight UI.

## Prerequisites

On each app instance:

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

## Run

```bash
dotnet run --project src/CacheOrchestrator.Admin
```

- UI: http://localhost:5188/  
- Scalar API: http://localhost:5188/scalar/v1  
- Health: http://localhost:5188/health  

## UI (hash routes)

| Route | Page |
|-------|------|
| `#/overview` | Dashboard: KPIs, pipeline, alerts, top endpoints |
| `#/endpoints` | Endpoint list (search / sort / paginate) |
| `#/endpoints?route=GET+%2Fhello` | Endpoint detail (OC + FC stale/factory, by-instance) |
| `#/domains` | Domain list |
| `#/domains?name=hello` | Domain detail + nested endpoints + config |
| `#/instances` | Instance health list |
| `#/instances?id=local-minimal` | Instance detail |
| `#/operations` | Invalidate / version / TTL fan-out |

Sticky header shows cluster health, pipeline, OC hit share, origin share, request totals (refreshes every 15s).

## Admin App API

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/overview` | Header + overview payload |
| GET | `/api/instances` | List + health probe |
| GET | `/api/stats?scope=all\|instance:{id}&groupByInstance=` | Aggregated live stats (shares + rates) |
| GET | `/api/endpoints?sort=&take=&skip=&search=&domain=&minRequests=` | Endpoint list |
| GET | `/api/domains` | Domain config snapshots (fan-out) |
| POST | `/api/invalidate` | Fan-out invalidation (`target`: `all` \| `instance:{id}`) |
| POST | `/api/domains/{domain}/version` | Fan-out version overlay |
| PATCH | `/api/domains/{domain}/ttl` | Fan-out TTL overlay |

Partial success is returned per instance in `results[]`.

**Metrics:** primary UI uses **request shares** (`hitShare` of total requests). Layer **rates** (`hitRate` among layer traffic) are secondary — avoid treating FC miss rate as cluster-wide when OC serves most traffic.

## Notes

- Runtime Version/TTL overlays are **process-local** on each instance — fan-out applies the same change to every target.
- History / “last 1h” metrics are out of scope (use OTLP/Prometheus).
- This project is **not** packed as a NuGet library (`IsPackable=false`).
