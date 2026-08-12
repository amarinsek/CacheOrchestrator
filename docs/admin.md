# CacheOrchestrator Admin

Operator and integrator guide for the **Local Admin** API (in-process on each app) and the separate **Admin App** (fan-out + SPA).

| Component | Location | Distributed as | Role |
|-----------|----------|----------------|------|
| **Local Admin API** | `src/CacheOrchestrator/Admin/` | **NuGet** `CacheOrchestrator` (opt-in at runtime) | Per-process stats, health, invalidate, version/TTL overlays |
| **Admin App** | `src/CacheOrchestrator.Admin/` | **Not** a NuGet package (`IsPackable=false`) | Multi-instance fan-out host + browser UI |

| Doc | Audience |
|-----|----------|
| This page | Architecture, security, production checklist |
| [Admin App README](../src/CacheOrchestrator.Admin/README.md) | Run, configure instances, UI map |
| [admin-hints.md](admin-hints.md) | Recommendation rules: formulas, catalogue, how to add |

---

## Who should use what

| You want… | Use |
|-----------|-----|
| Counters / invalidate / runtime Version·TTL **on one process** | Local Admin only (curl, script, your own tool) |
| Cluster dashboard, multi-node ops UI, recommendation hints | Admin App + Local Admin on **each** target |
| Metrics / time series (“last 1h”) | OTLP / Prometheus — **not** Admin (lifetime counters only) |

**Warning:** Admin is an **ops** surface. Writes (invalidate, version, TTL) change cache behaviour on live processes. Restrict who can reach it.

---

## Architecture

```
┌────────────────────┐     HTTP fan-out      ┌──────────────────────────┐
│  Admin App         │ ──────────────────►   │ App instance A           │
│  - CacheAdmin:*    │   X-Cache-Admin-Key   │ MapCacheOrchestratorAdmin│
│  - /api/* + SPA    │                       │ /cache-admin/local/*     │
│  (browser → open)  │ ──────────────────►   │ App instance B           │
└────────────────────┘                       └──────────────────────────┘
        ▲
        │  no built-in login today
        │  protect with network / SSO proxy
     Operators
```

- Admin App is **never** on the end-user caching hot path.  
- Each instance keeps its **own** L1 Output Cache / FusionCache counters.  
- Aggregation is **sum / recompute shares** (`StatsAggregator`, `AdminStatsMath`).  
- Runtime **Version** and **TTL** overlays are **process-local** on each node — fan-out must hit every instance that should change.

---

## Distribution

| Piece | How users get it |
|-------|------------------|
| Local Admin | Ships inside **`CacheOrchestrator`** NuGet. No extra package. Default **disabled** (`Cache:Admin:Enabled` = false). |
| Admin App | Source in repo; run with `dotnet run` / `dotnet publish`, or ship your own **container / release zip**. Not published to nuget.org. |

**Good practice:** document Local Admin in the library README; run Admin App as an internal ops service (Docker/Helm, VPN-only), not as a public SaaS endpoint.

---

## Local Admin (library)

### Enable (each app instance)

```json
"Cache": {
  "InstanceId": "app-1",
  "Admin": {
    "Enabled": true,
    "ApiKey": "use-a-strong-secret-in-production",
    "RoutePrefix": "/cache-admin/local",
    "TrackEndpoints": true,
    "TrackLatency": false
  }
}
```

```csharp
app.UseCacheOrchestrator();
// …
app.MapCacheOrchestratorAdmin(); // after routing is available; safe no-op when Admin disabled
```

| Option | Default | Notes |
|--------|---------|--------|
| `Cache:InstanceId` | machine name | Single process id for Admin, cluster bus, diagnostics |
| `Admin:Enabled` | `false` | No routes, no counter cost when false |
| `Admin:ApiKey` | empty | Empty + Enabled ⇒ **open** endpoints (dev only; logs a warning) |
| `Admin:RoutePrefix` | `/cache-admin/local` | Must match Admin App `LocalPathPrefix` |
| `Admin:TrackEndpoints` | `true` | Per-route counters |
| `Admin:TrackLatency` | `false` | Extra cost if true |

Process identity is **`Cache:InstanceId`** (not under Admin). Same id is used by the optional cluster bus.

### Cluster distribute (with CacheOrchestrator.Bus)

When the HTTP bus is enabled, Local Admin mutation bodies accept **`distribute`** (default `false`):

| Endpoint | `distribute: false` | `distribute: true` |
|----------|---------------------|--------------------|
| `POST …/invalidate` | This process only | Local + peers via bus |
| `POST …/domains/{d}/version` | Local Version overlay | Local + `VersionBumpCommand` |
| `PATCH …/domains/{d}/ttl` | Local TTL overlay | Local + `TtlPatchCommand` |

