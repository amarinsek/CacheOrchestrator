# CacheOrchestrator Admin

Two pieces work together:

- **Admin API** — opt-in HTTP on each application process (`Cache:Admin:Enabled`, `MapCacheOrchestratorAdmin`). Stats, health, invalidate, Version and TTL overlays. Ships in the core NuGet package; off by default.
- **Admin App** — a separate host (`src/CacheOrchestrator.Admin`) that calls those APIs and serves the operator UI. It is not a NuGet package.

This page covers architecture, security, and production. To run the App: [Admin README](../src/CacheOrchestrator.Admin/README.md). Hint rules: [admin-hints.md](admin-hints.md).

- One process, curl or a script — enable the Admin API.
- A dashboard across instances — Admin App, with the Admin API on each target.
- Time series (“last hour”) — optional **Admin App → Prometheus** (`CacheAdmin:Metrics`). Lifetime counters stay on Local Admin; sliding windows come from scraped `CacheOrchestrator` meter series.

Writes (invalidate, Version, TTL) change live cache state. Restrict who can reach these endpoints.

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
- Runtime **Version** and **TTL** overlays are **process-local** on each node unless the optional [cluster bus](cluster-bus.md) publishes them (`distribute: true` / Admin App **bus-distribute**). Without bus, Admin App **fan-out** must hit every instance that should change.

---

## Distribution

| Piece | How users get it |
|-------|------------------|
| Admin API | Ships inside **`CacheOrchestrator`** NuGet. No extra package. Default **disabled** (`Cache:Admin:Enabled` = false). |
| Admin App | Source in repo; run with `dotnet run` / `dotnet publish`, or ship your own **container / release zip**. Not published to nuget.org. |

Run the Admin App as an internal ops service (Docker or Helm, VPN only).

---

## Admin API (library)

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

When the HTTP bus is enabled, Admin API mutation bodies accept **`distribute`** (default `false`):

| Endpoint | `distribute: false` | `distribute: true` |
|----------|---------------------|--------------------|
| `POST …/invalidate` | This process only | Local + peers via bus |
| `POST …/domains/{d}/version` | Local Version overlay | Local + `VersionBumpCommand` |
| `PATCH …/domains/{d}/ttl` | Local TTL overlay | Local + `TtlPatchCommand` |

**Admin App** probes `GET …/cluster/info` on each configured instance (`GET /api/distribution`):

| Capability | Write behaviour |
|------------|-----------------|
| No bus | **fan-out** — HTTP to every target with `distribute:false` |
| Bus enabled (Static/ServiceDiscovery) | **bus-distribute** — one healthy origin with `distribute:true` (peers via bus) |

The Operations UI shows a banner and the mode used for the last result. Never combine full Admin App fan-out **and** `distribute:true` for the same action — the App chooses one path automatically.

Receive path for peers: `MapCacheOrchestratorHttpBus()` (not gated on `Admin:Enabled`).

Deep dive (membership, commands, metrics, security): **[cluster-bus.md](cluster-bus.md)**.

### Auth header

When `ApiKey` is set, every Admin API call must send:

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

### Limitations (Admin API)

- Counters are **process lifetime** (reset on restart), not sliding windows.  
- Version/TTL overlays do **not** replicate to other nodes by themselves.  
- Several instances stay coherent through Redis L2 and the backplane, shared configuration, or the [cluster bus](cluster-bus.md).

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
| `DownReprobeSeconds` | After an instance is Down, skip HTTP to it for this many seconds (5–300, default 15), then re-probe |
| `LocalPathPrefix` | Path on each instance (must match `RoutePrefix`) |
| `Instances[].id` | Stable UI / filter id |
| `Instances[].url` | **Base URL only** (scheme + host + port) — no `/cache-admin/...` path |
| `Metrics` | Optional Prometheus-compatible store for the **Metrics** page (see below) |

### Metrics store (time series)

Admin App can query an external Prometheus-compatible HTTP API (Prometheus, Mimir, VictoriaMetrics, Thanos Query) for windowed charts. **No core library changes** — the apps only need the usual OTel/Prometheus scrape of meter `CacheOrchestrator`.

Minimal config (everything else has defaults). The Admin App in this repo defaults to local Prometheus:

```json
"CacheAdmin": {
  "Metrics": {
    "Enabled": true,
    "Provider": "Prometheus",
    "BaseUrl": "http://localhost:9090"
  }
}
```

Dev stack (Docker Prometheus + Playground `/metrics` on port 5289): [deploy/prometheus/README.md](../deploy/prometheus/README.md).

| Key | Default | Notes |
|-----|---------|--------|
| `Enabled` | `false` | Off → UI shows “not configured”, no probe |
| `Provider` | `Prometheus` | Only Prometheus HTTP API v1 today |
| `BaseUrl` | empty | Required when enabled |
| `TimeoutMs` | `5000` | Probe / query timeout |
| `DefaultRange` | `1h` | UI default (`15m` / `1h` / `6h` / `24h` / `7d`) |
| `BearerToken` | empty | Optional `Authorization: Bearer` |
| `PathPrefix` | empty | e.g. `/prometheus` behind a reverse proxy |

When **not configured**, the Metrics page explains how to enable it; Overview omits history cards. When **configured but unreachable**, the UI shows **Disconnected** (no fake zeros).

Admin App API: `GET /api/metrics/status`, `/catalog`, `/series`, `/summary`.

