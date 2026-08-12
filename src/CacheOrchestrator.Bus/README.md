# CacheOrchestrator.Bus

Optional **cluster command bus** for [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/).

| | |
|--|--|
| **Provides** | HTTP fan-out of invalidate / version-bump / TTL-patch commands across instances |
| **Requires** | `CacheOrchestrator` core + `AddHttpClusterBus()` + `MapCacheOrchestratorHttpBus()` |
| **Does not** | Replace Redis Fusion backplane for L1/L2 coherence |

Without this package the core uses a **Null** bus (zero effect on the hot path).

Programmatic `Invalidate*` always publishes when the bus is **enabled**. Local Admin uses `distribute: true` to publish; default is local-only.

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

// After UseRouting / UseCacheOrchestrator:
app.MapCacheOrchestratorHttpBus();
```

## Configure

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
        "Static": {
          "Instances": [
            { "Id": "app1-a", "Url": "http://10.0.0.1:8080" },
            { "Id": "app1-b", "Url": "http://10.0.0.2:8080" }
          ]
        }
      }
    }
  }
}
```

Auth for `POST .../cluster/apply`: header `X-Cache-Admin-Key` using `Cache:Cluster:Bus:ApiKey`, or fallback `Cache:Admin:ApiKey`.

Receive endpoints are mapped even when Local Admin is disabled.