Do **not** combine Admin App full fan-out **and** `distribute: true` for the same action. Prefer either multi-target Admin App calls without distribute, or single-target + `distribute: true` when the bus owns peer membership.

Receive path for peers: `MapCacheOrchestratorHttpBus()` (not gated on `Admin:Enabled`).

### Auth header

When `ApiKey` is set, every Local Admin call must send:

```http
X-Cache-Admin-Key: <same-as-Cache:Admin:ApiKey>
```

Comparison is fixed-time. Wrong/missing key ⇒ `401 Unauthorized`.

### Important endpoints

Base path = `RoutePrefix` (default `/cache-admin/local`).

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/health` | `Healthy`, `InstanceId`, `StartedAtUtc`, `UptimeSeconds`, `Requests` |
| GET | `/stats` | Live domain/endpoint counters (shares + rates) |
| GET | `/endpoints` | Discovered + counted routes |
| GET | `/domains` | Effective domain options snapshot |
| POST | `/invalidate` | Domain / entity invalidation |
| POST | `/domains/{name}/version` | Runtime version overlay |
| PATCH | `/domains/{name}/ttl` | Runtime TTL overlay |

Responses are **not** stored in Output Cache (`NoStore` on the admin group).

### Stats model (shares vs rates)

1. **Output Cache** events on HTTP responses (hit / miss / bypass).  
2. **FusionCache** events on data path (hit / miss / stale / bypass, factory runs/failures).  

| Metric type | Question it answers |
|-------------|---------------------|
| **Request share** (`hitShare`, `originShare`, …) | Of **all** requests, what fraction? |
| **Layer rate** (`hitRate`, …) | Of traffic that **reached that layer**, what fraction? |

**Warning:** A high FC **miss rate** with very high OC hit share is often normal (Fusion barely runs). Prefer **origin share** / pipeline for cluster health. Recommendation rules follow the same preference — see [admin-hints.md](admin-hints.md).

### Health semantics (Admin App mapping)

| Probe result | Instance status in UI |
|--------------|------------------------|
| HTTP OK + `Healthy == true` | **Healthy** |
| HTTP OK + `Healthy == false` | **Degraded** |
| Timeout / connection error / 401 | **Down** |

`Requests` / uptime on the instance row come from health when the probe succeeds.

### Limitations (Local Admin)

- Counters are **process lifetime** (reset on restart), not sliding windows.  
- Version/TTL overlays do **not** replicate to other nodes by themselves.  
- Not a substitute for Redis backplane / shared config for multi-instance cache coherence.

---

## Admin App (process)

Standalone **net10.0** host (ops tool; target apps may still be net8/net10).

### Configuration

Section: `CacheAdmin` → `CacheAdminOptions`.

```json
{
  "CacheAdmin": {
    "ApiKey": "use-a-strong-secret-in-production",
    "RequestTimeoutMs": 3000,
    "Parallelism": 8,
    "LocalPathPrefix": "/cache-admin/local",
    "Instances": [
      { "id": "app-1", "url": "https://app-1.internal:8080" },
      { "id": "app-2", "url": "https://app-2.internal:8080" }
    ]
  }
}
```

| Key | Description |
|-----|-------------|
| `ApiKey` | Sent as `X-Cache-Admin-Key` to every instance (**must match** each app’s `Cache:Admin:ApiKey`) |
| `RequestTimeoutMs` | Per-call timeout (1–120000) |
| `Parallelism` | Max concurrent fan-out (1–64) |
| `LocalPathPrefix` | Path on each instance (must match `RoutePrefix`) |
| `Instances[].id` | Stable UI / filter id |
| `Instances[].url` | **Base URL only** (scheme + host + port) — no `/cache-admin/...` path |

### Run (local)

```bash
dotnet run --project src/CacheOrchestrator.Admin
```

Default UI: `http://localhost:5188/` (see launchSettings). Pair with Minimal sample + Local Admin on the port listed under `Instances`.

Quick operator steps: [Admin App README](../src/CacheOrchestrator.Admin/README.md).

### What the SPA shows

- Chrome: brand → **metrics strip** (`N/M up`, pipeline, OC/Origin, Req, Inv, hints) → **menu**  
- Overview: instances; **top 5 domains** and **top 5 endpoints** after sorting the **full** aggregated lists  
- Lists: filters, search, sort; detail pages; Operations fan-out; Hints page  
- Auto-refresh interval in `localStorage`  

### Recommendation hints

