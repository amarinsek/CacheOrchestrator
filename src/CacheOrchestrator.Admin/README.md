# CacheOrchestrator.Admin

![Admin App overview](../../docs/assets/admin-overview.png)

Separate **Admin App** that fans out to **Local Admin** APIs on your application instances.

It does **not** sit on the caching hot path. Instances remain independent; this app only aggregates HTTP results and hosts a lightweight SPA.

| Item | Path |
|------|------|
| Project | `src/CacheOrchestrator.Admin` (**net10.0**; not a NuGet package) |
| UI | `wwwroot/` — ES modules under `wwwroot/js/` |
| Full docs | [docs/admin.md](../../docs/admin.md) · [docs/admin-hints.md](../../docs/admin-hints.md) |
| Local Admin (in core library) | Ships with NuGet **CacheOrchestrator** — enable per app |

## Security (short)

| Trust boundary | Today |
|----------------|--------|
| Admin App → each app’s Local Admin | Shared secret header **`X-Cache-Admin-Key`** (`CacheAdmin:ApiKey` must match `Cache:Admin:ApiKey`) |
| Browser → Admin App (`/` and `/api/*`) | **No built-in login** — treat as internal; put VPN / SSO reverse-proxy in front in production |

- Sample key `dev-admin-key` is for **local development only**.  
- Empty API key on an enabled Local Admin leaves that instance’s admin routes **open** (dev warning in logs).  
- Operations (invalidate / version / TTL) **mutate** live cache state.

**Production:** strong secrets from a secret store, private network for Local Admin, TLS, and access control for humans on this host. Details and checklist: **[docs/admin.md — Security](../../docs/admin.md#security)**.

## Prerequisites (each target app)

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

Local Admin is **off by default** in the library. Target apps may be **net8 or net10**; this Admin App host is **net10**.

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
| `ApiKey` | Shared secret sent to Local Admin (`X-Cache-Admin-Key`) |
| `RequestTimeoutMs` | Per-instance HTTP timeout |
| `Parallelism` | Max concurrent fan-out calls |
| `LocalPathPrefix` | Must match each app’s `Cache:Admin:RoutePrefix` |
| `Instances[].id` | Stable id in the UI and `scope=instance:{id}` |
| `Instances[].url` | Base URL only (no admin path) |

Use environment variables / secret mounts for production keys (do not commit real secrets).

## Run

```bash
dotnet run --project src/CacheOrchestrator.Admin
```

| URL | Purpose |
|-----|---------|
| http://localhost:5188/ | SPA UI |
| http://localhost:5188/scalar/v1 | OpenAPI (Development only) |
| http://localhost:5188/health | Admin App process health |

Typical local pairing: Minimal sample with Local Admin enabled, then point `Instances` at that port.

## UI (hash routes)

Chrome (top → bottom): brand → **metrics strip** (`N/M up`, hints, pipeline, OC, Origin, Req, Inv) → **menu**.

| Route | Page |
|-------|------|
| `#/overview` | KPIs, pipeline, alerts; instances; top **5** domains & endpoints (sort over **all** rows) |
| `#/endpoints` | List (search, multi-instance/domain, sort, page) |
| `#/endpoints?route=…` | Endpoint detail |
| `#/domains` | List (search, instance filter, sort) |
| `#/domains?name=…` | Domain detail + config |
| `#/instances` | Health (search, sort) — status, Req, uptime, latency |
| `#/instances?id=…` | Instance detail |
| `#/hints` | Live recommendation hints |
| `#/operations` | Invalidate / version / TTL fan-out |

Auto-refresh interval is stored in `localStorage`.

### Metrics mental model

- Prefer **request shares** in the primary UI.  
- Layer **rates** are secondary (high FC miss rate with low origin share is often fine).  
- Health: Local Admin `GET …/health` → Healthy / Degraded / Down.

## Frontend modules

```
wwwroot/js/
  app.js       entry
  dom.js       query helpers
  api.js       /api/* client
  format.js    units, pipeline bar
  hints.js     badges / Hints page (render only)
  filters.js   multi-select, sort, search
  tables.js    entity tables
  router.js    hash routes
  shell.js     header + auto-refresh
  views.js     pages
```

## Admin App API

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/overview` | Overview payload |
| GET | `/api/instances` | Health fan-out |
| GET | `/api/stats?scope=…` | Aggregated stats |
| GET | `/api/endpoints?…` | Endpoint list |
| GET | `/api/domains` | Domain config fan-out |
| POST | `/api/invalidate` | Fan-out invalidation |
| POST | `/api/domains/{domain}/version` | Fan-out version overlay |
| PATCH | `/api/domains/{domain}/ttl` | Fan-out TTL overlay |

Partial success appears per instance in `results[]`. These routes are **not** authenticated by the app itself — see [Security](#security-short).

## Notes

- Runtime Version/TTL overlays are **process-local** per instance — fan-out must reach every node you care about.  
- No sliding-window history here (use OTLP/Prometheus).  
- How to distribute / harden for production: [docs/admin.md](../../docs/admin.md).  
