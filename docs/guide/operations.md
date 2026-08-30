# Operations

> **Guide** — diagnostics, Admin API vs Console App, and day-2 ops.

Operating a cache means answering three questions quickly:

1. Which layer served this request?
2. Is the observed behaviour expected for the domain?
3. If state must change now, which entries and instances should be affected?

Start with one response, move to aggregate telemetry, and use Admin mutations only after you know the intended scope.

## Table of Contents

- [Read one request with `X-Cache`](#read-one-request-with-x-cache)
- [Use metrics for trends](#use-metrics-for-trends)
- [Use logs and traces for the reason](#use-logs-and-traces-for-the-reason)
- [Probe backend health](#probe-backend-health)
- [Choose the Admin surface](#choose-the-admin-surface)
- [Match every mutation to its scope](#match-every-mutation-to-its-scope)
- [Confirm the instance boundary](#confirm-the-instance-boundary)
- [Security checklist](#security-checklist)
- [Use a short incident checklist](#use-a-short-incident-checklist)

## Read one request with `X-Cache`

Domain endpoints emit `X-Cache` by default:

```http
X-Cache: domain=catalog; version=v1; client=public; phase=n/a; oc=miss; dc=miss; fa=run; ms=12
```

| Token | Values | What it tells you |
|-------|--------|-------------------|
| `domain` | normalized name, or `_` | Resolved domain; `_` when the domain could not be resolved |
| `version` | domain `Version` stamp, or `-` | Always present; `-` when unresolved |
| `client` | `public` / `private` / `no-store` / `blocked` | Client Cache-Control class applied to the response |
| `phase` | `calm` / `approaching` / `hold` / `n/a` | Client Cache Schedule phase (always present) |
| `oc` | `hit` / `miss` / `bypass` / `off` | Output Cache outcome |
| `dc` | `hit` / `miss` / `stale` / `bypass` / `off` / `unresolved` / `n/a` | Data Cache outcome. `n/a` when no Data Cache operation ran. Omitted on an Output Cache `hit`. |
| `fa` | `run` | Application/origin work produced the result. Omitted on an Output Cache `hit` and on `dc=hit`. |
| `ms` | integer milliseconds | Wall-clock of the timed server path (Data Cache get-or-set, including L1/L2 `dc=hit`, or direct origin when `dc=n/a`). Omitted on `oc=hit`. Not factory-only — see [observability.md](../reference/observability.md). |

Common flows:

| Header shape | Interpretation |
|--------------|----------------|
| `oc=hit` and no `dc` | Output Cache returned the HTTP response; the endpoint did not run |
| `oc=miss; dc=hit` | Endpoint ran, but the object came from Data Cache |
| `oc=miss; dc=miss; fa=run` | Both server layers missed and the database/service factory ran |
| `oc=miss; dc=n/a; fa=run` | The application generated the response directly; no Data Cache operation was used |
| `oc=bypass; dc=bypass; fa=run` | Policy deliberately bypassed caching, often for authenticated traffic |
| `dc=unresolved; fa=run` | Data Cache call could not resolve a domain and ran uncached |

Use `curl` or disable the browser cache while diagnosing. If a browser serves its own fresh copy, the request never reaches the application and no new server header is produced.

Set `Cache:EmitDiagnosticsHeaders` to `false` when exposing domain names and cache state to public clients is undesirable. Metrics, traces, logs, `Cache-Control`, and ETags continue to work.

Full field semantics: [Observability](../reference/observability.md#x-cache-response-header).

## Use metrics for trends

The meter name is `CacheOrchestrator`. Export it through OpenTelemetry to Prometheus or another metrics backend.

Start with these signals:

| Signal | Operational question |
|--------|----------------------|
| `cache_orchestrator.oc.requests` | Is Output Cache serving the expected share of requests? |
| `cache_orchestrator.dc.requests` | Is the Data Cache hitting, missing, going stale, or bypassing? |
| `cache_orchestrator.factory.runs` | How often did application/origin work run, including endpoints without Data Cache? |
| `cache_orchestrator.factory.failures` | How often did that work fail? |
| `cache_orchestrator.factory.duration` | How expensive is the database or service work on factory runs? |
| `cache_orchestrator.factory.result_size` | Are factories returning unexpectedly large cacheable values? |
| `cache_orchestrator.client.schedule` | Which domains are Calm, Approaching, or in Hold? |
| `cache_orchestrator.invalidate` | Are invalidations occurring at the expected scope? |
| `cache_orchestrator.cluster.*` | Are bus commands published, received, applied, or failing? |

By default, request instruments include a stable route-template label rather than raw resource paths. Disable `Cache:Metrics:IncludeEndpointLabel` if that cardinality is still too high, and keep the choice consistent across instances.

Admin Console App traffic charts require Prometheus. The Admin API `/stats` endpoint contains process-lifetime diagnostics and is not the source for time-window analytics.

## Use logs and traces for the reason

Metrics show that a miss or failure increased. Logs and activities explain a particular path.

- Cache hits and misses are logged at Debug.
- An unresolved Data Cache domain is a Warning.
- Stale results, backend failures, partial invalidation failures, and cluster publish failures appear at higher levels.
- Activity source `CacheOrchestrator` emits Data Cache, Output Cache hit, and invalidation activities.

Do not enable verbose cache logging indefinitely in a high-traffic environment. Prefer metrics for the baseline and raise log detail for the domain or incident window you are investigating. Category names and `appsettings` fine-tuning: [Observability — Logging](../reference/observability.md#logging).

## Probe backend health

Register the CacheOrchestrator health check with ASP.NET Core:

```csharp
builder.Services.AddHealthChecks()
    .AddCacheOrchestrator();
```

Active backend registrars contribute their probes. The Redis integration pings its connection; in-memory providers have no external dependency to probe. The default failure status is `Degraded`.

This health check answers whether the configured backend can respond. It does not prove that hit rates, TTLs, vary rules, or multi-instance invalidation are correct.

The Admin API `/health` endpoint is a separate operational projection and returns its own `Healthy` field. See [Health checks](../reference/observability.md#health-checks) for the distinction.

## Choose the Admin surface

CacheOrchestrator has two operator-facing pieces:

| Surface | Use it for | Where it runs |
|---------|------------|---------------|
| **Admin API** | Scripts, probes, domain inspection, invalidation, and runtime settings changes on an app | Opt-in routes in each ASP.NET Core process |
| **Admin Console App** | One UI that inspects and controls several app instances | Separate `net10.0` process or container |

The Admin API is disabled by default. Enable and map it on every application instance that should be managed:

```json
{
  "Cache": {
    "InstanceId": "catalog-1",
    "Admin": {
      "Enabled": true,
      "ApiKey": "read-this-from-a-secret-provider"
    }
  }
}
```

```csharp
app.UseCacheOrchestrator();
app.MapCacheOrchestratorAdmin();
```

Use the Admin API for automation. Add the Admin Console App when operators need instance fan-out, domain comparisons, hints, and a visual workflow. The Admin Console App is not a NuGet package and is never on the end-user caching path.

Setup and endpoint reference: [Admin](../reference/admin.md). Container runbook: [Deploy Admin Console App](../../deploy/admin/README.md).

## Match every mutation to its scope

Prefer the narrowest operation that restores correctness:

| Change | Operation |
|--------|-----------|
| One product changed | Invalidate that entity |
| A cached collection depends on changed members | Invalidate through its entity footprint |
| Every product representation changed | Invalidate the entity kind |
| The whole domain is suspect | Invalidate the domain |
| A new immutable dataset or representation generation shipped | Change domain `Version` |
| Temporary operational tuning is required | Apply a runtime settings overlay |

Broad invalidation increases origin load and can create a cold-cache event. A version change also moves the whole domain to empty key space. Use either deliberately and monitor factory rate and duration while the cache warms.

Runtime overlays are operational state, not a replacement for durable application configuration. Reconcile a permanent change into the deployed configuration so restarts and new instances receive the intended value.

## Confirm the instance boundary

Before running an invalidation or settings change, identify the topology:

- In a single process, local apply is the whole deployment.
- Redis Output Cache uses a shared response store.
- FusionCache Redis L2 plus backplane purges shared data and peer L1 entries.
- In-memory peer stores require HttpBus commands or Admin Console App fan-out.
- Runtime Version, TTL, and settings overlays do not travel through the FusionCache backplane.

With HttpBus enabled, programmatic invalidation publishes to peers after local apply. Admin mutations distribute only when `distribute: true` is requested. A peer failure can leave the origin changed while one or more peers remain unchanged; there is no automatic rollback.

Treat a partial multi-instance mutation as an incident: record which instances applied it, restore reachability, and reconcile the missing nodes before assuming the cluster is consistent.

See [Topologies](topologies.md) and [Cluster bus](../reference/cluster-bus.md) for the complete matrix.

## Security checklist

> [!IMPORTANT]
> Admin operations can evict cache entries and change live policy.
>
>  [ ] Keep Admin API and bus endpoints on a private network.
>  [ ] Set strong API keys through a secret provider; never commit them to configuration files.
>  [ ] Put VPN, SSO, or authenticated reverse-proxy access in front of the Admin Console App. It has no built-in user login.
>  [ ] Use TLS between operators, Admin Console App, and application instances.
>  [ ] Restrict Prometheus and diagnostic headers when their labels reveal sensitive deployment details.
>  [ ] Audit invalidation and settings mutations outside the cache process when required.
>
> An enabled Admin API with an empty key is suitable only for isolated local development and produces a warning.

## Use a short incident checklist

When cached output appears wrong:

1. Reproduce with the browser cache disabled and capture `X-Cache`.
2. Confirm the resolved `domain`, client policy, and schedule phase.
3. Decide whether the response came from Client Cache, Output Cache, Data Cache, or the factory.
4. Check auth and vary inputs before assuming the stored value is wrong.
5. Compare the domain `Version` and effective settings across instances.
6. Inspect backend health, invalidation failures, and cluster publish failures.
7. Apply the narrowest safe invalidation.
8. Verify the next request and watch factory load while entries refill.
9. Fix the write path, vary rule, configuration, or deployment cause—not only the stale entry.

For symptom-specific answers, continue to the [FAQ](faq.md). For full telemetry definitions, use [Observability](../reference/observability.md).