Evaluated **only in the Admin App** after fan-out aggregation (`RecommendationHints`). UI does not invent rules.

Details: [admin-hints.md](admin-hints.md).

### Admin App HTTP API (for the SPA / automation)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/overview` | Cluster overview + instances + top domains/endpoints + hints |
| GET | `/api/instances` | Health probe fan-out |
| GET | `/api/stats?scope=all\|instance:{id}&groupByInstance=` | Aggregated live stats |
| GET | `/api/endpoints?…` | Endpoint list (search/sort/page/filters) |
| GET | `/api/domains` | Domain config fan-out |
| POST | `/api/invalidate` | Fan-out invalidation |
| POST | `/api/domains/{domain}/version` | Fan-out version overlay |
| PATCH | `/api/domains/{domain}/ttl` | Fan-out TTL overlay |

Partial success is reported per instance in `results[]`.

**Warning:** These `/api/*` routes currently have **no application-level authentication**. Anyone who can reach the Admin App can read stats and run operations. Protect the host (see [Security](#security)).

---

## Security

API key is the **intended** machine-to-machine credential for Local Admin — not a temporary mock. Sample values like `dev-admin-key` are **dev-only**.

### Two different trust boundaries

| Path | Protected by today? |
|------|---------------------|
| **Admin App → Local Admin** on each app | Optional shared secret `X-Cache-Admin-Key` (required in production) |
| **Browser / user → Admin App** | **No built-in login** — network / reverse-proxy auth only |

So: the key stops strangers from calling `/cache-admin/local` on your apps **if** they cannot reach Admin App *or* guess the key. It does **not** by itself decide which humans may open the dashboard.

### Production checklist

1. **Network**  
   - Local Admin: **not** on the public internet (private mesh, allowlist Admin App only).  
   - Admin App: VPN, bastion, internal ingress, or zero-trust access — not a public anonymous URL.

2. **Shared API key**  
   - Strong random secret (e.g. 32+ bytes, base64).  
   - Same value on every instance (`Cache:Admin:ApiKey`) and Admin App (`CacheAdmin:ApiKey`).  
   - From a secret store (K8s Secret, Key Vault, …) — **never** commit production keys.  
   - Empty `ApiKey` with `Enabled=true` leaves Local Admin **open** (logs a warning) — do not do this outside local dev.

3. **Human access to Admin App**  
   Because SPA/`/api` has no first-party auth, put in front of it one of:  
   - OAuth2 / OIDC proxy (e.g. oauth2-proxy, Azure App Service auth, Cloudflare Access)  
   - mTLS / service mesh only for operators  
   - VPN-only deployment  

4. **TLS**  
   HTTPS for browser → Admin App and Admin App → instances so the key is not sent in clear text.

5. **Least privilege & audit**  
   Invalidate / version / TTL are **mutations**. Limit who can open Operations. Prefer platform logging of who accessed the admin host.  
   Future hardening (optional): separate read vs write keys — not implemented today.

6. **Do not rely on**  
   - API key alone without network isolation  
   - Sample `dev-admin-key` in shared environments  
   - Shipping Admin App as an unauthenticated public cloud URL  

### Local Admin without Admin App

You may enable Local Admin for scripts only. Still set `ApiKey` and lock down network if the process is reachable outside localhost.

---

## Common pitfalls

| Symptom | Likely cause |
|---------|----------------|
| All instances **Down** | Wrong URL/port; Local Admin not mapped; firewall; **401** wrong/missing ApiKey |
| Empty domains/endpoints | No traffic yet; all targets down; filters set to **None** |
| Version/TTL “didn’t stick” cluster-wide | Overlay is **process-local**; target was `instance:x` only, or a node was down during fan-out |
| High FC miss rate, everything “fine” | Prefer **origin share** / OC hit share — see shares vs rates |
| Scalar OpenAPI missing | Only mapped in **Development** on Admin App |
| CORS issues calling Local Admin from a browser | Prefer Admin App fan-out; Local Admin is for server-side callers |

---

## Out of scope

- Sliding-window / “last 1h” history (use Prometheus / OTLP).  
- Redis topology management.  
- Publishing Admin App as a NuGet library.  
- Built-in OIDC login UI inside Admin App (use edge auth for now).

---

## Related docs

- [admin-hints.md](admin-hints.md) — recommendation rule formulas  
- [observability.md](observability.md) — metrics / `X-Cache` / health checks  
- [invalidation.md](invalidation.md) — domain/entity invalidation model  
- [configuration.md](configuration.md) — domain options binding  
- [architecture.md](architecture.md) — library layers  
- [deployment.md](deployment.md) — multi-instance topologies  
