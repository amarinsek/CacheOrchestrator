# CacheOrchestrator.Admin

Admin Console for multi-instance CacheOrchestrator: live stats, domain settings, invalidation, Version/TTL, **time-series Metrics**, and **recommendation Hints**.

![Admin App overview](../../docs/assets/admin-overview.png)

It calls the **Admin API** on each instance you list (`Cache:Admin:Enabled`, `MapCacheOrchestratorAdmin`).

This host is **not** a NuGet package. It targets **.NET 10** only.  
Monitored app instances may still run on **.NET 8** or **.NET 10** — Admin talks **HTTP only**, so Admin TFM does not need to match instance TFMs.

| Guide | |
|-------|--|
| This README | Run / configure this host |
| **[hints/README.md](hints/README.md)** | **How to write and add custom hint rules** (ships next to the rule packs) |
| [docs/admin.md](../../docs/admin.md) | Architecture, security, Metrics store |
| [docs/admin-hints.md](../../docs/admin-hints.md) | Repo overview of the hints feature |

---

## Enable the Admin API on each instance

```json
"Cache": {
  "InstanceId": "app-1",
  "Admin": {
    "Enabled": true,
    "ApiKey": "dev-admin-key"
  }
}
```

```csharp
app.MapCacheOrchestratorAdmin();
```

---

## Configure this host

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
    ],
    "Metrics": {
      "Enabled": true,
      "Provider": "Prometheus",
      "BaseUrl": "http://localhost:9090"
    },
    "Hints": {
      "RuleFiles": [ "hints/*.json" ],
      "DisabledCodes": []
    }
  }
}
```

- **ApiKey** is sent as `X-Cache-Admin-Key` (must match each instance).  
- **Instances[].url** is the application base URL only.  
- **LocalPathPrefix** must match `Cache:Admin:RoutePrefix`.  
- Production keys belong in a secret store; put VPN/SSO in front of this host.  
- Invalidate / Version / TTL change live cache state — see [docs/admin.md — Security](../../docs/admin.md#security).

---

## Run

```bash
dotnet run --project src/CacheOrchestrator.Admin

# Publish
dotnet publish src/CacheOrchestrator.Admin -c Release -o ./publish/admin
```

Requires a **.NET 10** runtime/SDK for this host.

- http://localhost:5188/ — UI  
- http://localhost:5188/health — process health  
- http://localhost:5188/scalar/v1 — OpenAPI UI (Development; `MapOpenApi` + Scalar)

Default `Instances` point at the **Playground** sample (`:5289`), which also exposes `/metrics` for Prometheus.

**Pages:** Overview · Instances · Domains · Endpoints · **Metrics** · **Hints** · Operations · **Settings**

---

## Recommendation hints

The Admin App evaluates **read-only rules** against aggregated live stats (and domain config). Results appear as severity badges, the **Hints** page, and header chips.

| Capability | |
|------------|--|
| Product defaults | `hints/core-hints.json` (always loaded) |
| **Your rules** | Extra JSON packs under `hints/` via `CacheAdmin:Hints:RuleFiles` |
| No recompile | Drop a file, **Settings → Reload** |
| Disable codes | Settings checkboxes, or `DisabledCodes` in config |
| Inspect a rule | Settings → click a row → JSON definition |
| Compile errors | Settings shows an **ERROR** card with rule code + path inside the rule |

**Customization is the normal path** for team thresholds and extra checks—prefer a pack such as `hints/team-ops.json` over editing `core-hints.json` (cleaner product upgrades).

### Minimal custom pack

```json
{
  "name": "team-ops",
  "rules": [
    {
      "code": "team-high-factory",
      "severity": "Warning",
      "category": "Factory",
      "scope": "domain",
      "enabled": true,
      "when": {
        "all": [
          { "path": "domain.requests", "op": ">=", "value": 20 },
          { "path": "domain.fc.factoryShare", "op": ">=", "value": 0.30 }
        ]
      },
      "message": "Factory is {domain.fc.factoryShare:p1} on {domain.name}."
    }
  ]
}
```

Save as `hints/team-ops.json` with `"RuleFiles": [ "hints/*.json" ]`, open **Settings**, confirm the group, then generate traffic and check **Hints**.

**Full operator handbook** (format, paths, ops, disable, design checklist):

→ **[hints/README.md](hints/README.md)**

Repo architecture notes: [docs/admin-hints.md](../../docs/admin-hints.md).

---

## Metrics (Prometheus)

```json
"CacheAdmin": {
  "Metrics": {
    "Enabled": true,
    "Provider": "Prometheus",
    "BaseUrl": "http://localhost:9090"
  }
}
```

```bash
docker compose -f samples/CacheOrchestrator.Sample/deploy/prometheus/docker-compose.yml up -d
dotnet run --project samples/CacheOrchestrator.Sample
dotnet run --project src/CacheOrchestrator.Admin
```

Guide: [sample Prometheus (dev only)](../../samples/CacheOrchestrator.Sample/deploy/prometheus/README.md) · [docs/admin.md](../../docs/admin.md).

---

## Further reading

- [docs/admin.md](../../docs/admin.md) — fan-out, security, Metrics store  
- [docs/admin-hints.md](../../docs/admin-hints.md) — hints feature in the monorepo  
- **[hints/README.md](hints/README.md)** — writing custom rules  
- [docs/cluster-bus.md](../../docs/cluster-bus.md) — multi-instance bus  
