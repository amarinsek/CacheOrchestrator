# Cluster command bus (`CacheOrchestrator.Bus`)

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — topologies](guide/topologies.md). Catalog: [documentation index](README.md). Canonical detail for membership, commands, and peer HTTP.

When several instances must apply the same invalidate, Version, or TTL change, this package delivers those **commands** over HTTP. It does not move cache payloads. Peers run the same local purge or overlay they would have run if the call had been made on that process.

Package README: [src/CacheOrchestrator.Bus/README.md](../src/CacheOrchestrator.Bus/README.md). See also [invalidation.md](invalidation.md), [deployment.md](deployment.md), [admin.md](admin.md), [configuration.md](configuration.md).

---

## When to use it

| Situation | Prefer |
|-----------|--------|
| Multi-instance **InMemory** OC/FC, need immediate purge everywhere | **Bus** |
| Runtime **Version / TTL** overlays on all InMemory nodes | **Bus** + Admin `distribute` (or Admin Console App auto mode) |
| Shared Redis L2 + backplane | **Redis package** — Bus optional / redundant for tag invalidate |
| Sticky sessions + TTL-only expiry | Local invalidation may be enough |
| Single instance | Do not install Bus (or leave `Enabled: false`) |

**Bus carries commands, not cache values.** Peers re-run local tag purge / override apply.

```text
Origin: Invalidate* / Admin distribute
   → local apply
   → IClusterCommandBus.PublishAsync → ClusterPublishResult (per-peer)
        → membership peers (except self)
        → POST {peer}{prefix}/cluster/apply
             → ApplyLocal only (no re-publish)
```

Peer HTTP/timeout failures are reported in `ClusterPublishResult` (not swallowed). Admin mutations with `distribute: true` return **409** when any peer failed; local apply may already have succeeded (`localApplied: true`).

---

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.Bus
```

## Register

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Bus;

builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddHttpClusterBus();
});

var app = builder.Build();
app.UseRouting();
app.UseCacheOrchestrator();
app.MapCacheOrchestratorHttpBus();
```

Receive endpoints are independent of the Admin API. Call `AddHttpClusterBus()` inside the `AddCacheOrchestrator` builder callback so it registers before the core Null defaults.

| API | Assembly / namespace |
|-----|----------------------|
| `AddHttpClusterBus` | `CacheOrchestrator.Bus` |
| `MapCacheOrchestratorHttpBus` | `CacheOrchestrator.Bus` |
| `IClusterCommandBus` / `IClusterMembership` / `IClusterCommandHandler` | `CacheOrchestrator.Cluster` (core; Null by default) |
| `IInstanceIdProvider` | `CacheOrchestrator.Cluster` |

Call `AddHttpClusterBus()` **inside** the `AddCacheOrchestrator` builder callback so it registers before core `TryAdd` Null defaults.

---

## Configuration

Root identity and bus options live under **`Cache`**:

```json
{
  "Cache": {
    "Namespace": "app1",
    "InstanceId": "app1-a",
    "Cluster": {
      "Bus": {
        "Enabled": true,
        "Membership": "Static",
        "PeerTimeoutMs": 2000,
        "MaxParallelism": 32,
        "DedupeWindowSeconds": 60,
        "ApiKey": "",
        "Static": {
          "Instances": [
            { "Id": "app1-a", "Url": "http://10.0.0.1:8080" },
            { "Id": "app1-b", "Url": "http://10.0.0.2:8080" }
          ]
        },
        "ServiceDiscovery": {
          "ServiceName": "app1",
          "DefaultScheme": "http",
          "CacheSeconds": 15
        }
      }
    },
    "Admin": {
      "Enabled": false,
      "ApiKey": "…",
      "RoutePrefix": "/cache-admin/local"
    }
  }
}
```

### Options reference (`Cache:Cluster:Bus`)

