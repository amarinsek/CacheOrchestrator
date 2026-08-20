# Operations

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

How to see what the cache is doing, and which Admin document to open. This page does not replace the runbooks.

## Read order

| Document | Open it when |
|----------|----------------|
| **This page** | You need the map |
| [observability.md](../observability.md) | `X-Cache`, meter names, health checks, logs |
| [admin.md](../admin.md) | Admin API + Console architecture, security, endpoints |
| [Admin Console README](../../src/CacheOrchestrator.AdminConsole/README.md) | `dotnet run`, host `appsettings`, pages |
| [deploy/admin/README.md](../../deploy/admin/README.md) | Docker / GHCR image, volumes, logs |
| [admin-hints.md](../admin-hints.md) | How recommendation hints work in the repo |
| [hints/README.md](../../src/CacheOrchestrator.AdminConsole/hints/README.md) | Writing JSON hint rules |
| [cluster-bus.md](../cluster-bus.md) | Invalidate / Version / TTL on every instance |

## See a request: `X-Cache`

On domain endpoints, with `EmitDiagnosticsHeaders` at its default of `true`:

```http
X-Cache: domain=catalog; version=1; client=public; phase=n/a; oc=miss; fc=miss; fa=run; ms=12
```

- **oc** — Output Cache `miss`, `hit`, `bypass`, or `off`.
- **fc** — Fusion (`hit`, `miss`, …). Omitted when Output Cache already hit.
- **fa** — `run` when `fc` is present and is not `hit` (factory callback ran).
- **phase** — Client Cache Schedule, or `n/a`.

Hide the header from clients with `"EmitDiagnosticsHeaders": false`. Metrics continue.

Details: [observability.md](../observability.md), [FAQ](../faq.md#should-i-expose-x-cache-in-production).

## Meter and traces

Meter and activity source name: **`CacheOrchestrator`**.

Admin Console **traffic** stats (Overview, Domains, Endpoints, Hints, impact) are **Prometheus-only**. Scrape the meter; point `AdminConsole:Metrics` at Prometheus. Local Admin `GET …/stats` is process-lifetime diagnostics and is **not** used by the stats UI.

Details: [observability.md](../observability.md#metrics), [admin.md](../admin.md).

## Admin API vs Admin Console App

| Piece | What it is | Where it runs |
|-------|------------|----------------|
| **Admin API** | Opt-in HTTP on **each** app (`Cache:Admin:Enabled` + `MapCacheOrchestratorAdmin`) | Inside the core NuGet package; off by default |
| **Admin Console App** | Separate process that fans out to those APIs and serves the operator UI | `src/CacheOrchestrator.AdminConsole`; not a NuGet package; Docker on GHCR |

Use the API alone for curl/scripts (health, config, invalidate, Version, **PATCH settings**). Use the Console for a dashboard across instances. Writes change live cache state. Process-lifetime `GET …/stats` is obsolete for analytics.

**Console stats need Prometheus.** Without a Metrics store, health, config, and operations still work; charts and window stats do not.

Details: [admin.md](../admin.md), [FAQ](../faq.md#admin-api-vs-admin-console-app).

## Docker vs local Console

| How you run it | Config | Custom hints | Disabled codes |
|----------------|--------|--------------|----------------|
| `dotnet run` (Development) | Playground defaults (`:5289`) | `hints/*.json` | `hints/disabled.local.json` |
| Docker / Production | You mount instance list + ApiKey | `data/rules/*.json` | `data/disabled.local.json` |

Product pack `hints/core-hints.json` is **always** loaded. Do not mount over all of `/app/hints`.

Image: `ghcr.io/amarinsek/cacheorchestrator-admin-console`.

Details: [deploy/admin/README.md](../../deploy/admin/README.md), [Admin Console README](../../src/CacheOrchestrator.AdminConsole/README.md).

## Hints

Hints are **read-only** recommendations from the Console Prometheus window (and domain config). They never change TTLs, Version, or invalidation. Add a JSON pack, **Settings → Reload** — no recompile.

Details: [hints/README.md](../../src/CacheOrchestrator.AdminConsole/hints/README.md), [admin-hints.md](../admin-hints.md).

## Security (short)

- Admin API: private network; strong `Cache:Admin:ApiKey` (`X-Cache-Admin-Key`). Empty key + Enabled = open (dev only).
- Admin Console App: **no built-in login**. Put VPN / SSO / reverse-proxy auth in front of `/` and `/api`.
- Treat `dev-admin-key` as sample-only.
- Invalidate / Version / TTL are mutations.

Details: [admin.md](../admin.md#security).

## Health

`IHealthChecksBuilder.AddCacheOrchestrator()` runs backend probes (Redis pings the multiplexer). InMemory registers no external probe.

Local Admin `GET …/health` is a **separate** endpoint (HTTP 200 with `Healthy: false` when a cache probe fails). The Console maps that to Degraded.

Details: [observability.md](../observability.md#health-checks).