`GET /api/metrics/series` query params: `range`, `panels`, `domains`, `instances` (scrape label `instance_id`), `routes` (stable endpoint key, e.g. `GET /api/catalog`). Detail pages embed scoped charts (domain / instance / endpoint). Endpoint series need core `Cache:Metrics:IncludeEndpointLabel` (default true) and samples in the selected range—empty charts show a neutral notice, not a hard “feature disabled” claim.

### Run (local)

```bash
dotnet run --project src/CacheOrchestrator.Admin
```

Default UI: `http://localhost:5188/` (see launchSettings). Default `Instances` point at the Playground sample (`:5289`). Metrics time series use Playground scrape via Prometheus.

Quick operator steps: [Admin App README](../src/CacheOrchestrator.Admin/README.md).

### What the SPA shows

- Chrome: brand → **metrics strip** (`N/M up`, pipeline, OC/Origin, Req, Inv, hints, optional metrics store pill) → **menu**  
- Overview: instances; **top 5 domains** and **top 5 endpoints** after sorting the **full** aggregated lists; optional **last 1h** embed when Metrics store is connected  
- Lists: filters, search, sort; detail pages; Hints page  
- **Metrics** (`#/metrics`): window charts from Prometheus (req rate, OC/FC shares, invalidations, schedule phase, cluster failures, FC p95)  
- **Operations** (`#/operations`): invalidate / version / TTL; banner **HTTP fan-out** vs **Cluster bus (distribute)**; cluster probe table; last-run mode in result  
- Auto-refresh interval in `localStorage`  

### Recommendation hints

Evaluated **only in the Admin App** after fan-out aggregation (`RecommendationHints`). UI does not invent rules.

Details: [admin-hints.md](admin-hints.md).

### Admin App HTTP API (for the SPA / automation)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/overview` | Cluster overview + instances + top domains/endpoints + hints |
| GET | `/api/instances` | Health probe fan-out |
| GET | `/api/distribution` | Probe `…/cluster/info`; recommended write mode (fan-out vs bus-distribute) |
| GET | `/api/stats?scope=all\|instance:{id}&groupByInstance=` | Aggregated live stats |
| GET | `/api/endpoints?…` | Endpoint list (search/sort/page/filters) |
| GET | `/api/domains` | Domain config fan-out |
| GET | `/api/metrics/status` | Metrics store probe (`NotConfigured` / `Disconnected` / `Connected`) |
| GET | `/api/metrics/catalog` | Allowlisted chart panels |
| GET | `/api/metrics/series?range=&panels=&domains=` | Range series for panels |
| GET | `/api/metrics/summary?range=` | Window KPI snapshot |
| POST | `/api/invalidate` | Invalidate (auto fan-out or bus-distribute) |
| POST | `/api/domains/{domain}/version` | Version overlay write |
| PATCH | `/api/domains/{domain}/ttl` | TTL overlay write |

Write responses include `distributionMode`, `distribute`, `distributionSummary`, optional `busOriginInstanceId`, and per-instance `results[]`.

**Warning:** These `/api/*` routes currently have **no application-level authentication**. Anyone who can reach the Admin App can read stats and run operations. Protect the host (see [Security](#security)).

---

## Security

API key is the **intended** machine-to-machine credential for Admin API — not a temporary mock. Sample values like `dev-admin-key` are **dev-only**.

### Two different trust boundaries

| Path | Protected by today? |
|------|---------------------|
| **Admin App → Admin API** on each app | Optional shared secret `X-Cache-Admin-Key` (required in production) |
| **Browser / user → Admin App** | **No built-in login** — network / reverse-proxy auth only |

So: the key stops strangers from calling `/cache-admin/local` on your apps **if** they cannot reach Admin App *or* guess the key. It does **not** by itself decide which humans may open the dashboard.

### Production checklist

1. **Network**  
   - Admin API: **not** on the public internet (private mesh, allowlist Admin App only).  
   - Admin App: VPN, bastion, internal ingress, or zero-trust access — not a public anonymous URL.

2. **Shared API key**  
   - Strong random secret (e.g. 32+ bytes, base64).  
   - Same value on every instance (`Cache:Admin:ApiKey`) and Admin App (`CacheAdmin:ApiKey`).  
   - From a secret store (K8s Secret, Key Vault, …) — **never** commit production keys.  
   - Empty `ApiKey` with `Enabled=true` leaves Admin API **open** (logs a warning) — do not do this outside local dev.

3. **Human access to Admin App**  
   The Admin App has no built-in login. Put one of these in front of `/` and `/api`:  
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

### Admin API without Admin App

You may enable Admin API for scripts only. Still set `ApiKey` and lock down network if the process is reachable outside localhost.

---

## Common pitfalls

| Symptom | Likely cause |
|---------|----------------|
| All instances **Down** | Wrong URL/port; Admin API not mapped; firewall; **401** wrong/missing ApiKey |
| Empty domains/endpoints | No traffic yet; all targets down; filters set to **None** |
| Version/TTL “didn’t stick” cluster-wide | Overlay is **process-local** without bus; use fan-out to all nodes, or bus-distribute; node down during write |
| High FC miss rate, everything “fine” | Prefer **origin share** / OC hit share — see shares vs rates |
| Scalar OpenAPI missing | Only mapped in **Development** on Admin App |
| CORS issues calling Admin API from a browser | Prefer Admin App fan-out; Admin API is for server-side callers |

---

## Out of scope

- Built-in time-series database inside Admin App (use Prometheus / compatible store).  
- Free-form PromQL from the browser (panels are allowlisted server-side).  
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
