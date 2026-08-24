# Operations

> **Guide.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md).

A map of where to look when you need to see cache behaviour or change live state. Details live on the reference pages.

## Documents

| Document | Open it when |
|----------|----------------|
| [Observability](../reference/observability.md) | `X-Cache`, meters, health, logs |
| [Admin](../reference/admin.md) | Admin API + Console architecture, security, endpoints |
| [Admin Console README](../../src/CacheOrchestrator.AdminConsole/README.md) | `dotnet run`, host config, UI pages |
| [deploy/admin](../../deploy/admin/README.md) | Docker / GHCR, volumes, logs |
| [Admin hints](../contributor/admin-hints.md) | How recommendation hints work |
| [Writing hint rules](../../src/CacheOrchestrator.AdminConsole/hints/README.md) | JSON packs for the Console |
| [Cluster bus](../reference/cluster-bus.md) | Invalidate / Version / TTL on every instance |

## See a request: `X-Cache`

On domain endpoints (default `EmitDiagnosticsHeaders: true`):

```http
X-Cache: domain=catalog; version=1; client=public; phase=n/a; oc=miss; dc=miss; fa=run; ms=12
```

`oc` / `dc` are Output Cache and data-cache dispositions; `phase` is Client Cache Schedule. Hide the header with `"EmitDiagnosticsHeaders": false` — metrics continue.

Field reference: [observability](../reference/observability.md). Production tip: [FAQ](faq.md#should-i-expose-x-cache-in-production).

## Admin API vs Admin Console App

| Piece | What it is | Where it runs |
|-------|------------|----------------|
| **Admin API** | Opt-in HTTP on **each** app | AspNetCore package; off by default |
| **Admin Console App** | Fan-out UI across instances | Separate process / Docker; not a NuGet package |

Use the API for curl/scripts (health, config, invalidate, Version, PATCH settings). Use the Console for a multi-instance dashboard. **Console traffic stats need Prometheus.**

Details: [admin](../reference/admin.md) · [FAQ](faq.md#admin-api-vs-admin-console-app).

## Docker vs local Console

| How you run it | Config | Custom hints | Disabled codes |
|----------------|--------|--------------|----------------|
| `dotnet run` (Development) | Playground defaults | `hints/*.json` | `hints/disabled.local.json` |
| Docker / Production | Mount instance list + ApiKey | `data/rules/*.json` | `data/disabled.local.json` |

Product pack `hints/core-hints.json` is always loaded. Do not mount over all of `/app/hints`.

Image: `ghcr.io/amarinsek/cacheorchestrator-admin-console`.

## Security (short)

- Admin API: private network; strong `Cache:Admin:ApiKey` (`X-Cache-Admin-Key`).
- Admin Console: **no built-in login** — put VPN / SSO / reverse-proxy auth in front.
- Invalidate / Version / TTL are mutations.

Details: [admin — security](../reference/admin.md#security).

## Health

`IHealthChecksBuilder.AddCacheOrchestrator()` runs backend probes. Details: [observability — health](../reference/observability.md#health-checks).