| Property | Default | Description |
|----------|---------|-------------|
| `Enabled` | `false` | Master switch; when false, `IClusterCommandBus.IsEnabled` is false (no peer calls) |
| `Membership` | `Null` | `Null` · `Static` · `ServiceDiscovery` |
| `PeerTimeoutMs` | `2000` | Per-peer HTTP timeout |
| `MaxParallelism` | `32` | Max concurrent peer deliveries |
| `DedupeWindowSeconds` | `60` | Receive-side `CommandId` window; `0` disables |
| `ApiKey` | empty | Auth for receive endpoints; falls back to `Cache:Admin:ApiKey` |
| `Static.Instances` | `[]` | `{ Id, Url }` peers (Static membership) |
| `ServiceDiscovery.ServiceName` | empty | Logical service name for SD |
| `ServiceDiscovery.DefaultScheme` | `http` | Scheme when building peer URLs / query |
| `ServiceDiscovery.CacheSeconds` | `15` | In-process peer list cache |

### `Cache:InstanceId`

Single process identity for Admin, bus anti-echo, and diagnostics.  
When empty, the machine name is used. Process identity lives at `Cache:InstanceId`.

### `Cache:Namespace`

Isolation boundary for keys **and** cluster commands. Namespace mismatch → **409**. Origin-is-self (anti-echo) → **200** `{ applied: false, reason: "origin-is-self" }`.  
Namespace is **not** membership discovery — do not mix app1 and app2 peers in one Static list.

---

## Membership

| Kind | Package | Behaviour |
|------|---------|-----------|
| **Null** | core | Empty peers; local only |
| **Static** | Bus | Peer URLs from `Cluster:Bus:Static` |
| **ServiceDiscovery** | Bus | `Microsoft.Extensions.ServiceDiscovery` (config, platform DNS, Aspire, …) |

### Static (VM / IIS / simple LAN)

List every instance **of this app**. `Id` should match each process’s `Cache:InstanceId` so the origin can skip itself.

### ServiceDiscovery (K8s / Aspire / config endpoints)

```json
"Membership": "ServiceDiscovery",
"ServiceDiscovery": {
  "ServiceName": "app1",
  "DefaultScheme": "http"
}
```

Host configuration should expose endpoints, for example:

```json
{
  "Services": {
    "app1": {
      "http": [ "10.0.0.1:8080", "10.0.0.2:8080" ]
    }
  }
}
```

Bare names are normalized to `http://{ServiceName}` so the configuration endpoint provider matches (scheme-required query).  
Peer `Id` values are synthetic (`app1-0`, …); anti-echo still uses `OriginInstanceId` on receive.

---

## Commands

Wire type is polymorphic JSON (`commandType` discriminator):

| `commandType` | Type | ApplyLocal effect |
|---------------|------|-------------------|
| `invalidate` | `InvalidateCommand` | Domain / entity / tags via invalidator |
| `versionBump` | `VersionBumpCommand` | Runtime Version overlay |
| `ttlPatch` | `TtlPatchCommand` | Runtime TTL overlay (legacy TTL-only patch) |
| `settingsPatch` | `SettingsPatchCommand` | Sparse runtime overlay (same shape as Admin `PATCH …/domains/{d}/settings`) |

Never carries response bodies or cache entries.

### Who publishes

| Entry point | Publish? |
|-------------|----------|
| `ICacheOrchestratorInvalidator.Invalidate*` | **Yes** when bus enabled (unless remote/local-only scope) |
| Admin `POST …/invalidate` | Only if body `distribute: true` |
| Admin `POST …/domains/{d}/version` | Only if body `distribute: true` |
| Admin `PATCH …/domains/{d}/settings` | `distribute: true` → `settingsPatch`, **except** TTL-only patches (`IsTtlOnly`) which still send **`ttlPatch`** so older peers apply |
| Admin `PATCH …/domains/{d}/ttl` | Obsolete TTL-only; `distribute: true` → `ttlPatch` |
| Peer `POST …/cluster/apply` | **Never** (ApplyLocal only) |

`ClusterCommandScope`:

- **Remote** — receive path; suppresses re-publish  
- **LocalOnly** — Admin without distribute; local apply only  

---

## HTTP endpoints

Base path = `Cache:Admin:RoutePrefix` (default `/cache-admin/local`), **even if Admin is disabled**.

