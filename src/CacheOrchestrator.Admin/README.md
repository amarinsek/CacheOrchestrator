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

## Admin App API

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/instances` | List + health probe |
| GET | `/api/stats?scope=all\|instance:{id}` | Aggregated live stats |
| GET | `/api/endpoints?sort=missRate&take=10` | Top endpoints |
| GET | `/api/domains` | Domain config snapshots (fan-out) |
| POST | `/api/invalidate` | Fan-out invalidation (`target`: `all` \| `instance:{id}`) |
| POST | `/api/domains/{domain}/version` | Fan-out version overlay |
| PATCH | `/api/domains/{domain}/ttl` | Fan-out TTL overlay |

Partial success is returned per instance in `results[]`.

## Notes

- Runtime Version/TTL overlays are **process-local** on each instance — fan-out applies the same change to every target.
- History / “last 1h” metrics are out of scope (use OTLP/Prometheus).
- This project is **not** packed as a NuGet library (`IsPackable=false`).