| Method | Path | Role |
|--------|------|------|
| `POST` | `…/cluster/apply` | ApplyLocal command body |
| `GET` | `…/cluster/info` | instance id, namespace, bus enabled, membership, peer count. When **Admin is enabled**, Local Admin maps this route (Bus does not duplicate it). When Admin is off, Bus maps it. |

### Auth

Header **`X-Cache-Admin-Key`** (same as Admin API):

1. `Cache:Cluster:Bus:ApiKey` if set  
2. Else `Cache:Admin:ApiKey`  
3. Else open (dev only; warning log)

Prefer private network and/or mTLS in front of instances for production.

### Partial failure

Origin local result is **not** failed if a peer times out. Peer errors are logged + `publish_failures` metric.

---

## Admin Console App interaction

The Admin Console App probes `GET …/cluster/info` on configured instances (`GET /api/distribution`):

| Capability | Write mode |
|------------|------------|
| No bus | **fan-out** — HTTP to each target, `distribute: false` |
| Bus enabled | **bus-distribute** — one healthy origin, `distribute: true` |

Never combine full Admin Console App fan-out **and** `distribute: true` for the same action — the App chooses one path.  
Operations UI shows mode banner + last-run summary.

Details: [admin.md](admin.md#cluster-distribute-with-cacheorchestratorbus).

---

## Bus vs Redis backplane

| Concern | Redis L2 + backplane | HTTP Bus |
|---------|----------------------|----------|
| Shared object store | Yes (L2) | No |
| L1 drop on other nodes after tag remove | Yes (pub/sub) | Yes (command → local purge) |
| InMemory multi-instance | Not applicable | Primary tool |
| Runtime Version/TTL overlay cluster-wide | Not covered | Yes |
| Hot-path read latency | L2 cost | No read path cost |

Using **both** for tag invalidation is safe (idempotent double purge) but often unnecessary for Fusion when Redis backplane is already on. Bus remains useful for OC InMemory + Version / TTL / **settings** overlays. The Redis backplane does **not** distribute runtime overlays.

---

## Observability

Meter: **`CacheOrchestrator`**

| Instrument | Description |
|------------|-------------|
| `cache_orchestrator.cluster.commands_published` | Origin publish attempts (`command_type` is the **CLR name**: `InvalidateCommand`, `VersionBumpCommand`, `TtlPatchCommand`, `SettingsPatchCommand`) |
| `cache_orchestrator.cluster.commands_received` | Receive path entered |
| `cache_orchestrator.cluster.commands_applied` | ApplyLocal success |
| `cache_orchestrator.cluster.publish_failures` | Per-peer failure (`reason`: `http_status` / `timeout` / `transport` / `exception`) |
| `cache_orchestrator.cluster.command_dedupe_hits` | Duplicate `CommandId` within window |

Logs: command type, namespace, origin, commandId (Debug for ignore paths; Warning for peer failures).

See [observability.md](observability.md).

---

## Zero effect / performance

| Condition | Behaviour |
|-----------|-----------|
| Bus package not referenced | Null bus only |
| Package present, no `AddHttpClusterBus()` | Null bus |
| `Enabled: false` | No peer HTTP |
| GetOrSet / Output Cache policy | Never touch bus |

When enabled: `IHttpClientFactory`, parallel peer posts, per-peer timeout, capped parallelism, small JSON commands.

---

## Security checklist

- [ ] Set `Cluster:Bus:ApiKey` or `Admin:ApiKey` outside local dev  
- [ ] Restrict peer HTTP to private networks / mesh  
- [ ] Treat apply endpoints as admin-level (can purge cache)  
- [ ] Do not expose Admin API / cluster routes on the public internet without auth  

---

## Related

- [Guide — topologies](guide/topologies.md) — Bus vs Redis backplane  
- [invalidation.md](invalidation.md) — multi-instance strategies  
- [deployment.md](deployment.md) — topologies  
- [admin.md](admin.md) — Admin API + Admin Console App  
- [configuration.md](configuration.md) — options tables  
- [backends.md](backends.md) — Redis package  
- [architecture.md](architecture.md) — layout  
